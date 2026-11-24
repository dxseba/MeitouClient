using System.Collections.Frozen;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.EquipmentDurability;

/// <summary>
/// <paramref name="Repairer"/> repairs <paramref name="Owner"/>'s weapon (or armour) with its own repair item and smithing.
/// </summary>
public sealed record RepairOrder(CharacterId Repairer, CharacterId Owner, bool Armour) : SimCommand;

/// <summary>
/// The original's timed repairs (<c>StartRepairJob</c>, <c>ProcessRepairJobs</c>): a repair of one's own gear stands still for <c>RepairSeconds</c>
/// and is applied at the end; one of a squad member's gear first walks the repairer within <c>RepairReach</c> of the member, waits 0.4 s, then repairs
/// the same way. Moving (0.75 units), being attacked or going down cancels it and nothing is used up. Times are game seconds. While repairing the
/// repairer plays the repair animation (<see cref="Move"/>), and the repair sound plays as the work begins or as it is applied.
/// </summary>
sealed class Repairs(DurabilityConfig config, PopulationData data, RngPurpose purpose, Action<string> log, Func<string, SoundHandle> sound)
{
    #region Jobs

    /// <summary>
    /// The phases: walking to the member, standing a moment on arrival, repairing.
    /// </summary>
    enum Phase { Walk, Settle, Work }

    /// <summary>
    /// A repair under way.
    /// </summary>
    sealed class Job
    {
        public required CharacterId Repairer { get; init; }

        public required CharacterId Owner { get; init; }

        public required bool Armour { get; init; }

        public Phase Phase { get; set; }

        /// <summary>
        /// Seconds since the order, in the phase, standing still while walking, and repairing.
        /// </summary>
        public float Age { get; set; }

        public float Walked { get; set; }

        public float Stalled { get; set; }

        public float Settled { get; set; }

        public float Worked { get; set; }

        /// <summary>
        /// The nearest the walk came, where the repairer stood when the work began, and where the member was when the walk was last sent.
        /// </summary>
        public float Best { get; set; }

        public Vector3 StandAt { get; set; }

        public Vector3 Goal { get; set; }

        /// <summary>
        /// The tick the work began, and whether its sound already played (at the start).
        /// </summary>
        public long WorkStart { get; set; }

        public bool Sounded { get; set; }

        /// <summary>The sound posted as the work began (stopped when the repair is cancelled, as the original's).</summary>
        public SoundHandle Sound { get; set; } = SoundHandle.None;

        /// <summary>The steps of the work the game kept the repair clip off the character (blocked since the last one counted).</summary>
        public int Restarts { get; set; }

        public bool Blocked { get; set; }

        public bool Cross => Repairer != Owner;
    }

    /// <summary>
    /// The repairs under way, in order of their start.
    /// </summary>
    readonly List<Job> _jobs = [];

    /// <summary>
    /// The characters attacked since the last step (they cancel their repair).
    /// </summary>
    readonly List<CharacterId> _attacked = [];

