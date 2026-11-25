using System.Collections.Frozen;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;

namespace Meitou.ForceAwakening;

/// <summary>
/// The paths in the tick (README "Facts"): learning from the class items and the Grey One start, the pools (Concentration, Fury, Focus), the bar's
/// casts and auto-casts, the timed effects, deflected shots, Dark Rage's harder blows, and Command / Mind Trick's lost hostility. Serial phases
/// only (Inputs, SlowWorld, the hit filter and observers); the target-score filter reads a dictionary published in SlowWorld.
/// </summary>
sealed partial class ForceSystem(ForceAwakeningMod mod, PopulationData population, RngPurpose purpose, CombatSystem? combat,
    PopulationSystem? populationSystem, Meitou.Simulation.Aggro.AggroSystem? aggro, Meitou.Simulation.Crime.CrimeSystem? crime)
    : IWorldReady, IModSaveData, IStateHashed, ICombatHitFilter, ICombatHitObserver, ITargetScoreFilter
{
    readonly ForceConfig _config = mod.Config;

    /// <summary>The adepts by lasting id (sorted, so every pass goes in one order).</summary>
    readonly SortedDictionary<ulong, Adept> _adepts = [];

    readonly List<SimCommand> _orders = [];
    readonly List<Timed> _timed = [];

    /// <summary>Shots a deflecting character sent back: the shooter and the packet, applied in SlowWorld.</summary>
    readonly List<(CharacterId Shooter, DamagePacket Packet)> _reflected = [];

    /// <summary>The characters whose hostility is held down (Command: towards one faction; Mind Trick: -1, towards everyone).</summary>
    volatile FrozenDictionary<CharacterId, int> _calm = FrozenDictionary<CharacterId, int>.Empty;

    volatile FrozenDictionary<CharacterId, BarView> _bars = FrozenDictionary<CharacterId, BarView>.Empty;

    /// <summary>The player characters' bars, for the interface (host thread).</summary>
    public FrozenDictionary<CharacterId, BarView> Bars => _bars;

    public void Ready(World world, bool fromSave)
    {
        ClearEffects(world);
        if (!fromSave) StartClasses(world);
        Publish(world);
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is CastOrder or BarOrder or AutoOrder or TreeOrder or RenounceOrder or BrainwashFactionOrder) _orders.Add(command);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Apply(world, order);
        _orders.Clear();

        _by = -1;
        ApplyReflected(world);
        StepTimed(world);
        StepPools(world);
        if (world.Tick % Ticks(world, 1) == 0) Learn(world);
        if (world.Tick % Ticks(world, 10) == 0) SpawnMasters(world);
        if (world.Tick % Ticks(world, 2) == 0) CheckBounty(world);
        AutoCasts(world);
        if (world.Tick % 3 == 0) Publish(world);
        else PublishMoves(world);
    }

    void Apply(World world, SimCommand order)
    {
        switch (order)
        {
            case CastOrder cast:
                if (AdeptOf(world, cast.Caster) is { } a && cast.Slot is >= 0 and < 5 && _config.SpellByNumber(a.Bar[cast.Slot]) is { } spell)
                    Cast(world, cast.Caster, a, spell, cast.Target, cast.Point, manual: true);
                break;
            case BarOrder bar when AdeptOf(world, bar.Who) is { } held && bar.Slot is >= 0 and < 5:
                for (int i = 0; i < held.Bar.Length; i++)
                {
                    if (held.Bar[i] == bar.Spell) held.Bar[i] = 0;
                }

                held.Bar[bar.Slot] = bar.Spell;
                break;
            case AutoOrder auto when AdeptOf(world, auto.Who) is { } ticked:
                if (auto.On) ticked.Auto.Add(auto.Spell);
                else ticked.Auto.Remove(auto.Spell);
                break;
            case TreeOrder tree when AdeptOf(world, tree.Who) is { } chooser && tree.Tree is 1 or 2:
                chooser.Tree = tree.Tree;
                break;
            case BrainwashFactionOrder next when AdeptOf(world, next.Who) is { } chooser:
                chooser.Faction = (chooser.Faction + 1) % (_config.BrainwashFactions.Count + 1);
                world.Post(ForceTexts.T("GRIM_BRAINWASH_NEXT"));
                break;
            case RenounceOrder renounce:
                Renounce(world, renounce.Who);
                break;
        }
    }

    #region Adepts

    Adept? AdeptOf(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { LastingId: { IsNone: false } lasting }) return null;

        return _adepts.TryGetValue(lasting.Value, out var a) && a.Path != ForcePath.None ? a : null;
    }

    /// <summary>The adepts in the world now: their character, slot and state, in lasting-id order.</summary>
    IEnumerable<(CharacterId Id, int Slot, CharacterCold Cold, Adept Adept)> Present(World world)
    {
        var table = world.Characters;
        foreach (var (key, adept) in _adepts.ToArray())
        {
            var id = world.LastingIds.Find(new LastingId(key));
            if (id.IsNone || !table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Medical: { Dead: false } } cold) continue;

            yield return (id, slot, cold, adept);
        }
    }

    /// <summary>The path level: The Force (Jedi, Sith, Grey Jedi) or Crossbows (Bounty Hunter).</summary>
    float Level(CharacterCold cold, ForcePath path) =>
        cold.Stats is not { } stats ? 0 : path == ForcePath.Hunter ? stats[StatsEnumerated.Crossbows] : mod.Force.Level(stats);

    float Wisdom(CharacterCold cold) => cold.Stats is { } stats ? mod.Wisdom.Level(stats) : 1;

    float PoolMax(CharacterCold cold, Adept a) => a.Path is ForcePath.Jedi or ForcePath.Grey
        ? _config.ManaBase + _config.ManaPerWisdom * Wisdom(cold)
        : 100;

    /// <summary>The value of passive <paramref name="index"/> of <paramref name="tree"/> once open (the chosen tree, the level reached), else 0.</summary>
    float Passive(CharacterCold cold, Adept a, int tree, int index)
    {
        if (a.Tree != tree || !_config.Passives.TryGetValue((a.Path, tree, index), out float value)) return 0;

        return Level(cold, a.Path) >= _config.PassiveLevels[index - 1] ? value : 0;
    }

    void Learn(World world, CharacterId id, CharacterCold cold, ForcePath path)
    {
        if (SideConflict(world, cold, path) is { } other)
        {
            world.Post(ForceTexts.T(path == ForcePath.Jedi ? "SAY_SIDE_JEDI" : "SAY_SIDE_SITH", other));
            return;
        }

        var adept = new Adept { Path = path, Pool = path == ForcePath.Sith ? _config.FuryFloor : PoolMax(cold, new Adept { Path = path }) };
        int slot = 0;
        foreach (var spell in _config.SpellsOf(path))
        {
            if (slot < 5 && Level(cold, path) >= spell.Level) adept.Bar[slot++] = spell.Number;
        }

        _adepts[world.LastingIds.GetOrGive(id).Value] = adept;
        world.Post($"{cold.Name}: {ForceTexts.PathName(path)}");
    }

    /// <summary>The original's one side per group: a Jedi never with a Sith in one squad (the Grey Jedi walks with both), unless the option lifts it.</summary>
    string? SideConflict(World world, CharacterCold cold, ForcePath path)
    {
        if (mod.Settings.MixedSides || path is not (ForcePath.Jedi or ForcePath.Sith)) return null;

        var opposite = path == ForcePath.Jedi ? ForcePath.Sith : ForcePath.Jedi;
        foreach (var (_, _, other, adept) in Present(world))
        {
            if (adept.Path == opposite && other.SquadId == cold.SquadId && other != cold) return other.Name;
        }

        return null;
    }

    /// <summary>A pathless player character carrying a class item learns its path (the item is kept: hand it on).</summary>
    void Learn(World world)
    {
        if (_config.ClassItems.Count == 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory, Medical.Dead: false } cold) continue;

            var id = table.IdOf(slot);
            if (!cold.LastingId.IsNone && _adepts.TryGetValue(cold.LastingId.Value, out var known) && known.Path != ForcePath.None) continue;

            foreach (var item in inventory.All())
            {
                if (!_config.ClassItems.TryGetValue(item.Record, out var path)) continue;

                Learn(world, id, cold, path);
                break;
            }
        }
    }

    void StartClasses(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true } cold && _config.StartClasses.TryGetValue(cold.RecordId, out var path))
                Learn(world, table.IdOf(slot), cold, path);
        }
    }

    void Renounce(World world, CharacterId id)
    {
        if (!world.Characters.IsAlive(id) || AdeptOf(world, id) is not { } a) return;

        string name = world.Characters.NameOf(id);
        world.Post(ForceTexts.T("GRIM_RENOUNCED", name, ForceTexts.PathName(a.Path)));
        _adepts.Remove(world.LastingIds.GetOrGive(id).Value);
    }

    #endregion

    #region Pools

    /// <summary>Concentration comes back by itself (Wisdom), Focus refills, Fury fills from blows and drains out of a fight to its floor.</summary>
    void StepPools(World world)
    {
        float dt = world.TickSeconds;
        foreach (var (_, _, cold, a) in Present(world))
        {
            float max = PoolMax(cold, a);
            switch (a.Path)
            {
                case ForcePath.Jedi or ForcePath.Grey:
                    float regen = _config.RegenBase + _config.RegenPerWisdom * Wisdom(cold);
                    a.Pool += regen * (1 + Passive(cold, a, 2, 1) / 100) * dt;
                    break;
                case ForcePath.Hunter:
                    a.Pool += _config.FocusRegen * dt;
                    break;
                case ForcePath.Sith:
                    Fury(world, a, dt);
                    break;
            }

            a.Pool = Math.Clamp(a.Pool, 0, max);
        }
    }

    void Fury(World world, Adept a, float dt)
    {
        float floor = _config.FuryFloor;
        if (a.Pool < floor) a.Pool = MathF.Min(floor, a.Pool + _config.FuryFloorRegen * dt);
        else if ((world.Tick - a.LastFight) * world.TickSeconds > _config.FuryDecayDelay) a.Pool = MathF.Max(floor, a.Pool - _config.FuryDecay * dt);
    }

    #endregion

    #region Combat hooks

    /// <summary>Deflect sends a bolt back at its shooter; Dark Rage makes the raging character's blows harder.</summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (hit.Source == CombatHitSource.Projectile && Has(Lasting.Deflect, hit.Victim))
        {
            hit.Cancelled = true;
            if (!hit.Attacker.IsNone) _reflected.Add((hit.Attacker, hit.Packet));
            return;
        }

        if (hit.Source == CombatHitSource.Melee && Find(Lasting.DarkRage, hit.Attacker) is { } rage)
        {
            float k = 1 + rage.Power / 100;
            var p = hit.Packet;
            hit.Packet = p with { Cut = p.Cut * k, Blunt = p.Blunt * k, Pierce = p.Pierce * k };
        }
    }

    /// <summary>Fury from blows given and taken; a hit on a commanded character from the faction it was calmed towards breaks the Command.</summary>
    public void HitResolved(World world, in CombatHit hit)
    {
        Gain(world, hit.Attacker, _config.FuryHit);
        Gain(world, hit.Victim, _config.FuryHurt);

        var table = world.Characters;
        if (!table.TryResolveNext(hit.Attacker, out int slot) || table.Cold(slot) is not { } attacker) return;

        var victim = hit.Victim;
        _timed.RemoveAll(t => t.Kind == Lasting.Command && t.Who == victim && t.Faction == attacker.Faction);
    }

    void Gain(World world, CharacterId id, float fury)
    {
        if (AdeptOf(world, id) is not { } a) return;

        a.LastFight = world.Tick;
        if (a.Path == ForcePath.Sith) a.Pool = MathF.Min(100, a.Pool + fury);
    }

    /// <summary>A calmed character scores its targets (Command: of the faction it forgot; Mind Trick: anyone) as not worth a fight.</summary>
    public float Score(World world, in TargetScoreAsk ask)
    {
        if (!_calm.TryGetValue(ask.Chooser, out int faction)) return ask.Score;
        if (faction >= 0 && (ask.Target.Slot >= world.Characters.HighWater || world.Characters.Cold(ask.Target.Slot)?.Faction != faction))
            return ask.Score;

        return -1e6f;
    }

    void ApplyReflected(World world)
    {
        foreach (var (shooter, p) in _reflected) Hurt(world, shooter, new HitDamage(p.Cut, p.Blunt, p.Pierce));
        _reflected.Clear();
    }

    #endregion

    #region Publishing

    void Publish(World world)
    {
        var bars = new Dictionary<CharacterId, BarView>();
        var calm = new Dictionary<CharacterId, int>();
        foreach (var t in _timed)
        {
            if (t.Kind is Lasting.Command or Lasting.MindTrick) calm[t.Who] = t.Kind == Lasting.MindTrick ? -1 : t.Faction;
        }

        foreach (var (id, _, cold, a) in Present(world))
        {
            if (cold.IsPlayer) bars[id] = View(world, id, cold, a);
        }

        _bars = bars.ToFrozenDictionary();
        _calm = calm.ToFrozenDictionary();
        PublishMoves(world);
    }

    BarView View(World world, CharacterId id, CharacterCold cold, Adept a)
    {
        float level = Level(cold, a.Path);
        var slots = a.Bar.Select(n => _config.SpellByNumber(n) is { } s
            ? new SlotView(s, Cooldown(world, a, s), Refusal(world, id, cold, a, s, checkTarget: false) is null)
            : new SlotView(null, 0, false)).ToArray();
        var known = _config.SpellsOf(a.Path).Select(s => new SpellView(s, level >= s.Level)).ToArray();
        return new BarView(id, cold.Name, a.Path, a.Tree, level, Wisdom(cold), a.Pool, PoolMax(cold, a), slots, known, a.Auto.ToFrozenSet(),
            BrainwashFactionName(a));
    }

    float Cooldown(World world, Adept a, Spell s) =>
        a.Ready.TryGetValue(s.Number, out long ready) && ready > world.Tick ? (ready - world.Tick) * world.TickSeconds : 0;

    #endregion

    #region Save and hash

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(2);
        w.Write(_adepts.Count);
        foreach (var (key, a) in _adepts)
        {
            w.Write(key);
            a.Write(w);
        }

        WriteBounty(w);
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = r.ReadInt32();
        if (version is < 1 or > 2) return;

        _adepts.Clear();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            ulong key = r.ReadUInt64();
            _adepts[key] = Adept.Read(r, version);
        }

        if (version >= 2) ReadBounty(r);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_adepts.Count);
        foreach (var (key, a) in _adepts)
        {
            hasher.Add(key);
            hasher.Add((int)a.Path);
            hasher.Add(a.Pool);
        }

        hasher.Add(_timed.Count);
        hasher.Add(_config.Bounties.FindIndex(b => b.Character == _contract));
        hasher.Add(_done.Count);
    }

    #endregion

    static long Ticks(World world, float seconds) => Math.Max(1, (long)MathF.Round(seconds / world.TickSeconds));
}
