using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Population;

namespace Meitou.Struggle;

/// <summary>A struggle the player chose for <paramref name="Victim"/> (from its or its carrier's right-click menu).</summary>
public sealed record StruggleOrder(CharacterId Victim) : SimCommand;

/// <summary>A carried character's offer for the menu: its carrier, its chance (percent) and the seconds of cooldown left.</summary>
readonly record struct Offer(CharacterId Carrier, float Chance, int Cooldown);

/// <summary>
/// The original's struggle (<b>Verified</b> decompile of <c>Struggle.dll</c> and its description): a carried character that is alive, conscious and
/// whose head is not below 0 compares its current Strength + Toughness with its carrier's: 50 % at equal sums, plus the step per point of
/// difference; an NPC adds its personality's points; held to the side's minimum and maximum. Success lets go of it where the carrier stands
/// (<see cref="CarrySystem.LetGo"/>). Failure with punishment on is a slap to the head and a knock-out of carrier STR x 20 / victim TGH seconds,
/// held to the knock-out range and at most 30 s (a longer one already running is kept). A player's character waits its cooldown after a failure;
/// an NPC (with NPC struggle on) tries by itself whenever its own cooldown has run, after every try. Serial phases only.
/// </summary>
sealed class StruggleSystem(PopulationData population, GameDatabase db, RngPurpose purpose) : ITickSystem, IStateHashed
{
    /// <summary>The original's hard cap on a failure's knock-out, seconds.</summary>
    const float KnockOutCap = 30;

    /// <summary>The attribute sum past which an NPC's base cooldown is halved, and the cooldown's floor.</summary>
    const float SharpSum = 240, LeastNpcCooldown = 5;

    /// <summary>The tick before which each character may not struggle again.</summary>
    readonly Dictionary<CharacterId, long> _readyAt = [];

    readonly List<StruggleOrder> _orders = [];

    CarrySystem? _carry;

    volatile FrozenDictionary<CharacterId, Offer> _offers = FrozenDictionary<CharacterId, Offer>.Empty;

    /// <summary>The carried characters' offers, by victim; rebuilt every 10 ticks, safe to read from the interface thread.</summary>
    public FrozenDictionary<CharacterId, Offer> Offers => _offers;

    /// <summary>The lines for the player, read by the interface.</summary>
    public ConcurrentQueue<(Message Message, float Chance, int Seconds)> Messages { get; } = new();

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is StruggleOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        var settings = StruggleMod.Settings;
        if (settings.Enabled)
        {
            foreach (var order in _orders) Attempt(world, order.Victim, auto: false);
            if (settings.NpcStruggle && world.Tick % 15 == 0) NpcTurns(world);
        }

