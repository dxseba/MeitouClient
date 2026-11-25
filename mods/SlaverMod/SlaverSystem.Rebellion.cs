using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

namespace Meitou.SlaverMod;

/// <summary>
/// The daily rules besides submission: the rebellion check, the night cages, and skeleton reprogramming (a menu row).
/// </summary>
sealed partial class SlaverSystem
{
    /// <summary>A game hour began: the night cages at their hours, and once a day the rebellion check when it is due.</summary>
    void Daily(World world, int hour)
    {
        int h = ((hour % 24) + 24) % 24;
        if (settings.CageAtNight && h == settings.CageNightHour) CageAll(world);
        if (settings.CageAtNight && h == settings.CageMorningHour) LetAllOut(world);

        int day = hour / 24;
        if (h != 0 || !settings.RebellionEnabled) return;

        if (_state.NextRebellionDay < 0) _state.NextRebellionDay = day + (int)settings.RebellionDays;
        if (day < _state.NextRebellionDay) return;

        _state.NextRebellionDay = day + (int)settings.RebellionDays;
        CheckRebellion(world);
    }

    #region Rebellion

    /// <summary>
    /// The original's weighing: free = free members x their highest strength, slaves = the defiant slaves (below <c>RebelMaxSubmission</c>) x
    /// their highest; when the slaves weigh more, a chance of (slaves - free) / slaves x <c>RebellionChanceMult</c> (at most
    /// <c>RebellionMaxChance</c>). Rebels are the slaves strong enough for their submission (<c>RebelMinStrengthDefiant</c> at 0 rising to
    /// <c>RebelMinStrength</c> near <c>RebelMaxSubmission</c>).
    /// </summary>
    void CheckRebellion(World world)
    {
        var (freeCount, freeTop) = Weigh(world, slaves: false);
        var (slaveCount, slaveTop) = Weigh(world, slaves: true);
        float free = freeCount * freeTop, slaves = slaveCount * slaveTop;
        if (slaves <= free || slaves <= 0)
        {
            string word = slaveCount == 0 ? "The slaves thought about rising up, but they are too submissive." : "The slaves are under control";
            if (_state.Slaves.Count > 0) world.Post(word);
            return;
        }

        float chance = MathF.Min((slaves - free) / slaves * settings.RebellionChanceMult, settings.RebellionMaxChance);
        if (Roll() >= chance)
        {
            world.Post("The slaves are restless, but did not rise up");
            return;
        }

        Rise(world);
    }

    (int Count, float Top) Weigh(World world, bool slaves)
    {
        var table = world.Characters;
        int count = 0;
        float top = 0;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { IsPlayer: true, Medical.Dead: false, Stats: { } stats } c) continue;
            if (slaves != PlayerSlave(c)) continue;
            if (slaves && Submission(world, i) >= settings.RebelMaxSubmission) continue;
            if (!slaves && c.Medical is { Incapacitated: true }) continue;