    /// <summary>
    /// A melee blow reached the character (hit, blocked or dodged).
    /// </summary>
    public void Attacked(CharacterId id)
    {
        if (_jobs.Count > 0) _attacked.Add(id);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_jobs.Count);
        foreach (var job in _jobs)
        {
            hasher.Add(job.Repairer);
            hasher.Add(job.Owner);
            hasher.Add((int)job.Phase);
            hasher.Add(job.Age);
            hasher.Add(job.WorkStart);
        }
    }

    /// <summary>
    /// Starts a repair (the original's <c>StartRepairJob</c>): ignored while the repairer has one, or when there is nothing to repair.
    /// </summary>
    public void Start(World world, RepairOrder order, Condition rules)
    {
        if (!config.Enabled || !config.Repair || _jobs.Any(j => j.Repairer == order.Repairer)) return;
        if (order.Repairer != order.Owner && !config.RepairOthers) return;
        if (Cold(world, order.Repairer) is not { IsPlayer: true } repairer || Cold(world, order.Owner) is not { IsPlayer: true } owner) return;

        var job = new Job { Repairer = order.Repairer, Owner = order.Owner, Armour = order.Armour, Best = float.MaxValue };
        if (!Repair(world, repairer, owner, order.Armour, rules, commit: false))
        {
            log("repair: nothing to repair, or no repair item");
            return;
        }

        if (job.Cross && Distance(world, job) is { } d && d > config.Reach)
        {
            (job.Phase, job.Best, job.Goal) = (Phase.Walk, d, Position(world, order.Owner));
            world.GiveOrder(order.Repairer, TaskType.MoveCusOrdered, point: job.Goal);
            _jobs.Add(job);
            return;
        }

        if (config.RepairInstant) Commit(world, job, repairer, owner, rules);
        else Begin(world, job);
    }

    /// <summary>
    /// Runs every repair once a tick.
    /// </summary>
    public void Step(World world, Condition rules)
    {
        float dt = world.TickSeconds;
        for (int i = 0; i < _jobs.Count;)
        {
            var job = _jobs[i];
            job.Age += dt;
            string? why = Stepped(world, job, dt, rules, out bool finished);
            if (why is null && !finished)
            {
                i++;
                continue;
            }

            _jobs.RemoveAt(i);
            if (why is not null) Cancelled(job, why);
        }

        _attacked.Clear();
    }

    /// <summary>The original's cancel: its sound stops, then the log line.</summary>
    void Cancelled(Job job, string why)
    {
        job.Sound.Stop();
        log("repair cancelled: " + why + " (nothing used up)");
    }

    /// <summary>
    /// One tick of a job: a reason to cancel it, or null (finished true once it repaired).
    /// </summary>
    string? Stepped(World world, Job job, float dt, Condition rules, out bool finished)
    {
        finished = false;
        if (job.Age > 60) return "stale";
        if (Cold(world, job.Repairer) is not { } repairer || repairer.Medical is { Dead: true } or { Unconscious: true })
            return "the character went down";
        if (_attacked.Contains(job.Repairer)) return "the character was attacked";
        if (Cold(world, job.Owner) is not { } owner || Distance(world, job) is not { } dist) return "the squad member to repair is gone";

        return job.Phase switch
        {
            Phase.Walk => Walk(world, job, dist, dt),
            Phase.Settle => Settle(world, job, repairer, owner, dist, dt, rules, out finished),
            _ => Work(world, job, repairer, owner, dist, dt, rules, out finished),
        };
    }

    /// <summary>
    /// Walking to the member: arrived within reach (or within reach + 1 after half a second without getting nearer), else cancelled after 25 s, 4 s
    /// without getting nearer, or 4 units farther than its best (another order); the walk follows a member that moved 3 units.
    /// </summary>
    string? Walk(World world, Job job, float dist, float dt)
    {
        job.Walked += dt;
        if (dist < job.Best - 0.3f)
        {
            job.Best = dist;
            job.Stalled = 0;
        }
        else job.Stalled += dt;

        var here = Position(world, job.Repairer);
        if (dist <= config.Reach || job.Stalled >= 0.5f && dist <= config.Reach + 1)
        {
            world.GiveOrder(job.Repairer, TaskType.MoveCusOrdered, point: here);
            (job.Phase, job.Settled) = (Phase.Settle, 0);
            return null;
        }

        if (job.Walked > 25)
        {
            world.GiveOrder(job.Repairer, TaskType.MoveCusOrdered, point: here);
            return "the walk took too long";
        }

        if (dist > job.Best + 4) return "the character was given another order";
        if (job.Stalled > 4) return "the character could not reach the squad member";

        Follow(world, job);
        return null;
    }

    void Follow(World world, Job job)
    {
        var there = Position(world, job.Owner);
        if (Vector3.Distance(there, job.Goal) <= 3) return;

        job.Goal = there;
        world.GiveOrder(job.Repairer, TaskType.MoveCusOrdered, point: there);
    }

    /// <summary>
    /// Standing 0.4 s on arrival, then repairing if the member is still within reach + 2 and something still needs it.
    /// </summary>
    string? Settle(World world, Job job, CharacterCold repairer, CharacterCold owner, float dist, float dt, Condition rules, out bool finished)
    {
        finished = false;
        job.Settled += dt;
        if (job.Settled < 0.4f) return null;
        if (dist > config.Reach + 2) return "the squad member moved away";
        if (!Repair(world, repairer, owner, job.Armour, rules, commit: false)) return "there is nothing left to repair";

        if (!config.RepairInstant)
        {
            Begin(world, job);
            return null;
        }

        Commit(world, job, repairer, owner, rules);
        finished = true;
        return null;
    }

    /// <summary>
    /// Repairing: cancelled when the repairer moved 0.75 units or the member is 4 units past reach; applied after <c>RepairSeconds</c>.
    /// </summary>
    string? Work(World world, Job job, CharacterCold repairer, CharacterCold owner, float dist, float dt, Condition rules, out bool finished)
    {
        finished = false;
        job.Worked += dt;
        if (Vector3.Distance(Position(world, job.Repairer), job.StandAt) > 0.75f) return "the character moved";
        if (job.Cross && dist > config.Reach + 4) return "the squad member moved away";
        Watch(world, job);
        if (job.Worked < config.RepairSeconds) return null;

        if (job.Restarts > 0) log($"repair: the game interrupted the repair clip {job.Restarts} times");
        Commit(world, job, repairer, owner, rules);
        finished = true;
        return null;
    }

    /// <summary>
    /// The original keeps putting its repair clip back while the game's own actions replace it, and does not cancel for that; the port's clip
    /// is asked for every step, so it plays again once the game lets it (<see cref="CombatSystem.ModMoveBlockOf"/>). Each time the game keeps
    /// it off is counted and logged (first time and the count at the end), as the original logs its restarts.
    /// </summary>
    void Watch(World world, Job job)
    {
        if (config.RepairAnimation == "none" || world.Systems.OfType<CombatSystem>().FirstOrDefault() is not { } combat) return;

        bool blocked = combat.ModMoveBlockOf(world, job.Repairer) != ModMoveBlock.None;
        if (blocked && !job.Blocked && ++job.Restarts == 1) log("repair: the game interrupted the repair clip; it plays again once the game lets it");
        job.Blocked = blocked;
    }

    void Begin(World world, Job job)
    {
        (job.Phase, job.Worked, job.StandAt, job.WorkStart) = (Phase.Work, 0, Position(world, job.Repairer), world.Tick);
        if (!_jobs.Contains(job)) _jobs.Add(job);
        if (config.SoundAtStart) job.Sounded = Play(job);
    }

    /// <summary>
    /// Applies the repair; its sound plays now unless it played as the work began.
    /// </summary>
    void Commit(World world, Job job, CharacterCold repairer, CharacterCold owner, Condition rules)
    {
        if (Repair(world, repairer, owner, job.Armour, rules, commit: true) && !job.Sounded) Play(job);
    }

    /// <summary>
    /// Posts the repair sound (the original's <c>RepairSound</c> file in the mod folder, on the interface); false when there is none.
    /// </summary>
    bool Play(Job job)
    {
        if (config.RepairSound.Length == 0) return false;

        job.Sound = sound(config.RepairSound);
        return true;
    }

    /// <summary>
    /// The repair animation of a character repairing (the original's first-aid action, <c>RepairAnimation = auto</c> its <c>medic</c>, else the
    /// named one) at its share of <c>RepairSeconds</c>; null for anyone else. Read in the animation's parallel step: the jobs change only in the
    /// serial step.
    /// </summary>
    public CombatMove? Move(World world, CharacterId id, long tick)
    {
        if (config.RepairInstant || config.RepairAnimation == "none") return null;

        foreach (var job in _jobs)
        {
            if (job.Repairer != id || job.Phase != Phase.Work) continue;

            float progress = (tick - job.WorkStart) * world.TickSeconds / config.RepairSeconds;
            string clip = config.RepairAnimation is "auto" or "" ? "medic" : config.RepairAnimation;
            return CombatMove.Clip(clip, Math.Clamp(progress, 0, 0.999f));
        }

        return null;
    }

    static CharacterCold? Cold(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Cold(slot) : null;

    static Vector3 Position(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Next[slot].Position : default;

    static float? Distance(World world, Job job) =>
        world.Characters.TryResolveNext(job.Owner, out int o) && world.Characters.TryResolveNext(job.Repairer, out int r)
            ? Vector3.Distance(world.Characters.Next[o].Position, world.Characters.Next[r].Position)
            : null;

    #endregion

    #region Repairing

    /// <summary>
    /// The original's <c>DoRepair</c> and <c>DoRepairArmour</c>: the repairer needs a repair item; the target is the first piece below its cap
    /// (weapons: slot 1 then slot 2; armour: helmet, chest, shirt, legs, boots). Committed, one repair item is used up and the piece gains
    /// <c>RepairAmount</c> + <c>SmithingBonusPerLevel</c> x the repairer's base smithing level (<c>RepairAmountAtZero</c> and no bonus when
    /// broken), up to its cap. False when nothing can be repaired.
    /// </summary>
    bool Repair(World world, CharacterCold repairer, CharacterCold owner, bool armour, Condition rules, bool commit)
    {
        if (repairer.Inventory is not { } tools || FindTool(tools) is not { } tool || owner.Inventory is not { } inventory) return false;
        if (Target(world, owner, inventory, armour, rules) is not { } target) return false;

        if (!commit) return true;
        if (!tools.RemoveOne(tool)) return false;

        var (item, c, dur, cap) = target;
        int bonus = dur > 0 && config.SmithPerLevel > 0 ? Bonus(repairer, armour ? StatsEnumerated.SmithingArmour : SmithOf(c)) : 0;
        int repaired = Math.Min(cap, dur + (dur <= 0 ? config.RepairAtZero : config.RepairAmount) + bonus);
        Condition.Set(item, repaired);
        log($"repaired '{item.Name}': {dur} -> {repaired} / {cap}");
        return true;
    }

    /// <summary>
    /// Crossbows train Crossbow Smithing, other weapons Weapon Smithing.
    /// </summary>
    static StatsEnumerated SmithOf(GearClass c) => c == GearClass.Ranged ? StatsEnumerated.SmithingBow : StatsEnumerated.SmithingWeapon;

    int Bonus(CharacterCold repairer, StatsEnumerated stat)
    {
        int level = (int)(repairer.Stats?.Stored(stat) ?? 0);
        return level > 0 ? (int)(level * config.SmithPerLevel) : 0;
    }

    (ItemInstance Item, GearClass Class, int Dur, int Cap)? Target(World world, CharacterCold owner, Inventory inventory, bool armour, Condition rules)
    {
        string[] sections = armour ? Condition.ArmourSections : Condition.WeaponSections;
        foreach (string section in sections)
        {
            if (Gear.In(inventory, section) is not { } item || !rules.Tracked(item, out var c) || Condition.IsArmour(c) != armour) continue;

            int cap = rules.RangeOf(c).Cap;
            int dur = rules.GetOrRoll(item, c, Rng.Hash(world.Seed, Rng.StableHash(item.Record) ^ (ulong)world.Tick << 20, purpose, (ulong)Array.IndexOf(sections, section)));
            if (dur < cap) return (item, c, dur, cap);
        }

        return null;
    }

    /// <summary>
    /// The repair item in the character's own sections (not a bag): by its record id (the vanilla Tools, or <c>RepairItemID</c>), or by name
    /// (<c>RepairItem</c> and the translated names the original knows).
    /// </summary>
    ItemInstance? FindTool(Inventory inventory) => inventory.Items.FirstOrDefault(IsTool);

    bool IsTool(ItemInstance item)
    {
        if (item.Record == ToolsRecord || config.RepairItemId.Length > 0 && item.Record == config.RepairItemId) return true;

        string? name = data.Db.Find(item.Record)?.Name;
        return IsToolName(item.Name) || name is not null && IsToolName(name);
    }

    bool IsToolName(string name) => config.RepairItems.Contains(name) || TranslatedTools.Contains(name);

    /// <summary>
    /// The vanilla ITEM "Tools" (<c>43404-changes_otto.mod</c>), which the original matches in every language.
    /// </summary>
    const string ToolsRecord = "43404-changes_otto.mod";

    /// <summary>
    /// The translated names of Tools the original knows (Portuguese, German, French, Spanish, Russian).
    /// </summary>
    static readonly string[] TranslatedTools = ["Ferramentas", "Werkzeuge", "Outils", "Herramientas", "Инструменты"];

    /// <summary>
    /// The player characters carrying a repair item.
    /// </summary>
    public FrozenSet<CharacterId> Holders(World world)
    {
        var table = world.Characters;
        var holders = new List<CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true, Inventory: { } inventory } && FindTool(inventory) is not null)
                holders.Add(table.IdOf(slot));
        }

        return holders.ToFrozenSet();
    }

    #endregion

    #region Breaking

    /// <summary>
    /// <c>BreakWeapons</c>: the piece is taken off and destroyed, and the owner's fighting gear and look made again from its inventory
    /// (<see cref="CharacterRefresh"/>).
    /// </summary>
    public void Destroy(CharacterCold owner, ItemInstance item)
    {
        if (owner.Inventory?.Remove(item) != true) return;

        log($"broke '{item.Name}'");
        CharacterRefresh.Refresh(data, owner, CharacterRefreshKind.Equipment);
    }

    #endregion
}
