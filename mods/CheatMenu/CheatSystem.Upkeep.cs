using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.CheatMenu;

/// <summary>
/// The on/off cheats' upkeep, every tick in the serial slow world (the original's runs once a frame in its main-loop hook): God mode and No hunger
/// on the player's characters, Invincible NPCs on the rest, No encumbrance as a carry modifier; once a second the NPC stat modes, every 60 s Chaos
/// and every 180 s a raid (<b>Verified</b>, the main-loop hook's timers).
/// </summary>
internal sealed partial class CheatSystem
{
    /// <summary>The carry modifier key of No encumbrance.</summary>
    const string CarryKey = "dxseba.cheatmenu.noencumbrance";

    /// <summary>No encumbrance's kilograms: the most a carry modifier adds (the original makes the encumbrance factor 1).</summary>
    const float NoLoad = 1_000_000;

    /// <summary>The NPC modes' stat values: Every NPC is a 100-skill beast, Every NPC is a weakling (<b>Verified</b>).</summary>
    const float BeastStat = 100, WeaklingStat = 1;

    const double StatsSeconds = 1, ChaosSeconds = 60, RaidSeconds = 180;

    public void SlowWorld(World world)
    {
        Leave(world);
        KeepInventories(world);
        BaseUpkeep(world);
        WorldUpkeep(world);
        var table = world.Characters;
        for (int s = 0; s < table.HighWater; s++)
        {
            if (!table.Next[s].Alive || table.Cold(s) is not { Medical: { Dead: false } medical } cold) continue;

            if (cold.IsPlayer) KeepPlayer(world, s, cold, medical);
            else if (On(Toggle.InvincibleNpcs)) Restore(cold, medical);
        }

        if (Every(world, StatsSeconds)) NpcStats(world);
        if (On(Toggle.Chaos) && Every(world, ChaosSeconds)) Chaos(world);
        if (On(Toggle.Raids) && Every(world, RaidSeconds)) Raid(world);
    }

    static bool Every(World world, double seconds)
    {
        long ticks = Math.Max(1, (long)Math.Round(seconds / world.TickSeconds));
        return world.Tick % ticks == 0;
    }

    void KeepPlayer(World world, int slot, CharacterCold cold, MedicalState medical)
    {
        if (On(Toggle.GodMode)) Restore(cold, medical);
        if (On(Toggle.NoHunger) || _fedOnly.Contains(world.Characters.IdOf(slot))) medical.Hunger = MedicalState.MaxHunger;
        if (On(Toggle.NoEncumbrance) && cold.Stats is { } stats) stats.CarryModifiers.Set(CarryKey, CarryTerm.Kilograms, NoLoad);
    }

    /// <summary>The original's God mode on one character: every part's health at its maximum and its stun cleared, blood full.</summary>
    static void Restore(CharacterCold cold, MedicalState medical)
    {
        foreach (var part in medical.Parts)
        {
            part.Flesh = part.MaxHp;
            part.Stun = 0;
        }

        if (cold.Race is { } race)
        {
            float strength = cold.Stats?.Strength ?? 50;
            medical.Blood = MedicalState.BloodCapacity(race, strength, cold.Stats is not null && medical.HasStatsObject, medical.ScaleK,
                medical.ScaleZ);
        }
    }

    void ClearCarry(World world)
    {
        var table = world.Characters;
        for (int s = 0; s < table.HighWater; s++)
        {
            if (table.Next[s].Alive && table.Cold(s) is { Stats: { } stats }) stats.CarryModifiers.RemoveAll(CarryKey);
        }
    }

    /// <summary>Beasts or weaklings: every NPC's stats set once a second, as the original's hook sets them each second.</summary>
    void NpcStats(World world)
    {
        float? value = On(Toggle.Beasts) ? BeastStat : On(Toggle.Weaklings) ? WeaklingStat : null;
        if (value is not { } v) return;

        var npcs = Living(world, cold => !cold.IsPlayer);
        if (npcs.Count > 0) Send(world, new DebugSetStats(npcs, AllStats, v) { Tick = world.Tick + 1 });
    }

    /// <summary>The stats the original's loops set: 1 to 38 (strength to bow smithing).</summary>
    internal static readonly IReadOnlyList<StatsEnumerated> AllStats =
        [.. Enumerable.Range(1, (int)StatsEnumerated.End - 1).Select(i => (StatsEnumerated)i)];

    /// <summary>The living characters that match, in slot order.</summary>
    static List<CharacterId> Living(World world, Func<CharacterCold, bool> match)
    {
        var table = world.Characters;
        var found = new List<CharacterId>();
        for (int s = 0; s < table.HighWater; s++)
        {
            if (table.Next[s].Alive && table.Cold(s) is { Medical.Dead: false } cold && match(cold)) found.Add(table.IdOf(s));
        }

        return found;
    }

    /// <summary>Queues a game debug command for the next step (the game's player system applies it).</summary>
    static void Send(World world, SimCommand command) => world.Commands.Enqueue(command);
}
