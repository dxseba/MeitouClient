using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// A player medic able to take a first aid order this pulse: where it stands, its kits, whether it fights, the outpost it stands in (0: none)
/// and its race (hazard immunity).
/// </summary>
internal readonly record struct Medic(int Slot, CharacterId Id, System.Numerics.Vector3 Position, bool Organic, bool Robot, bool InCombat,
    int Town, RaceData? Race);

/// <summary>
/// A downed character in reach of some medic with the right kit: its group, its need, the order row (25 first aid, 60 for a robot), the hazard on
/// it and the outpost it lies in.
/// </summary>
internal readonly record struct Patient(int Slot, CharacterId Id, System.Numerics.Vector3 Position, bool Robot, float Need, PatientGroup Group,
    WeatherAffecting Hazard, int Town);

/// <summary>
/// The automatic medic (the original's <c>GameWorld</c> main-loop hook): every <see cref="MedicTuning.PulseSeconds"/> of game time, unless orders
/// went out in the last <see cref="MedicTuning.OrderCooldownSeconds"/>, the player characters with the Medic job and a kit get the game's own
/// First Aid order (row 25, 60 for a robot) on the downed characters nearby, one patient per medic, by group then urgency. Its state (manual
/// order times, cooldowns, blocked patients, pending checks) is world-local, hashed and not saved: the original clears it on every load.
/// </summary>
internal sealed partial class MedicSystem(MedicSettings settings, MedicTuning tuning, ModContext context, BodySystem? bodies)
    : ITickSystem, IStateHashed
{
    readonly MedicTuning _tuning = tuning;
    MedicRules _rules = MedicRules.From(settings, tuning.UnitsPerMeter);
    double _nextPulse;
    double _quietUntil;

    double Now(World world) => world.Tick * (double)world.TickSeconds;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands) => _rules = MedicRules.From(settings, _tuning.UnitsPerMeter);

    public void SlowWorld(World world)
    {
        double now = Now(world);
        CheckPending(world, now);
        if (!_tuning.AutoPulse || now < _nextPulse) return;

        _nextPulse = now + _tuning.PulseSeconds;
        Prune(now);
        if (now < _quietUntil) return;

        int sent = Pulse(world, now);
        if (sent == 0) return;

        _quietUntil = now + _tuning.OrderCooldownSeconds;
        if (_rules.Notify) world.Post(context.Tr("Medic AI: medics sent to give first aid: ") + sent);
    }

    /// <summary>
    /// One pulse: the medics, the patients in their reach, the pairs, then the orders; the number of orders given.
    /// </summary>
    int Pulse(World world, double now)
    {
        var medics = Medics(world, now);
        if (medics.Count == 0) return 0;

        var patients = Patients(world, medics, now);
        int sent = 0;
        foreach (var (medic, patient) in Assign(medics, patients, now))
        {
            if (Order(world, medic, patient, now)) sent++;
        }

        return sent;
    }

    /// <summary>
    /// Each patient in order takes the nearest free medic that has its kit and reaches it (the radius, or the same outpost), is immune to the
    /// hazard on it, and, when fighting, is a combat medic within its radius of an own or allied patient. One medic per patient, at most
    /// <see cref="MedicTuning.OrdersPerPulse"/> orders. With the kit fallback, a medic without the patient's kit may go when no medic holding it
    /// reaches the patient.
    /// </summary>
    List<(Medic Medic, Patient Patient)> Assign(List<Medic> medics, List<Patient> patients, double now)
    {
        var free = medics.Where(m => !Cooling(_actorCooldown, m.Id, now, _tuning.ActorCooldownSeconds)).ToList();
        var pairs = new List<(Medic, Patient)>();
        int limit = _tuning.OrdersPerPulse(medics.Count);
        foreach (var patient in patients)
        {
            if (free.Count == 0 || pairs.Count >= limit) break;

            bool fallback = _tuning.KitFallback && !medics.Any(m => Reaches(m, patient));
            int best = Nearest(free, patient, fallback);
            if (best < 0) continue;

            pairs.Add((free[best], patient));
            free.RemoveAt(best);
        }

        return pairs;
    }

    int Nearest(List<Medic> free, in Patient patient, bool fallback)
    {
        int best = -1;
        float bestSq = 0;
        for (int i = 0; i < free.Count; i++)
        {
            var medic = free[i];
            float d2 = System.Numerics.Vector3.DistanceSquared(medic.Position, patient.Position);
            if (!Serves(medic, patient, d2, fallback) || best >= 0 && d2 >= bestSq) continue;

            best = i;
            bestSq = d2;
        }

        return best;
    }

    bool Serves(in Medic medic, in Patient patient, float d2, bool fallback)
    {
        if (!(fallback || HasKit(medic, patient)) || !InReach(medic, patient, d2)) return false;
        if (_rules.AvoidHazards && patient.Hazard != WeatherAffecting.None && medic.Race?.ImmuneTo(patient.Hazard) != true) return false;
        if (!medic.InCombat) return true;

        return d2 <= _rules.CombatRadiusSq && patient.Group < PatientGroup.Neutral;
    }

    static bool HasKit(in Medic medic, in Patient patient) => patient.Robot ? medic.Robot : medic.Organic;

    bool InReach(in Medic medic, in Patient patient, float d2) =>
        d2 <= (patient.Robot ? _rules.RobotRadiusSq : _rules.FieldRadiusSq) || _rules.WholeBase && medic.Town != 0 && medic.Town == patient.Town;

    /// <summary>
    /// Gives the medic the order on the patient through the player's order queue, when the game would take it now (the original's
    /// <c>canTakePlayerOrdersAtThisTime</c>); notes the cooldowns and the approach check.
    /// </summary>
    bool Order(World world, in Medic medic, in Patient patient, double now)
    {
        var row = patient.Robot && world.CanOrder(TaskType.FirstAidRobot, medic.Id, patient.Id).Can ? TaskType.FirstAidRobot : TaskType.FirstAidOrder;
        if (!world.CanOrder(row, medic.Id, patient.Id).Can) return false;

        _ordering = true;
        try
        {
            world.GiveOrder(medic.Id, row, patient.Id);
        }
        finally
        {
            _ordering = false;
        }

        _targetCooldown[patient.Id] = now;
        _actorCooldown[medic.Id] = now;
        if (_tuning.Debug) context.Log($"order {row} medic {medic.Id} patient {patient.Id} group {patient.Group} need {patient.Need:0.###}");
        _pending[medic.Id] = new Pending(patient.Id, now, System.Numerics.Vector3.DistanceSquared(medic.Position, patient.Position));
        return true;
    }
}
