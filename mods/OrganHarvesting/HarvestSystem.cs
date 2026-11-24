using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Kidnap;
using Meitou.Simulation.Movement;

namespace Meitou.OrganHarvesting;

/// <summary>
/// The harvest: each worker walks to the downed victim and strikes it where it lies with the engine's execution blow (<see cref="CombatSystem.Execute"/>,
/// the blow of the kill-prisoner task 134 the original orders), one blow per chop clip, until it dies. As in the original, one victim is pending
/// at a time: whoever of the player's characters on the harvest landed the last blow on it before it died gets a random organ. Not saved (the
/// original's pending victim is not either). Serial phases only.
/// </summary>
sealed class HarvestSystem(ModContext context, CombatSystem? combat, CaptiveSystem? captives) : ITickSystem
{
    /// <summary>How near the victim a worker strikes from (engine choice, the captive jobs' reach 15 plus room to stop by a body).</summary>
    const float Reach = 20;

    /// <summary>How often a worker that stopped short walks to the victim again before giving up.</summary>
    const int Walks = 3;

    /// <summary>The blow lands this far into each chop clip (the engine's execution timing).</summary>
    const float BlowAt = 0.55f;

    sealed class Job
    {
        public required CharacterId Worker;
        public required CharacterId Victim;
        public Vector3 WalkedTo;
        public int Walks;
        public int Blows;
        public float Time;
    }

    readonly List<Job> _jobs = [];
    CharacterId _pending = CharacterId.None;
    CharacterId _killer = CharacterId.None;
    bool _noCombatLogged;