        _orders.Clear();
        if (world.Tick % 10 == 0) Publish(world);
    }

    /// <summary>One try. A player's try that cannot be made says why.</summary>
    void Attempt(World world, CharacterId victim, bool auto)
    {
        var table = world.Characters;
        if (!table.IsAlive(victim) || table.Cold(victim.Slot) is not { } cold || !table.IsAlive(cold.CarriedBy)
            || table.Cold(cold.CarriedBy.Slot) is not { Carrying: var carried } carrier || carried != victim)
        {
            Tell(auto, Message.Unavailable);
            return;
        }

        if (Refusal(cold) is { } refusal)
        {
            Tell(auto, refusal);
            return;
        }

        int left = CooldownLeft(world, victim);
        if (left > 0)
        {
            Tell(auto, Message.Cooldown, seconds: left);
            return;
        }

        Resolve(world, victim, cold, cold.CarriedBy, carrier, auto);
    }

    /// <summary>The roll and its outcome.</summary>
    void Resolve(World world, CharacterId victim, CharacterCold cold, CharacterId carrierId, CharacterCold carrier, bool auto)
    {
        var settings = StruggleMod.Settings;
        bool npc = !cold.IsPlayer;
        float chance = Chance(world, victim, cold, carrier, npc);
        float roll = Rng.Float(Rng.Hash(world.Seed, Rng.Key(victim), purpose, (ulong)world.Tick)) * 100;
        if (npc) _readyAt[victim] = world.Tick + Ticks(world, NpcCooldown(world, victim, cold));

        if (roll < chance)
        {
            bool released = Carry(world)?.LetGo(world, carrierId.Slot) ?? false;
            Tell(auto, released ? Message.Success : Message.NotReleased, chance);
            return;
        }

        if (!npc) _readyAt[victim] = world.Tick + Ticks(world, settings.BaseCooldownSec + settings.FailExtraSec);
        if (!settings.FailPunish)
        {
            Tell(auto, Message.Failed);
            return;
        }

        Punish(world, victim, cold, carrier);
        Tell(auto, Message.Slapped);
    }

    /// <summary>The chance in percent: 50 + step x (victim's STR + TGH - carrier's), an NPC's personality points added, held to the side's range.</summary>
    float Chance(World world, CharacterId victim, CharacterCold cold, CharacterCold carrier, bool npc)
    {
        var settings = StruggleMod.Settings;
        float difference = Current(cold, StatsEnumerated.Strength) + Current(cold, StatsEnumerated.Toughness)
            - Current(carrier, StatsEnumerated.Strength) - Current(carrier, StatsEnumerated.Toughness);
        float chance = 50 + settings.StepPerPoint * difference;
        if (!npc) return Math.Clamp(chance, settings.MinChance, Math.Max(settings.MinChance, settings.MaxChance));

        chance += Traits(PersonalityOf(world, victim, cold)).Points;
        return Math.Clamp(chance, settings.NpcMinChance, Math.Max(settings.NpcMinChance, settings.NpcMaxChance));
    }

    /// <summary>Why the character cannot struggle (dead, out cold or its head below 0), or null.</summary>
    static Message? Refusal(CharacterCold cold)
    {
        if (cold.Medical is not { Dead: false } medical) return Message.Unavailable;
        if (medical.Unconscious) return Message.Unconscious;

        int head = Head(medical);
        return head >= 0 && medical.Parts[head].IsDown ? Message.HeadDown : null;
    }

    /// <summary>The head's part index, or -1.</summary>
    static int Head(MedicalState medical)
    {
        for (int i = 0; i < medical.Parts.Count; i++)
        {
            if (medical.Parts[i].Template.Type == BodyPartType.Head) return i;
        }

        return -1;
    }

    /// <summary>The slap: the head takes the slap damage as blunt, then the knock-out (a longer running one is kept).</summary>
    void Punish(World world, CharacterId victim, CharacterCold cold, CharacterCold carrier)
    {
        var settings = StruggleMod.Settings;
        var medical = cold.Medical!;
        int head = Head(medical);
        if (cold.Race is { } race && head >= 0 && settings.SlapDamage > 0)
        {
            var context = new MedicalContext(population.Bodies.Constants, population.BodyOptions, race)
            {
                Toughness = cold.Stats?.ScaledToughness ?? 0,
                Strength = cold.Stats?.Strength ?? 50,
                HasStats = cold.Stats is not null,
                Seed = world.Seed,
                CharacterKey = Rng.Key(victim),
            };
            medical.ApplyHit(head, new HitDamage(0, settings.SlapDamage, 0), context);
        }

        float toughness = Math.Max(Current(cold, StatsEnumerated.Toughness), 1);
        float longest = Math.Min(Math.Max(settings.KoMinSec, settings.KoMaxSec), KnockOutCap);
        float seconds = Math.Clamp(Current(carrier, StatsEnumerated.Strength) * 20 / toughness, Math.Min(settings.KoMinSec, longest), longest);
        medical.KnockOut(seconds);
    }

    /// <summary>NPC struggle: every carried character not of the player's, ready and able, tries.</summary>
    void NpcTurns(World world)
    {
        var table = world.Characters;
        var victims = new List<CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is { CarriedBy.IsNone: false, IsPlayer: false } cold && Refusal(cold) is null) victims.Add(table.IdOf(slot));
        }

        foreach (var victim in victims)
        {
            if (CooldownLeft(world, victim) <= 0) Attempt(world, victim, auto: true);
        }
    }

    /// <summary>An NPC's cooldown: the base, halved past 240 STR + DEX + TGH + PER, times its personality's factor, at least 5 s.</summary>
    float NpcCooldown(World world, CharacterId id, CharacterCold cold)
    {
        float sum = Current(cold, StatsEnumerated.Strength) + Current(cold, StatsEnumerated.Dexterity) + Current(cold, StatsEnumerated.Toughness)
            + Current(cold, StatsEnumerated.Perception);
        float seconds = StruggleMod.Settings.NpcCooldownSec * (sum > SharpSum ? 0.5f : 1);
        return Math.Max(LeastNpcCooldown, seconds * Traits(PersonalityOf(world, id, cold)).Cooldown);
    }

    /// <summary>The personality's cooldown factor and chance points (the original's table; an unknown tag changes nothing).</summary>
    static (float Cooldown, float Points) Traits(int tag) => tag switch
    {
        2 => (0.9f, 5),      // traitorous
        5 => (1.25f, 10),    // smart
        6 => (0.75f, -10),   // dumb
        9 => (0.75f, 10),    // brave
        10 => (1.5f, -10),   // fearful
        11 => (1.25f, -5),   // warm / kind
        12 => (0.9f, 5),     // cold / cruel
        14 => (0.5f, 15),    // maniacal
        _ => (1, 0),         // honorable, normal, unknown
    };

    int PersonalityOf(World world, CharacterId id, CharacterCold cold)
    {
        string faction = population.Relations.Contains(cold.Faction) ? population.Factions[cold.Faction].Id : "";
        return Personality.Of(world, db, id, cold, faction);
    }

    /// <summary>A current attribute: stored, times the stats' scale and the injuries' multiplier.</summary>
    static float Current(CharacterCold c, StatsEnumerated stat) =>
        c.Stats is { } s && c.Medical is { } m ? s[stat] * s.Scale * m.StatMultiplier(stat) : 0;

    int CooldownLeft(World world, CharacterId id)
    {
        if (!_readyAt.TryGetValue(id, out long ready) || ready <= world.Tick) return 0;

        return (int)MathF.Ceiling((float)((ready - world.Tick) * world.TickSeconds));
    }

    static long Ticks(World world, float seconds) => (long)MathF.Ceiling(seconds / world.TickSeconds);

    void Tell(bool auto, Message message, float chance = 0, int seconds = 0)
    {
        if (!auto) Messages.Enqueue((message, chance, seconds));
    }

    CarrySystem? Carry(World world) => _carry ??= world.Systems.OfType<CarrySystem>().FirstOrDefault();

    /// <summary>The offers of the carried characters the menu may show (the player's own, or any without Your characters only).</summary>
    void Publish(World world)
    {
        var settings = StruggleMod.Settings;
        var table = world.Characters;
        var offers = new Dictionary<CharacterId, Offer>();
        for (int slot = 0; slot < table.HighWater && settings.Enabled; slot++)
        {
            if (table.Cold(slot) is not { CarriedBy.IsNone: false } cold || (settings.PlayerOnly && !cold.IsPlayer)) continue;
            if (!table.IsAlive(cold.CarriedBy) || table.Cold(cold.CarriedBy.Slot) is not { } carrier || carrier.IsPlayer) continue;

            var id = table.IdOf(slot);
            offers[id] = new(cold.CarriedBy, Chance(world, id, cold, carrier, !cold.IsPlayer), CooldownLeft(world, id));
        }

        _offers = offers.ToFrozenDictionary();
        foreach (var id in _readyAt.Where(e => e.Value <= world.Tick).Select(e => e.Key).ToList()) _readyAt.Remove(id);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_readyAt.Count);
        foreach (var (id, tick) in _readyAt.OrderBy(e => e.Key.Slot).ThenBy(e => e.Key.Generation))
        {
            hasher.Add(id);
            hasher.Add(tick);
        }
    }
}
