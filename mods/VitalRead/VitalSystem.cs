using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.VitalRead;

/// <summary>
/// Publishes each player character's <see cref="VitalFacts"/> every <see cref="Every"/> ticks, for the portrait bar on the interface's thread: the
/// wound severity, the crippled legs and arms (<see cref="MedicalState"/>), whether it is a robot and whether its bed is a repair bed.
/// </summary>
sealed class VitalSystem(VitalReadSettings settings, PopulationData data) : ITickSystem
{
    const int Every = 5;

    /// <summary>Per bed record: whether a functionality of it is a repair bed (<see cref="BedKind.RepairBed"/>).</summary>
    readonly ConcurrentDictionary<string, bool> _repairBeds = new(StringComparer.Ordinal);

    volatile FrozenDictionary<CharacterId, VitalFacts> _facts = FrozenDictionary<CharacterId, VitalFacts>.Empty;

    /// <summary>The facts of the player's characters, rebuilt in the slow phase; safe to read from the interface thread.</summary>
    public FrozenDictionary<CharacterId, VitalFacts> Facts => _facts;

    public void SlowWorld(World world)
    {
        if (!settings.Enabled || world.Tick % Every != 0) return;

        var table = world.Characters;
        var map = new Dictionary<CharacterId, VitalFacts>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is { IsPlayer: true, Medical: { Dead: false } medical } cold) map[table.IdOf(slot)] = FactsOf(world, cold, medical);
        }

        _facts = map.ToFrozenDictionary();
    }

    VitalFacts FactsOf(World world, CharacterCold cold, MedicalState medical)
    {
        bool leg = medical.Crawling || medical.LegsMessedUp;
        bool arm = !medical.ArmWorks(left: true) || !medical.ArmWorks(left: false);
        return new(Wounds(cold, medical), leg, arm, cold.Race?.IsRobot ?? false, InRepairBed(world, cold));
    }

    /// <summary>
    /// The health word's fold over the parts (docs/game/ui-screens.md "Health word choice", h flesh, d bandage, s stun, m maximum): 1 for a
    /// non-vital part with h + d - s at or under -m (only from 0), else for one with h + d under 0, 3 when vital and not at or above the KO point
    /// (none known counts as under), 2 otherwise; the largest.
    /// </summary>
    int Wounds(CharacterCold cold, MedicalState medical)
    {
        float? ko = cold.Stats is { } stats ? MedicalState.KoPoint(data.Bodies.Constants, stats.ScaledToughness) : null;
        int worst = 0;
        foreach (var part in medical.Parts)
        {
            float healed = part.Flesh + part.Bandage;
            if (healed - part.Stun <= -part.MaxHp)
            {
                if (!part.IsVital && worst == 0) worst = 1;
            }
            else if (healed < 0)
            {
                bool belowKo = !(ko is { } point && healed >= point);
                worst = Math.Max(worst, part.IsVital && belowKo ? 3 : 2);
            }
        }

        return worst;
    }

    bool InRepairBed(World world, CharacterCold cold)
    {
        if (cold is not { InSomething: HeldState.Bed, InWhat: { } id } || world.Fixtures.Find(id) is not { } bed) return false;

        return _repairBeds.GetOrAdd(bed.RecordId, IsRepairBed);
    }

    bool IsRepairBed(string record) =>
        data.Db.Find(record) is { } bed
        && bed.GetReferences("functionality").Any(r => data.Db.Find(r.TargetStringId)?.GetInt("function") == (int)BedKind.RepairBed);
}