            count++;
            top = MathF.Max(top, stats.Strength);
        }

        return (count, top);
    }

    float Submission(World world, int slot) => _state.Slaves.GetValueOrDefault(Lasting(world, world.Characters.IdOf(slot)))?.Submission ?? 0;

    void Rise(World world)
    {
        int faction = population.Relations.IndexOf(settings.RebelFaction);
        if (faction < 0) return;

        var rebels = new List<(int Slot, CharacterCold Cold, ulong Lasting)>();
        foreach (var (lasting, record) in _state.Slaves.ToList())
        {
            if (Resolve(world, lasting, out int s, out var c) && PlayerSlave(c) && c.Medical is not { Incapacitated: true }
                && record.Submission < settings.RebelMaxSubmission && c.Stats?.Strength >= RebelStrength(record.Submission))
                rebels.Add((s, c, lasting));
        }

        if (rebels.Count == 0)
        {
            world.Post("The slaves tried to rise up, but none was strong enough.");
            return;
        }

        if (settings.RebellionHostile && world.Player.Faction >= 0) population.Relations.DeclareWar(faction, world.Player.Faction);

        var squad = NewNpcSquad(world, rebels[0].Slot, faction, FreedTemplate);
        foreach (var (s, c, lasting) in rebels) Rebel(world, s, c, lasting, faction, squad);

        world.Post($"REBELLION! {rebels.Count} slave(s) rose up and joined {population.Relations.Factions[faction].Name}");
    }

    float RebelStrength(float submission) =>
        settings.RebelMinStrengthDefiant + (settings.RebelMinStrength - settings.RebelMinStrengthDefiant) * submission / settings.RebelMaxSubmission;

    void Rebel(World world, int slot, CharacterCold c, ulong lasting, int faction, Simulation.Factions.Squad squad)
    {
        _state.Slaves.Remove(lasting);
        _state.Rebels.Add(lasting);
        if (LockSystem.WornShackles(c)?.Lock is { } l) l.Broken = true;

        FixtureRegistry.ClearSlave(world, c);
        Say(world, world.Characters.IdOf(slot), "RebellionSlave");
        JoinNpcSquad(world, slot, c, faction, squad);
        Arm(c);
    }

    /// <summary>The rebel's poor weapon (<c>RebelWeapon</c>, the worst grade), in a free weapon slot.</summary>
    void Arm(CharacterCold c)
    {
        if (c.Inventory is not { } inv || DebugGiveItem.Make(population, settings.RebelWeapon, 1, 0) is not { } weapon) return;

        if (!InventoryCommands.TryEquip(inv, weapon, c)) inv.Add(weapon);
        CharacterRefresh.Refresh(population, c, CharacterRefreshKind.Equipment);
    }

    /// <summary>Whether <paramref name="id"/> rose up (the dialogue's recruit veto, <c>RebelsDistrust</c>).</summary>
    internal bool IsRebel(World world, CharacterId id) =>
        settings.RebelsDistrust && _state.Rebels.Any(r => world.LastingIds.Find(new LastingId(r)) == id);

    internal Data.Gameplay.FactionRelations Relations => population.Relations;

    #endregion

    #region Cages

    /// <summary>At night: each slave not caged walks into the nearest free, unlocked cage of the player's faction within the search range.</summary>
    void CageAll(World world)
    {
        int sent = 0;
        foreach (var lasting in _state.Slaves.Keys.ToList())
        {
            if (Resolve(world, lasting, out int s, out var c) && !c.IsImprisoned && SendToCage(world, s, lasting)) sent++;
        }

        if (sent > 0) world.Post($"{sent} slave(s) go to their cages for the night.");
    }

    void LetAllOut(World world)
    {
        int let = 0;
        foreach (var lasting in _state.Caged.ToList())
        {
            if (Resolve(world, lasting, out int s, out _) && LetOut(world, s, lasting)) let++;
        }

        _state.Caged.Clear();
        if (let > 0) world.Post($"{let} slave(s) let out of the cages for the day.");
    }

    bool SendToCage(World world, int slot, ulong lasting)
    {
        var table = world.Characters;
        var at = table.Next[slot].Position;
        var cage = world.Fixtures.CageSpots(table)
            .Where(c => c.Owner == world.Player.Faction && c.Occupant.IsNone && !c.Locked
                && System.Numerics.Vector3.Distance(c.Position, at) <= settings.CageSearchRange)
            .OrderBy(c => System.Numerics.Vector3.DistanceSquared(c.Position, at)).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        if (cage is null)
        {
            world.Post($"No free cage of yours near {table.Cold(slot)!.Name}");
            return false;
        }

        world.Defer(new GetInCageOrder([table.IdOf(slot)], cage.Id) { Tick = world.Tick + 1 });
        _state.Caged.Add(lasting);
        return true;
    }

    bool LetOut(World world, int slot, ulong lasting)
    {
        var table = world.Characters;
        var id = table.IdOf(slot);
        var spot = world.Fixtures.CageSpots(table).FirstOrDefault(c => c.Occupant == id);
        if (spot is null) return false;

        if (spot.Locked && world.Fixtures.Find(spot.Id) is { } cage) world.Fixtures.SetLocked(cage, false);
        world.Defer(new GetOutOrder([id]) { Tick = world.Tick + 1 });
        _state.Caged.Remove(lasting);
        return true;
    }

    #endregion

    #region Skeletons

    /// <summary>
    /// Reprogram (an unconscious robot instead of Enslave): chance <c>ThrallBaseChance</c> + <c>ThrallChancePerLevel</c> x the performer's robotics,
    /// at most <c>ThrallMaxChance</c>. Success: a free member of the player's faction in the performer's squad (with <c>ThrallRace</c> its race);
    /// failure: the skeleton is destroyed.
    /// </summary>
    void Reprogram(World world, SlaveOrder order)
    {
        var table = world.Characters;
        if (!settings.SkeletonThrall || !table.TryResolveNext(order.Slave, out int t) || table.Cold(t) is not { Race.IsRobot: true } target) return;
        if (!table.TryResolveNext(order.Owner, out int o) || table.Cold(o) is not { IsPlayer: true, Stats: { } stats } owner) return;
        if (target.IsPlayer || Distance(world, o, t) > ActReach || spawner is null) return;

        float chance = MathF.Min(settings.ThrallBaseChance + settings.ThrallChancePerLevel * stats[Data.Gameplay.Bodies.StatsEnumerated.Robotics],
            settings.ThrallMaxChance);
        if (Roll() * 100 >= chance)
        {
            target.Medical?.Kill();
            MovementSystem.Stop(table, t);
            world.Post($"{owner.Name} failed to reprogram {target.Name} and destroyed them ({chance:0}% chance).");
            return;
        }

        if (!spawner.JoinPlayerSquad(world, order.Slave, owner.SquadId)) return;

        ChangeRace(target);
        world.Post($"{target.Name} was reprogrammed and now serves your faction ({chance:0}% chance).");
    }

    static readonly PropertyInfo? AppearanceRace = typeof(Data.Characters.CharacterAppearance).GetProperty("Race");

    /// <summary>
    /// <c>ThrallRace</c>: the body's race record and the race data change, and the look is rebuilt (the health panel as the original).
    /// </summary>
    void ChangeRace(CharacterCold c)
    {
        if (settings.ThrallRace.Length == 0 || population.Db.Find(settings.ThrallRace) is not { } race || c.Appearance is not { } look) return;

        AppearanceRace?.SetValue(look, race);
        c.Race = Data.Gameplay.Bodies.RaceData.From(race, population.Db);
        CharacterRefresh.Refresh(population, c, CharacterRefreshKind.Look);
    }

    #endregion
}