    float ChopSeconds => MathF.Max(captives?.ChopSeconds ?? 1.5f, 0.1f);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is HarvestOrder order) Start(world, order);
        }
    }

    public void SlowWorld(World world)
    {
        for (int k = _jobs.Count - 1; k >= 0; k--)
        {
            if (!Step(world, _jobs[k])) Drop(world, k);
        }

        Award(world);
    }

    #region Starting

    /// <summary>The original's <c>StartHarvest</c>: the victim must still qualify and the first worker cut; the victim becomes the pending one.</summary>
    void Start(World world, HarvestOrder order)
    {
        if (combat is null)
        {
            if (!_noCombatLogged) context.LogError("no combat system: harvesting does nothing");
            _noCombatLogged = true;
            return;
        }

        var table = world.Characters;
        if (!table.TryResolveNext(order.Victim, out int v) || !Harvestable(table.Cold(v))) return;
        if (order.Workers is not [var lead, ..] || !CanCut(world, lead))
        {
            context.Log("harvester cannot harvest (self-healing race)");
            return;
        }

        _pending = order.Victim;
        _killer = CharacterId.None;
        foreach (var worker in order.Workers)
        {
            if (CanWork(world, worker)) Begin(world, worker, order.Victim, table.Next[v].Position);
        }
    }

    static bool Harvestable(CharacterCold? victim) =>
        victim is { Medical: { Unconscious: true, Dead: false }, Appearance: { } look }
        && HarvestRules.Harvestable(look.Race, look.Animal is not null);

    static bool CanCut(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) && world.Characters.Cold(s) is { Appearance: { } look }
        && !HarvestRules.SelfHealing(look.Race);

    static bool CanWork(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) && world.Characters.Cold(s) is { IsPlayer: true, Medical.Incapacitated: false } c
        && c.CarriedBy.IsNone && !c.IsImprisoned;

    void Begin(World world, CharacterId worker, CharacterId victim, Vector3 at)
    {
        int old = _jobs.FindIndex(j => j.Worker == worker);
        if (old >= 0) Drop(world, old);

        var job = new Job { Worker = worker, Victim = victim };
        _jobs.Add(job);
        WalkTo(world, job, at);
    }

    static void WalkTo(World world, Job job, Vector3 at)
    {
        job.WalkedTo = at;
        job.Walks++;
        world.GiveOrder(job.Worker, TaskType.MoveCusOrdered, point: at);
    }

    #endregion

    #region The kill

    /// <summary>One worker's tick; false when its job ends (either gone, the victim dead or up, another order given).</summary>
    bool Step(World world, Job job)
    {
        var table = world.Characters;
        if (!CanWork(world, job.Worker) || !table.TryResolveNext(job.Worker, out int w)) return false;
        if (!table.TryResolveNext(job.Victim, out int v) || table.Cold(v) is not { Medical: { Dead: false, Incapacitated: true } } victim) return false;
        if (!victim.CarriedBy.IsNone) return false;

        var cold = table.Cold(w)!;
        var at = table.Next[v].Position;
        if (Redirected(world, w, cold, at)) return false;

        if (Flat(table.Next[w].Position - at) > Reach) return Approach(world, w, job, at);

        Strike(world, w, v, job, cold);
        return true;
    }

    /// <summary>The player gave the worker another order: one in its queue that is not a move, or a path ending away from the victim.</summary>
    static bool Redirected(World world, int w, CharacterCold cold, Vector3 at)
    {
        if (cold.OrderQueue.Front is { Row: not TaskType.MoveCusOrdered }) return true;

        bool walking = ((MoveFlags)world.Characters.Next[w].Flags & MoveFlags.HasPath) != 0 && cold.Path.Length > 0;
        return walking && Flat(cold.Path[^1] - at) > Reach * 2;
    }

    /// <summary>Still walking: wait; stopped short (the body was moved, the path ended early): walk again, a few times.</summary>
    bool Approach(World world, int w, Job job, Vector3 at)
    {
        bool walking = ((MoveFlags)world.Characters.Next[w].Flags & MoveFlags.HasPath) != 0;
        if (walking && Flat(job.WalkedTo - at) <= Reach) return true;
        if (job.Walks >= Walks) return false;

        WalkTo(world, job, at);
        return true;
    }

    /// <summary>At the victim: the chop clip, facing it, one execution blow per clip; the blow's worker is the one credited.</summary>
    void Strike(World world, int w, int v, Job job, CharacterCold cold)
    {
        var table = world.Characters;
        var to = table.Next[v].Position - table.Next[w].Position;
        if (to.X * to.X + to.Z * to.Z > 1e-4f) table.Next[w].Yaw = MathF.Atan2(to.X, to.Z);

        cold.ActionClip = CaptiveSystem.ChopClip;
        job.Time += world.TickSeconds;
        if (job.Time < (job.Blows + BlowAt) * ChopSeconds) return;

        job.Blows++;
        combat!.Execute(world, w, v, job.Blows);
        if (job.Victim == _pending) _killer = job.Worker;
    }

    void Drop(World world, int k)
    {
        var job = _jobs[k];
        _jobs.RemoveAt(k);
        if (world.Characters.TryResolveNext(job.Worker, out int w) && world.Characters.Cold(w) is { } cold && cold.ActionClip == CaptiveSystem.ChopClip)
            cold.ActionClip = null;
    }

    static float Flat(Vector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);

    #endregion

    #region The organ

    /// <summary>The original's <c>UpdateHarvest</c>: once the pending victim is dead, its last credited worker gets an organ; then nothing is pending.</summary>
    void Award(World world)
    {
        if (_pending.IsNone) return;

        var table = world.Characters;
        if (!table.TryResolveNext(_pending, out int v) || table.Cold(v) is not { Medical: { } medical })
        {
            (_pending, _killer) = (CharacterId.None, CharacterId.None);
            return;
        }

        if (!medical.Dead) return;

        if (!_killer.IsNone) GiveOrgan(world, _killer);
        (_pending, _killer) = (CharacterId.None, CharacterId.None);
    }

    /// <summary>One of the four organs at random (the original's <c>rand() % 4</c>; here the world's seeded roll) into the harvester's inventory.</summary>
    void GiveOrgan(World world, CharacterId harvester)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(harvester, out int h) || table.Cold(h) is not { Inventory: { } inventory } cold) return;

        ulong roll = Rng.Hash(world.Seed, Rng.Key(harvester), context.RngPurpose, (ulong)world.Tick);
        string id = OrganHarvestingMod.Organs[Rng.Int(roll, OrganHarvestingMod.Organs.Length)];
        if (context.Population.Items.Create(id) is not { } organ)
        {
            context.LogError($"organ data not found: {id}");
            return;
        }

        if (!inventory.Add(organ))
        {
            world.Post($"{cold.Name} has no room for the organ.");
            return;
        }

        context.Log($"gave {id} to {cold.Name}");
    }

    #endregion
}
