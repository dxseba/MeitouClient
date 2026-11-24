using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Items;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The pulse's scans, in slot order: the medics, then the patients in reach of one of them.
/// </summary>
internal sealed partial class MedicSystem
{
    const int OrganicKit = ItemInfo.FirstAidKitFunction;
    const int RobotKit = ItemInfo.RobotKitFunction;

    #region Medics

    /// <summary>
    /// The player characters that may be sent: alive and up, not carrying anyone, out of combat unless the combat medic is on, not under a
    /// manual order's grace, with the Medic job (58) unless <c>requireMedicJob</c> is off; with the kits they hold anywhere in their inventory
    /// (bags in it included). One holding no kit serves only through the kit fallback.
    /// </summary>
    List<Medic> Medics(World world, double now)
    {
        var table = world.Characters;
        var medics = new List<Medic>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold || !MaySend(world, cold, slot, now)) continue;

            bool organic = HoldsKit(cold, OrganicKit), robot = HoldsKit(cold, RobotKit);
            var at = table.Next[slot].Position;
            medics.Add(new Medic(slot, table.IdOf(slot), at, organic, robot, cold.InCombat, OutpostAt(world, at), cold.Race));
        }

        return medics;
    }

    bool MaySend(World world, CharacterCold cold, int slot, double now)
    {
        if (CharacterFacts.Down(cold) || !cold.Carrying.IsNone || _tuning.RequireMedicJob && !cold.MedicJob) return false;
        if (cold.InCombat && !_rules.CombatMedic) return false;

        return !_manual.TryGetValue(world.Characters.IdOf(slot), out double at) || now - at >= _rules.GraceSeconds;
    }

    /// <summary>
    /// A kit of the function in the inventory, its worn backpack or any bag in it (the original's <c>hasItemFunction</c>, then each item's own
    /// inventory); charges are not looked at.
    /// </summary>
    static bool HoldsKit(CharacterCold cold, int function) =>
        cold.Inventory is { } own && own.All().Any(i => i.Type == Meitou.Data.Fcs.FcsRecordType.Item && i.ItemFunction == function);

    #endregion

    #region Patients

    /// <summary>
    /// The downed characters (down, not carried, not a medic) whose faction rule or group is treated, not in a building the filters skip, with a
    /// need above the floor, in reach of a medic holding their kit, not blocked or treated in the last
    /// <see cref="MedicTuning.TargetCooldownSeconds"/>; by group, then the most urgent first.
    /// </summary>
    List<Patient> Patients(World world, List<Medic> medics, double now)
    {
        var table = world.Characters;
        var medicSlots = medics.Select(m => m.Slot).ToHashSet();
        var patients = new List<Patient>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (medicSlots.Contains(slot) || table.Cold(slot) is not { Medical: { } } cold || !CharacterFacts.Down(cold)) continue;
            if (!cold.CarriedBy.IsNone || Candidate(world, slot, cold, now) is not { } patient) continue;
            if (InSomeReach(medics, patient)) patients.Add(patient);
        }

        return [.. patients.OrderBy(p => p.Group).ThenByDescending(p => p.Need)];
    }

    /// <summary>
    /// A medic holding the patient's kit reaches it; or, with the kit fallback, some medic holds that kit and any medic reaches it.
    /// </summary>
    bool InSomeReach(List<Medic> medics, Patient patient)
    {
        if (medics.Any(m => Reaches(m, patient))) return true;
        if (!_tuning.KitFallback || !medics.Any(m => HasKit(m, patient))) return false;

        return medics.Any(m => InReach(m, patient, Vector3.DistanceSquared(m.Position, patient.Position)));
    }

    bool Reaches(in Medic medic, in Patient patient) =>
        HasKit(medic, patient) && InReach(medic, patient, Vector3.DistanceSquared(medic.Position, patient.Position));

    Patient? Candidate(World world, int slot, CharacterCold cold, double now)
    {
        var id = world.Characters.IdOf(slot);
        if (Blocked(id, now) || Cooling(_targetCooldown, id, now, _tuning.TargetCooldownSeconds)) return null;
        if (GroupOf(world, cold) is not { } group || !BuildingAllows(world, slot)) return null;

        bool robot = cold.Race?.IsRobot == true;
        float need = NeedOf(world, cold, robot);
        if (need <= (robot ? _tuning.MinRobotNeed : _tuning.MinOrganicNeed)) return null;

        var at = world.Characters.Next[slot].Position;
        return new Patient(slot, id, at, robot, need, group, Hazard(cold), OutpostAt(world, at));
    }

    /// <summary>
    /// The patient's group when it is treated: the player's characters by their switch; others by their faction's rule (always, never), else by
    /// their group's switch: a wild animal, an enemy of the player's faction, an ally (relation 50 or more), else a neutral.
    /// </summary>
    PatientGroup? GroupOf(World world, CharacterCold cold)
    {
        if (cold.IsPlayer) return _rules.Treats(PatientGroup.Player) ? PatientGroup.Player : null;

        var group = Classify(cold);
        var relations = context.Population.Relations;
        string? faction = relations.Contains(cold.Faction) ? relations.Factions[cold.Faction].Id : null;
        var rule = faction is not null && _rules.Factions.TryGetValue(faction, out var r) ? r : FactionRule.General;

        return rule switch
        {
            FactionRule.Always => group,
            FactionRule.Never => null,
            _ => _rules.Treats(group) ? group : null,
        };
    }

    PatientGroup Classify(CharacterCold cold)
    {
        if (SquadRegistry.IsAnimal(cold)) return PatientGroup.Animal;

        var relations = context.Population.Relations;
        int player = relations.PlayerFaction;
        if (player < 0 || !relations.Contains(cold.Faction)) return PatientGroup.Neutral;
        if (Simulation.Aggro.AggroSystem.IsEnemy(relations, player, cold.Faction)) return PatientGroup.Enemy;

        return relations.IsAlly(cold.Faction, player) ? PatientGroup.Ally : PatientGroup.Neutral;
    }

    /// <summary>
    /// The original's building filters: a patient inside a building of the player's is kept with <c>allowTargetsInsidePlayerBuildings</c>, one
    /// inside another faction's skipped with <c>skipTargetsInsideForeignBuildings</c> (both on by default). Meitou's sight map knows every room's
    /// building, so <c>skipTargetsInsideUnknownBuildings</c> has nothing to act on.
    /// </summary>
    bool BuildingAllows(World world, int slot)
    {
        if (world.Sight.BuildingOf(slot) is not { } building) return true;

        int player = world.Player.Faction;
        bool own = world.Buildings.Find(building) is { } built ? built.Owner == player : world.Fixtures.OwnsBuilding(building, player);
        return own ? _tuning.AllowPlayerBuildings : !_tuning.SkipForeignBuildings;
    }

    /// <summary>
    /// The first aid need score of the patient's kind (<c>scoreFirstAidNeed</c>: robot for a robot race, else organic).
    /// </summary>
    float NeedOf(World world, CharacterCold cold, bool robot)
    {
        if (cold is not { Race: { } race, Medical: { } med }) return 0;

        float capacity = MathF.Max(MedicalState.BloodCapacity(race, cold.Stats?.Strength ?? 50, med.HasStatsObject, med.ScaleK, med.ScaleZ), 1);
        float extraLoss = bodies?.Constants.ExtraBloodLossFromBodyparts ?? 1;
        var (organic, robotic) = med.FirstAidNeeds(race, capacity, extraLoss);
        return robot ? robotic : organic;
    }

    /// <summary>
    /// The acid rain, fire or gas acting on the patient now (the original's <c>getCurrentWeatherAffectStatus</c>, kinds 2 to 4), else none.
    /// </summary>
    static WeatherAffecting Hazard(CharacterCold cold) =>
        cold.Weather is WeatherAffecting.Acid or WeatherAffecting.Burning or WeatherAffecting.Gas ? cold.Weather : WeatherAffecting.None;

    /// <summary>
    /// The number of the player's outpost the point stands in (horizontally within its radius), 0 for none.
    /// </summary>
    static int OutpostAt(World world, Vector3 at)
    {
        int player = world.Player.Faction;
        foreach (var town in world.Buildings.Towns)
        {
            if (town.Owner != player) continue;

            float dx = at.X - town.Position.X, dz = at.Z - town.Position.Z;
            if (dx * dx + dz * dz <= town.Radius * town.Radius) return town.Number;
        }

        return 0;
    }

    #endregion

    static bool Cooling(Dictionary<CharacterId, double> since, CharacterId id, double now, double seconds) =>
        since.TryGetValue(id, out double at) && now - at < seconds;
}
