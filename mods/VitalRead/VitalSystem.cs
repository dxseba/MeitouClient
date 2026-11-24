using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.VitalRead;

/// <summary>
/// Publishes each player character's <see cref="VitalFacts"/> every <see cref="Every"/> ticks, for the portrait bar on the interface's thread: the
/// dying and recovery-coma tests, the crippled legs and arms (<see cref="MedicalState"/>) and the function of its bed.
/// </summary>
sealed class VitalSystem(VitalReadSettings settings, PopulationData data) : ITickSystem
{
    const int Every = 5;

    /// <summary>
    /// Per bed record: its bed function (<see cref="VitalRules.RepairBed"/>, <see cref="VitalRules.Bed"/> or 0).
    /// </summary>
    readonly ConcurrentDictionary<string, int> _bedFunctions = new(StringComparer.Ordinal);

    volatile FrozenDictionary<CharacterId, VitalFacts> _facts = FrozenDictionary<CharacterId, VitalFacts>.Empty;

    /// <summary>
    /// The facts of the player's characters, rebuilt in the slow phase; safe to read from the interface thread.
    /// </summary>
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
        bool dying = DyingTerms(cold, medical);
        bool leg = medical.Crawling && !medical.CanKick;
        bool arm = !medical.BothArmsWork;
        return new(dying && !medical.PlayingDead, RecoveryComa(medical, dying), leg, arm, medical.ReallyHungry, BedFunction(world, cold));
    }

    /// <summary>
    /// The original's dying terms (the game's own "Dying" status, the fork's v2): blood-loss trauma (<see cref="MedicalState.BloodKo"/>),
    /// <c>isProbablyDying</c> (<see cref="MedicalState.ProbablyDying"/>) or blood at or under the KO point (<c>pointOfNoReturn</c>).
    /// </summary>
    bool DyingTerms(CharacterCold cold, MedicalState medical)
    {
        bool pastNoReturn = KoPoint(cold) is { } point && medical.Blood <= point;
        return medical.BloodKo || medical.ProbablyDying || pastNoReturn;
    }

    /// <summary>
    /// The original's recovery coma: the coma flag, <c>!canGetUpWakeUp</c>, no KO timer left, none of the dying terms and no bleeding (its extra
    /// blood loss has no counterpart). It does not need the unconscious flag.
    /// </summary>
    static bool RecoveryComa(MedicalState medical, bool dying) =>
        medical.Coma && !medical.CanGetUpWakeUp && medical.KoTimer <= 0 && !dying && medical.Bleeding <= 0;

    float? KoPoint(CharacterCold cold) =>
        cold.Stats is { } stats ? MedicalState.KoPoint(data.Bodies.Constants, stats.ScaledToughness) : null;

    /// <summary>
    /// The function of the bed the character lies in (<c>inSomething == IN_BED</c>, the building's function), 0 when none.
    /// </summary>
    int BedFunction(World world, CharacterCold cold)
    {
        if (cold is not { InSomething: HeldState.Bed, InWhat: { } id } || world.Fixtures.Find(id) is not { } bed) return 0;

        return _bedFunctions.GetOrAdd(bed.RecordId, FunctionOf);
    }

    int FunctionOf(string record)
    {
        if (data.Db.Find(record) is not { } bed) return 0;

        var functions = bed.GetReferences("functionality").Select(r => data.Db.Find(r.TargetStringId)?.GetInt("function")).ToList();
        if (functions.Contains(VitalRules.RepairBed)) return VitalRules.RepairBed;

        return functions.Contains(VitalRules.Bed) ? VitalRules.Bed : 0;
    }
}
