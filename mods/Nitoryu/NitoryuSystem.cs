using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;

namespace Meitou.Nitoryu;

/// <summary>Everything a world's Nitoryu needs from the session: the settings, rules, the skill and the RNG purpose.</summary>
sealed record NitoryuSession(NitoryuConfig Config, Pairs Pairs, NpcRules Npcs, ModSkill Skill, RngPurpose Purpose, PopulationData Population,
    GameDatabase Data, NitoryuSettings Settings, Action<string> Log, Func<string, object?[], string> Tr);

/// <summary>
/// A world's dual wielding (README "What it does"). The serial update (<see cref="Schedule"/>) works out, for every character, whether it
/// fights with two blades now, its Dual Wielding (seeded the first time it is seen), the Path and the rank, the beginner's clumsiness, and gives
/// the Dual Wielding experience the hooks gathered; the partial files hold the combat hooks.
/// </summary>
sealed partial class NitoryuSystem(NitoryuSession session) : IWorldReady
{
    /// <summary>The stat-modifier key of the clumsiness (melee attack and defence).</summary>
    const string ClumsyKey = "dxseba.nitoryu.clumsy";

    /// <summary>The original's STATS key of the skill (<c>fcs.def</c>), kept beside Meitou's own so a save taken to the original keeps it.</summary>
    const string OriginalStatsKey = "NIT_ambidexterity";

    /// <summary>The save key of a character who has the dual stance turned off.</summary>
    const string OffKey = "nitoryu dual off";

    static readonly ConditionalWeakTable<World, NitoryuSystem> Worlds = [];

    /// <summary>What the host draws: the dual-drawn characters' second blades (record and side), written in the serial update.</summary>
    public static readonly ConcurrentDictionary<CharacterId, (string Weapon, bool Back)> Drawn = new();

    readonly DualState _state = new();
    readonly HashSet<CharacterId> _off = [];
    readonly HashSet<CharacterId> _any = [];
    readonly List<ToggleAnyWeaponOrder> _anyOrders = [];

    /// <summary>The last reason logged per player character for not fighting dual, so each change is logged once.</summary>
    readonly Dictionary<CharacterId, string> _whyLogged = [];

    /// <summary>The save key of a character who fights with blunt weapons or hackers as a pair (the second button).</summary>
    const string AnyKey = "nitoryu any weapon";
    readonly List<ToggleDualOrder> _orders = [];
    MovePool? _moves;
    CombatSystem? _combat;

    public NitoryuSession Session => session;
    public DualState State => _state;
    public MovePool Moves => _moves ??= MovePool.Build(session.Data, _combat?.Techniques ?? []);
    public CombatSystem? Combat => _combat;

    /// <summary>The clip lengths the animation system plays by (the original's tracks among them).</summary>
    public AnimationLengths? Lengths { get; private set; }

    /// <summary>The last tick the serial update ran, and the world's tick length (for the display-side hooks).</summary>
    public long LastTick { get; private set; }

    public float TickSeconds { get; private set; } = 1f / 30;

    /// <summary>The system of the world that ran last (the animation hooks get no world; they check the character's key).</summary>
    public static NitoryuSystem? Latest { get; private set; }

    /// <summary>The world's system, once it has run its first update.</summary>
    public static NitoryuSystem? Of(World world) => Worlds.TryGetValue(world, out var system) ? system : null;

    public void Ready(World world, bool fromSave)
    {
        Attach(world);
        if (!fromSave) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is null) continue;

            var id = table.IdOf(slot);
            if (world.SaveFields.Bool(id, Simulation.Saving.CharacterSaveRecord.Stats, OffKey) == true) _off.Add(id);
            if (world.SaveFields.Bool(id, Simulation.Saving.CharacterSaveRecord.Stats, AnyKey) == true) _any.Add(id);
        }
    }

    void Attach(World world)
    {
        if (_combat is not null) return;

        _combat = world.Systems.OfType<CombatSystem>().FirstOrDefault();
        Lengths = world.Systems.OfType<Simulation.Movement.AnimationSystem>().FirstOrDefault()?.Lengths;
        TickSeconds = world.TickSeconds;
        Worlds.AddOrUpdate(world, this);
        Latest = this;
        Drawn.Clear();
        session.Log($"{Moves.All.Count} dual-wield moves in the pool");
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ToggleDualOrder order) _orders.Add(order);
            else if (command is ToggleAnyWeaponOrder any) _anyOrders.Add(any);
        }
    }

    public void Schedule(World world)
    {
        Attach(world);
        LastTick = world.Tick;
        ApplyToggles(world);
        var table = world.Characters;
        _state.Grow(table.HighWater);
        bool full = world.Tick % 32 == 0;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Stats: not null, Fighter: not null, Medical: not null } cold) continue;

            ref var s = ref _state[slot];
            var id = table.IdOf(slot);
            if (s.Id != id) Meet(world, ref s, id, cold);
            if (full || cold.DrawnWeapon != WeaponKinds.None || s.Dual) Update(world, ref s, id, cold);
            if (s.Dual || s.PressureStacks > 0 || Marks.States.ContainsKey(id)) PublishMarks(world, ref s, id, cold);
        }
    }

    /// <summary>The marks' state of a character (display only).</summary>
    void PublishMarks(World world, ref DualFighter s, CharacterId id, CharacterCold cold)
    {
        long now = world.Tick;
        float duration = session.Config.FlowDurationMs / 1000f / world.TickSeconds;
        float flow = s.Dual && now < s.FlowUntil && duration > 0 ? (s.FlowUntil - now) / duration : 0;
        int pressure = Stacks(world, ref s, now);
        Marks.Publish(id, new MarkState(flow, s.Dual && now < s.ChainUntil, pressure, pressure >= session.Config.PressureMaxStacks, cold.IsPlayer));
    }

    /// <summary>A character seen in this slot for the first time: fresh combat state, its Dual Wielding seeded.</summary>
    void Meet(World world, ref DualFighter s, CharacterId id, CharacterCold cold)
    {
        if (s.Dual) Drawn.TryRemove(s.Id, out _);
        s = default;
        s.ResetCombat();
        s.Id = id;
        Seed(world, id, cold);
    }

    void Seed(World world, CharacterId id, CharacterCold cold)
    {
        var stats = cold.Stats!;
        if (session.Skill.Level(stats) >= 0) return;

        string? faction = FactionId(cold);
        float level = session.Npcs.Level(cold.RecordId, faction)
            ?? world.SaveFields.Float(id, Simulation.Saving.CharacterSaveRecord.Stats, OriginalStatsKey)
            ?? session.Config.AmbidexteritySeed * MathF.Max(stats.Stored(StatsEnumerated.Katanas), stats.Stored(StatsEnumerated.Sabres));
        session.Skill.Set(stats, MathF.Max(0, level));
    }

    string? FactionId(CharacterCold cold) =>
        (uint)cold.Faction < (uint)session.Population.Factions.Count ? session.Population.Factions[cold.Faction].Id : null;

    void Update(World world, ref DualFighter s, CharacterId id, CharacterCold cold)
    {
        GiveExperience(world, ref s, cold);
        bool was = s.Dual;
        int oldTier = s.Tier;
        Decide(ref s, id, cold);
        if (s.Dual) s.LeftHand = false;
        else LeftHand(ref s, cold);
        Sheaths(world, ref s, cold);
        Clumsiness(ref s, cold);
        Pane(ref s, cold);
        if (cold.IsPlayer) Buttons(id, cold);
        Publish(ref s, id, cold, was);
        if (s.Dual && was && s.Tier != oldTier)
        {
            int from = oldTier, to = s.Tier;
            Addons.Raise(l => l.RankChanged(world, id, from, to));
        }

        if (s.Dual && s.Tier > oldTier && was && cold.IsPlayer && session.Settings.RankUpMessage)
            world.Post(session.Tr("{0} reaches the rank of {1} in Dual Wielding", [cold.Name, Ranks.Name(s.Tier)]));
    }

    /// <summary>Whether the character fights with two blades now, and its Path and rank.</summary>
    void Decide(ref DualFighter s, CharacterId id, CharacterCold cold)
    {
        var fighter = cold.Fighter!;
        s.Npc = !cold.IsPlayer;
        var pair = session.Settings.Enabled && !_off.Contains(id) && MeleeDrawn(cold) ? session.Pairs.SecondBlade(fighter, _any.Contains(id)) : null;
        bool allowed = pair is not null && cold.Medical!.ArmWorks(left: true) && cold.Medical.ArmWorks(left: false) && NpcFights(cold, pair.Value.Off);
        s.Dual = allowed;
        if (!allowed)
        {
            s.Off = null;
            LogWhyNot(id, cold, pair is not null);
            return;
        }

        s.Off = pair!.Value.Off;
        s.OffIsBack = pair.Value.OffIsBack;
        s.Traits = session.Pairs.Traits(fighter.Weapon!.Data, s.Off.Data);
        Rank(ref s, cold);
        LogDual(ref s, id, cold);
    }

    /// <summary>Logs once per rank when a player character fights with two blades (support only).</summary>
    void LogDual(ref DualFighter s, CharacterId id, CharacterCold cold)
    {
        if (!cold.IsPlayer) return;

        string state = $"dual with '{s.Off!.Data.StringId}' as second blade, Path {s.Path:0}, rank {s.Tier}";
        if (_whyLogged.TryGetValue(id, out var last) && last.StartsWith("dual", StringComparison.Ordinal) && last.EndsWith($"rank {s.Tier}"))
            return;

        _whyLogged[id] = state;
        session.Log($"{cold.Name}: {state}");
    }

    /// <summary>
    /// Logs once per change why a player character carrying a back and a hip weapon does not fight with both (display and support only).
    /// </summary>
    void LogWhyNot(CharacterId id, CharacterCold cold, bool paired)
    {
        var fighter = cold.Fighter!;
        if (!cold.IsPlayer || fighter.BackWeapon is null || fighter.HipWeapon is null) return;

        string why = !session.Settings.Enabled ? "dual wielding is off in Mod settings"
            : _off.Contains(id) ? "its dual stance is toggled off"
            : !MeleeDrawn(cold) ? "no melee weapon drawn"
            : paired ? "an arm does not work"
            : session.Pairs.WhyNot(fighter, _any.Contains(id)) ?? "unknown";
        if (_whyLogged.TryGetValue(id, out var last) && last == why) return;

        _whyLogged[id] = why;
        session.Log($"{cold.Name}: not fighting with two blades: {why}");
    }

    static bool MeleeDrawn(CharacterCold cold) =>
        cold.DrawnWeapon != WeaponKinds.None && cold.DrawnWeapon != HandHold.OfCategory((int)WeaponCategory.Bow).Weapon;

    /// <summary>
    /// An NPC fights dual by <c>[NPC] dual</c>: <c>off</c> none, <c>authored</c> only the opted-in, <c>all</c> also any with Dexterity 30, both
    /// weapon skills 30 and strength at least half the pair's weight.
    /// </summary>
    bool NpcFights(CharacterCold cold, WeaponInstance off)
    {
        if (cold.IsPlayer) return true;

        var config = session.Config;
        if (config.NpcDual == "off") return false;
        if (session.Npcs.OptedIn(cold.RecordId, FactionId(cold))) return true;
        if (config.NpcDual != "all") return false;

        var stats = cold.Stats!;
        var main = cold.Fighter!.Weapon!;
        float weight = main.Stats.Weight + off.Stats.Weight;
        return stats[StatsEnumerated.Dexterity] >= config.NpcMinDexterity && stats[main.Skill] >= config.NpcMinSkill
            && stats[off.Skill] >= config.NpcMinSkill && stats[StatsEnumerated.Strength] >= weight * config.NpcStrengthShare;
    }

    void Rank(ref DualFighter s, CharacterCold cold)
    {
        var config = session.Config;
        s.Ambidexterity = session.Skill.Level(cold.Stats!);
        s.Effective = s.Ambidexterity * ArmourFactor(cold) * OverloadFactor(cold);
        s.Path = MathF.Max(PathOf(ref s, cold), s.Npc ? session.Npcs.PathFloor(cold.RecordId, FactionId(cold)) : 0);
        s.Tier = Ranks.Tier(config, s.Path, s.Effective);
        bool authored = session.Npcs.OptedIn(cold.RecordId, FactionId(cold));
        if (s.Npc && !authored) s.Tier = Math.Min(s.Tier, config.NpcMaxTier);
    }

    /// <summary>
    /// The Path: Dual Wielding as it counts in the fight 60 %, the lower of the two weapon skills, Dexterity, melee attack and melee defence 10 %
    /// each, as the fight reads them (the port's own clumsiness taken back out).
    /// </summary>
    float PathOf(ref DualFighter s, CharacterCold cold)
    {
        var config = session.Config;
        var main = cold.Fighter!.Weapon!;
        float skill = MathF.Min(CombatAccess.Eff(cold, main.Skill), CombatAccess.Eff(cold, s.Off!.Skill));
        float dex = CombatAccess.Eff(cold, StatsEnumerated.Dexterity);
        float attack = CombatAccess.Eff(cold, StatsEnumerated.MeleeAttack) + s.Clumsy;
        float defence = CombatAccess.Eff(cold, StatsEnumerated.MeleeDefence) + s.Clumsy + s.Divided;
        float path = config.WeightAmbidexterity * s.Effective + config.WeightSkill * skill + config.WeightDexterity * dex
            + config.WeightAttack * attack + config.WeightDefence * defence;
        return Math.Clamp(path, 0, 100);
    }

    /// <summary>Each heavy piece worn takes 7 %, each medium 4 %, down to the floor (0.3).</summary>
    float ArmourFactor(CharacterCold cold)
    {
        var config = session.Config;
        float cut = 0;
        foreach (var piece in cold.Fighter!.Armour)
        {
            if (piece.Data.Class == ArmourClass.Heavy) cut += config.ArmourHeavyCut;
            else if (piece.Data.Class == ArmourClass.Medium) cut += config.ArmourMediumCut;
        }

        return MathF.Max(config.ArmourFloor, 1 - cut);
    }

    /// <summary>An overloaded fighter's Dual Wielding falls with its encumbrance factor, down to the floor (0.2).</summary>
    float OverloadFactor(CharacterCold cold) => Math.Clamp(cold.Fighter!.EncumbranceFactor, session.Config.OverloadFloor, 1);

    /// <summary>
    /// Learning is hard: -10 melee attack and defence at Path 0, gone at 40 (<c>clumsiness base</c> - <c>slope</c> x Path), and the split guard on
    /// defence by rank (<c>divided guard</c> 4, 4, 2, 0, ...). Set while the pair is drawn.
    /// </summary>
    void Clumsiness(ref DualFighter s, CharacterCold cold)
    {
        var mods = cold.Stats!.Modifiers;
        var config = session.Config;
        float clumsy = s.Dual ? MathF.Max(0, config.ClumsinessBase - config.ClumsinessSlope * s.Path) : 0;
        float divided = (s.Dual ? Ranks.Divided(config, s.Tier) : 0) + s.LeftBlock;
        if (clumsy == s.Clumsy && divided == s.Divided) return;

        s.Clumsy = clumsy;
        s.Divided = divided;
        if (clumsy <= 0 && divided <= 0)
        {
            mods.RemoveAll(ClumsyKey);
            return;
        }

        mods.Set(ClumsyKey, StatsEnumerated.MeleeAttack, -clumsy);
        mods.Set(ClumsyKey, StatsEnumerated.MeleeDefence, -clumsy - divided);
    }

    /// <summary>The inventory buttons' state (<see cref="InventoryButtons"/>).</summary>
    void Buttons(CharacterId id, CharacterCold cold)
    {
        var fighter = cold.Fighter!;
        bool any = _any.Contains(id);
        bool pair = fighter.BackWeapon is { } back && fighter.HipWeapon is { } hip && session.Pairs.Allowed(back.Data, any)
            && session.Pairs.Allowed(hip.Data, any) && (session.Pairs.HasBlade(back.Data) || session.Pairs.HasBlade(hip.Data));
        bool shown = session.Pairs.AnyButton(fighter);
        if (pair || shown) InventoryButtons.States[id] = new ButtonState(pair, !_off.Contains(id), shown, any);
        else InventoryButtons.States.TryRemove(id, out _);
    }

    /// <summary>The Dual Wielding pane's live lines of the character (<see cref="StatsPane"/>).</summary>
    void Pane(ref DualFighter s, CharacterCold cold)
    {
        var config = session.Config;
        float level = session.Skill.Level(cold.Stats!);
        float armour = ArmourFactor(cold), overload = OverloadFactor(cold);
        int next = s.Tier + 1;
        string needs = !s.Dual || next > 5 || next > config.TierPath.Length ? ""
            : $"Path {config.TierPath[next - 1]:0}" + (next <= config.TierAmbidexterity.Length && config.TierAmbidexterity[next - 1] > 0
                ? $", {config.TierAmbidexterity[next - 1]:0} Dual Wielding" : "") + $" for {Ranks.Name(next)}";
        string pair = !s.Dual ? "" : string.Join(", ", new[]
        {
            s.Traits.LongShort ? $"long + short: parries x{config.PairLongShortParry:0.00}" : "",
            s.Traits.Equal ? $"equal blades: twin strikes x{config.PairEqualTwinShare:0.00}" : "",
            s.Traits.Mixed ? "two kinds: the second blade's defence bonus" : "",
        }.Where(t => t.Length > 0));
        float speed = s.Dual ? SpeedFactor(ref s, CombatAccess.Eff(cold, StatsEnumerated.Dexterity)) : 1;
        StatsPane.Info.AddOrUpdate(cold, new PaneInfo(level * armour * overload, armour, overload, s.Dual ? s.Path : 0, s.Dual ? s.Tier : 0, needs,
            speed, pair, s.LeftHand));
    }

    /// <summary>
    /// The pair drawn or put away (the draw, sheathe and blood-flick tracks): the tick of each, which side the second blade is worn, and whether
    /// the sheathe follows a fight that just ended (the flick first).
    /// </summary>
    void Sheaths(World world, ref DualFighter s, CharacterCold cold)
    {
        if (cold.InCombat) s.LastFightTick = world.Tick;
        bool carries = session.Pairs.SecondBlade(cold.Fighter!) is not null || s.Dual;
        bool now = s.Dual;
        if (now && !s.PairOut)
        {
            s.DrawTick = world.Tick;
            s.OffHip = !s.OffIsBack;
        }
        else if (!now && s.PairOut && carries && cold.DrawnWeapon == WeaponKinds.None)
        {
            s.SheatheTick = world.Tick;
            s.Chiburi = !cold.InCombat && Ms(world, world.Tick - s.LastFightTick) < 3000 && cold.CarriedBy.IsNone;
        }

        s.PairOut = now;
    }

    /// <summary>The second blade the host draws in the left hand, and the original's save key.</summary>
    void Publish(ref DualFighter s, CharacterId id, CharacterCold cold, bool was)
    {
        if (s.Dual) Drawn[id] = (s.Off!.Data.StringId, s.OffIsBack);
        else if (was) Drawn.TryRemove(id, out _);
    }

    /// <summary>Gives the Dual Wielding experience the hooks gathered since the last update (on the game's curve).</summary>
    void GiveExperience(World world, ref DualFighter s, CharacterCold cold)
    {
        if (s.PendingOffXp > 0 && s.Off is { } off) cold.Stats!.Gain(off.Skill, s.PendingOffXp);
        s.PendingOffXp = 0;
        if (s.PendingXp <= 0) return;

        float amount = s.PendingXp;
        s.PendingXp = 0;
        float level = session.Skill.Gain(cold.Stats!, amount);
        world.SaveFields.SetFloat(s.Id, Simulation.Saving.CharacterSaveRecord.Stats, OriginalStatsKey, level);
    }

    void ApplyToggles(World world)
    {
        foreach (var order in _orders)
        {
            bool anyOn = order.Characters.Any(c => !_off.Contains(c));
            foreach (var id in order.Characters)
            {
                if (anyOn) _off.Add(id);
                else _off.Remove(id);
                if (world.Characters.IsAlive(id)) world.SaveFields.SetBool(id, Simulation.Saving.CharacterSaveRecord.Stats, OffKey, anyOn);
            }
        }

        _orders.Clear();
        foreach (var order in _anyOrders)
        {
            bool on = _any.Add(order.Character);
            if (!on) _any.Remove(order.Character);
            if (world.Characters.IsAlive(order.Character))
                world.SaveFields.SetBool(order.Character, Simulation.Saving.CharacterSaveRecord.Stats, AnyKey, on);
        }

        _anyOrders.Clear();
    }
}

/// <summary>The selected characters' dual stance is turned off, or on again when all of them have it off (the original's inventory button).</summary>
public sealed record ToggleDualOrder(IReadOnlyList<CharacterId> Characters) : SimCommand;
