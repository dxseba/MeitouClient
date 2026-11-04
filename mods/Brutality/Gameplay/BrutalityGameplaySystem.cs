using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Owns progression and cut decisions; native characters and medical outcomes are changed only by the separate bridge.</summary>
public sealed partial class BrutalityGameplaySystem(CombatSystem? combat, GameConstants constants, BodyOptions options, RngPurpose purpose, int playerFaction = -1)
    : ICombatHitObserver, IStateHashed
{
    sealed class Actor
    {
        public required BrutalityProgress Progress;
        public SevereHeadWoundState Severe = new(0);
        public HeadCutState? Head;
        public TorsoBisectionState? Torso;
        public IReadOnlyList<LimbCutState> Limbs = [];
        public WoundBody Wounds = new();
    }
    readonly Dictionary<CharacterId, Actor> actors = [];
    readonly ConditionalWeakTable<CharacterCold, Actor> retained = new();
    readonly Dictionary<GoreHitKey, GoreHitBridge> bridges = [];
    readonly record struct HitWork(CombatHit Hit, GoreHitBridge? Bridge, float Brutality, bool VictimDead,
        float Maximum, BodyPartTemplate? Part, bool Robot, bool Animal);
    readonly Queue<HitWork> pending = new();
    readonly List<GoreCutRequest> history = [];
    readonly Dictionary<string, BrutalityDurableState> unresolved = new(StringComparer.Ordinal);
    readonly XpService xp = new(constants, options);
    long nextRequest;
    bool changed;
    public GoreIntegrationGap Gaps { get; private set; }
    public IReadOnlyList<BrutalityActorView> ActorViews { get; private set; } = [];
    public IReadOnlyList<GoreCutRequest> CutRequests { get; private set; } = [];

    public void RecordHitBridge(GoreHitKey key, GoreHitBridge context) => bridges[key] = context with
    {
        SeveredLimbSlots = context.SeveredLimbSlots.Count == 0 ? [] : Array.AsReadOnly(context.SeveredLimbSlots.ToArray()),
    };

    public float LevelOf(World world, CharacterId id) => Find(world, id)?.Progress.Level ?? 0;
    public bool SetLevel(World world, CharacterId id, float level)
    {
        if (!float.IsFinite(level) || Find(world, id) is not { } actor) return false;
        actor.Progress = BrutalityProgress.FromLevel(level); changed = true; return true;
    }

    Actor? Find(World world, CharacterId id)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold) return null;
        if (actors.TryGetValue(id, out var existing)) return existing;
        int ownFaction = playerFaction >= 0 ? playerFaction : world.Player.Faction;
        var actor = retained.GetValue(cold, owner => new Actor
        {
            Progress = !owner.IsPlayer && (ownFaction < 0 || owner.Faction != ownFaction)
                && owner.Fighter is not { IsAnimal: true } && owner.Stats is { } stats
                ? BrutalityProgress.FromCombat(stats) : new(),
        });
        actors[id] = actor; changed = true; return actor;
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        bridges.Remove(GoreHitKey.From(hit), out var context);
        var attacker = Find(world, hit.Attacker);
        Find(world, hit.Victim);
        float brutality = attacker?.Progress.Level ?? 0;
        if (context is null) Gaps |= GoreIntegrationGap.PreHitContext;
        if (hit.Source == CombatHitSource.Melee && hit.VictimWasAlive && attacker is not null
            && context is { UsefulRemainingBeforeHit: { } remaining, ChallengeFactor: { } challenge })
        {
            // Applying own-state XP now preserves unlock ordering for several contacts in the same tick.
            float gained = attacker.Progress.Award(BrutalityPolicy.UsefulDamage(hit.Damage, remaining)
                * Math.Clamp(challenge, 0, 6) * MathF.Max(0, constants.ExpGainMultiplier));
            changed |= gained > 0;
        }
        CharacterCold? victim = world.Characters.TryResolveNext(hit.Victim, out int victimSlot) ? world.Characters.Cold(victimSlot) : null;
        var part = victim?.Medical is { } medical && (uint)hit.Part < (uint)medical.Parts.Count ? medical.Parts[hit.Part] : null;
        pending.Enqueue(new(hit, context, brutality, victim?.Medical?.Dead == true, part?.MaxHp ?? 0,
            part?.Template, victim?.Race?.IsRobot == true, victim?.Fighter?.IsAnimal == true));
    }

    /// <summary>Call before native damage with its selected part. A bridge may supply the exact write-side target state and medical context.</summary>
    public GoreHitBridge CaptureBeforeHit(World world, CharacterId attacker, CharacterId victim, int part,
        in DamagePacket packet, CombatWoundMechanism mechanism, MedicalContext? medicalContext = null, CombatSlot? targetState = null)
    {
        if (!world.Characters.TryResolveNext(victim, out int victimSlot) || world.Characters.Cold(victimSlot) is not { Medical: { } medical } cold
            || (uint)part >= (uint)medical.Parts.Count) return new() { Mechanism = mechanism };
        var struck = medical.Parts[part];
        float challenge = 0;
        var state = targetState ?? combat?.StateOf(victim) ?? default;
        if (Eligible(world, attacker, victim, state) && world.Characters.Cold(attacker.Slot) is { Fighter: { } fighter, Stats: { } stats, Medical: { } sourceMedical }
            && cold.Stats is { } targetStats)
        {
            float Effective(CharacterStats s, MedicalState m, StatsEnumerated stat) => s[stat] * s.Scale * m.StatMultiplier(stat);
            challenge = Math.Clamp(xp.SkillDifferenceFactor(Effective(stats, sourceMedical, fighter.Unarmed ? StatsEnumerated.MartialArts : StatsEnumerated.MeleeAttack),
                Effective(targetStats, medical, StatsEnumerated.MeleeDefence)), 0, 6);
        }
        return new()
        {
            Mechanism = mechanism, WeaponCategory = world.Characters.TryResolveNext(attacker, out int sourceSlot) ? world.Characters.Cold(sourceSlot)?.Fighter?.TechniqueCategory ?? -1 : -1, PreArmourCut = packet.Cut, UnarmouredPacket = packet,
            UsefulRemainingBeforeHit = MathF.Max(0, struck.Flesh + struck.MaxHp - struck.Stun), ChallengeFactor = challenge,
            PartMaximumBeforeHit = struck.MaxHp, OrganicBeforeHit = cold.Race is { } race ? !struck.IsRobotic(race) : null,
            AllowsMedicalSeverance = medicalContext is { } ctx ? ctx.HasStats && !ctx.ExemptFromWear && medical.HasStatsObject
                && ctx.Options.Dismemberment >= Dismemberment.Rare : null,
        };
    }

    bool Eligible(World world, CharacterId attacker, CharacterId victim, in CombatSlot target)
    {
        var table = world.Characters;
        if (attacker == victim || !table.TryResolveNext(attacker, out int a) || !table.TryResolveNext(victim, out int d)
            || table.Cold(a) is not { Medical: { Dead: false, Incapacitated: false }, Stats: not null } source
            || table.Cold(d) is not { Medical: { Dead: false, Incapacitated: false, PlayingDead: false } medical, Fighter: { CanAttack: true } fighter, CarriedBy.IsNone: true } other
            || other.InSomething != Meitou.Simulation.Locks.HeldState.None || other.IsImprisoned || CharacterFacts.GettingUp(other, world.Tick)
            || CarrySystem.Asleep(table, d) || target.Down) return false;
        if (!fighter.IsAnimal && !medical.ArmWorks(left: true) && !medical.ArmWorks(left: false)
            && (medical.Prone || medical.Parts.Any(p => p.IsLeg && p.Limb is LimbState.Stump or LimbState.Crushed))) return false;
        var relations = combat?.Relations;
        bool allied = source.Faction == other.Faction || relations is not null && relations.Contains(source.Faction) && relations.Contains(other.Faction)
            && (relations.IsAlly(source.Faction, other.Faction) || relations.IsAlly(other.Faction, source.Faction));
        bool retaliating = target.TargetSlot == attacker.Slot && target.TargetGeneration == attacker.Generation;
        return !allied && (retaliating || CharacterFacts.RegardsEnemy(relations, source.Faction, other.Faction))
            && (other.InCombat || target.TargetSlot >= 0 || target.AttackTech >= 0 || target.ReactTech >= 0);
    }

    public void Schedule(World world)
    {
        BindWorld(world);
        // Match spawn-time NPC seeding before native combat XP changes the raw skills.
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Next[slot].Alive) Find(world, world.Characters.IdOf(slot));
    }

    public void SlowWorld(World world)
    {
        BindWorld(world);
        HealBodyWounds(world);
        while (pending.TryDequeue(out var work)) Apply(world, work);
        RetainSaveState(world);
        foreach (var key in bridges.Keys.Where(k => k.Tick < world.Tick).ToArray()) bridges.Remove(key);
        foreach (var id in actors.Keys.Where(id => !world.Characters.IsAlive(id)).ToArray()) { actors.Remove(id); changed = true; }
        if (!changed) return;
        ActorViews = Array.AsReadOnly(actors.OrderBy(p => Rng.Key(p.Key)).Select(p => View(p.Key, p.Value)).ToArray());
        CutRequests = Array.AsReadOnly(history.ToArray()); changed = false;
        PublishBodyWounds(world);
    }

    void Apply(World world, in HitWork work)
    {
        var hit = work.Hit; var context = work.Bridge; float brutality = work.Brutality;
        if (Find(world, hit.Victim) is not { } actor || work.Part is not { } part
            || !world.Characters.TryResolveNext(hit.Victim, out int victimSlot) || world.Characters.Cold(victimSlot) is not { } cold) return;
        if (context is null || context.Mechanism == CombatWoundMechanism.Unspecified)
        {
            Gaps |= GoreIntegrationGap.PreHitContext;
            var missing = new TorsoBisectionInput(hit.Source, CombatWoundMechanism.Unspecified, part, hit.CutDirection, hit.Damage.Cut,
                work.Maximum, hit.VictimWasAlive, actor.Torso is not null, work.Robot, work.Animal) { Brutality = brutality, Lethal = work.VictimDead };
            TraceDecisions(world, work, missing, nextRequest);
            return;
        }
        long requestBefore = nextRequest;
        var mechanism = context.Mechanism;
        changed |= actor.Wounds.Apply(hit, part, context.PartMaximumBeforeHit ?? work.Maximum, mechanism, context.WeaponCategory);
        var travel = new Vector2(hit.Travel.X, hit.Travel.Z);
        ulong counter = CombatRolls.BlowCounter(hit.Attacker.Slot, hit.Sequence, hit.Blow, 0);
        ulong victimKey = Rng.Key(hit.Victim);
        float Roll(ulong domain) => Rng.Float(Rng.Hash(world.Seed, victimKey ^ domain, purpose, counter));
        float force = mechanism == CombatWoundMechanism.Crush ? hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce : hit.Damage.Cut + hit.Damage.Pierce;
        if (!work.Robot && part.Type == BodyPartType.Head && force >= BrutalityHeadPolicy.HeavyForce)
        {
            var requested = (hit.Victim.Slot % 3) switch { 1 => SevereHeadWoundKind.Scalp, 2 => SevereHeadWoundKind.BrokenSkull, _ => SevereHeadWoundKind.JawLost };
            var placement = HeadWoundPlacementPolicy.Estimate(travel, hit.VictimYaw, hit.CutDirection, force,
                Rng.Hash(world.Seed, Rng.Key(hit.Victim), GoreRandom.HeadWoundVisualPurpose, (ulong)hit.Tick));
            int profile = (hit.CutDirection switch { 1 or 4 or 5 or 7 => 2, 2 => 1, _ => 0 }) + (placement is null && force >= 35 ? 3 : 0);
            var wound = BrutalityHeadPolicy.Apply(actor.Severe, brutality, force, requested, mechanism, profile, placement);
            if (wound != actor.Severe) { actor.Severe = wound; Add(hit, GoreCutKind.SevereHead, severe: wound); }
        }
        if (part.Type == BodyPartType.Head && work.VictimDead && hit.VictimWasAlive && actor.Head is null && options.Decapitation)
        {
            if (context.HeadSeveranceAccepted is null || context.PreArmourCut is null) Gaps |= GoreIntegrationGap.HeadMedicalGate;
            else if (context.HeadSeveranceAccepted == true && !work.Animal
                && HeadCutPolicy.Candidate(brutality, mechanism, hit.CutDirection, context.PreArmourCut.Value,
                    HeadCutPolicy.VisualSeedFor(world.Seed, hit.Victim, hit.Attacker, counter), travel, hit.VictimYaw) is { IsValid: true } head)
            { actor.Head = head; Add(hit, GoreCutKind.Head, head: head); }
        }
        var input = new TorsoBisectionInput(hit.Source, mechanism, part, hit.CutDirection, hit.Damage.Cut,
            context.PartMaximumBeforeHit ?? work.Maximum, hit.VictimWasAlive, actor.Torso is not null, work.Robot, work.Animal)
            { Brutality = brutality, Lethal = work.VictimDead };
        if (TorsoBisectionPolicy.ShouldBisect(input, Roll(0x424953454354UL)))
        {
            var torso = TorsoBisectionPolicy.StateForHit(part, hit.CutDirection,
                TorsoBisectionPolicy.VisualSeedFor(world.Seed, hit.Victim, hit.Attacker, counter), travel, hit.VictimYaw);
            if (torso.IsValid) { actor.Torso = torso; Add(hit, GoreCutKind.Torso, torso: torso); }
        }
        TraceDecisions(world, work, input, requestBefore);
        if (!options.AdvancedRagdolls || !hit.VictimWasAlive) return;
        foreach (int limb in context.SeveredLimbSlots)
            RecordLimbSeverance(hit, limb, Rng.Mix(world.Seed ^ Rng.Key(hit.Victim) ^ Rng.Key(hit.Attacker) ^ (ulong)hit.Tick * 0x9E3779B97F4A7C15UL));
        if (!context.CounterpartResolved && context.SeveredLimbSlots.Count > 0 && !work.VictimDead && cold.Medical is { } medical && context.AllowsMedicalSeverance == true
            && options.Dismemberment >= Dismemberment.Rare && cold.Fighter is not { IsAnimal: true }
            && medical.LimbPart(context.SeveredLimbSlots[0] ^ 1) is { Limb: LimbState.Original or LimbState.Crushed, Template.Severance: true } counterpart
            && counterpart.Template.Type == part.Type && PairedLimbPolicy.ShouldFollowThrough(brutality, mechanism, hit.CutDirection, hit.Damage.Cut, counterpart.MaxHp, Roll(0x504149524544UL)))
        { Gaps |= GoreIntegrationGap.CounterpartDamage; Add(hit, GoreCutKind.PairedLimbAttempt, limbSlot: context.SeveredLimbSlots[0] ^ 1, bridge: context); }
    }

    void TraceDecisions(World world, in HitWork work, in TorsoBisectionInput torso, long requestBefore)
    {
        if (!DeveloperCombatDiagnostics.Active(world)) return;
        var kinds = history.Where(request => request.Sequence > requestBefore).Select(request => request.Kind.ToString()).ToList();
        if (work.Bridge?.SeveredLimbSlots.Count > 0) kinds.Add("NativeLimbLoss");
        DeveloperCombatDiagnostics.Trace(world, work.Hit, work.Bridge, work.Brutality,
            work.Bridge?.PartMaximumBeforeHit ?? work.Maximum, torso,
            history.Any(request => request.Sequence > requestBefore && request.Kind == GoreCutKind.Torso),
            kinds.Count == 0 ? "none" : string.Join(',', kinds));
    }

    public bool RecordLimbSeverance(in CombatHit hit, int limb, ulong seed)
    {
        if (!actors.TryGetValue(hit.Victim, out var actor) || limb is < 0 or > 3) return false;
        var cut = LimbCutPolicy.FromHit(limb, hit.CutDirection, hit.LowStrike, new(hit.Travel.X, hit.Travel.Z), hit.VictimYaw, seed);
        if (!cut.IsValid) return false;
        actor.Limbs = Array.AsReadOnly(actor.Limbs.Where(c => c.LimbSlot != limb).Append(cut).OrderBy(c => c.LimbSlot).ToArray());
        Add(hit, GoreCutKind.Limb, limb: cut, limbSlot: limb); return true;
    }

    void Add(in CombatHit hit, GoreCutKind kind, SevereHeadWoundState? severe = null, HeadCutState? head = null,
        TorsoBisectionState? torso = null, LimbCutState? limb = null, int limbSlot = -1, GoreHitBridge? bridge = null)
    {
        history.Add(new(++nextRequest, hit.Tick, hit.Victim, kind, severe, head, torso, limb, limbSlot, bridge) { Hit = hit });
        if (history.Count > 512) history.RemoveAt(0);
        changed = true;
    }
    static BrutalityActorView View(CharacterId id, Actor actor) => new(id, actor.Progress.Experience, actor.Progress.Level, actor.Progress.Progress01,
        actor.Severe, actor.Head, actor.Torso, actor.Limbs) { Wounds = actor.Wounds.Snapshot() };

    public BrutalityDurableState? DurableFor(CharacterCold cold, string key) =>
        key.Length > 0 && retained.TryGetValue(cold, out var actor)
            ? Durable(key, actor) : null;

    public IReadOnlyList<BrutalityDurableState> ExportDurable(Func<CharacterId, string?> keyFor)
    {
        var result = unresolved.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var (id, actor) in actors)
            if (keyFor(id) is { Length: > 0 } key) result[key] = Durable(key, actor);
        return result.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray();
    }
    public int ImportDurable(World world, IEnumerable<BrutalityDurableState> saved, Func<string, CharacterId?> resolve)
    {
        foreach (var state in saved) if (BrutalityStateCodec.Valid(state)) unresolved[state.Key] = state;
        return ResolveDurable(world, resolve);
    }
    public int ResolveDurable(World world, Func<string, CharacterId?> resolve)
    {
        int count = 0;
        foreach (var (key, saved) in unresolved.ToArray())
            if (resolve(key) is { } id && Find(world, id) is { } actor)
            {
                actor.Progress = new(saved.Experience); actor.Severe = saved.SevereHead; actor.Head = saved.Head; actor.Torso = saved.Torso;
                actor.Limbs = Array.AsReadOnly(saved.Limbs.ToArray()); actor.Wounds = WoundBody.From(saved.Wounds); unresolved.Remove(key); changed = true; count++;
            }
        return count;
    }

    public void Hash(ref StateHasher hash)
    {
        HashSave(ref hash);
        hash.Add(woundHealStep);
        hash.Add((int)Gaps); hash.Add(actors.Count);
        foreach (var (id, actor) in actors.OrderBy(p => Rng.Key(p.Key)))
        { hash.Add(id); HashActor(ref hash, actor.Progress.Experience, actor.Severe, actor.Head, actor.Torso, actor.Limbs, actor.Wounds.Snapshot()); }
        hash.Add(unresolved.Count);
        foreach (var (key, actor) in unresolved.OrderBy(p => p.Key, StringComparer.Ordinal))
        { hash.Add(Rng.StableHash(key)); HashActor(ref hash, actor.Experience, actor.SevereHead, actor.Head, actor.Torso, actor.Limbs, actor.Wounds); }
        hash.Add(pending.Count);
        foreach (var work in pending)
        {
            HashHit(ref hash, work.Hit); HashBridge(ref hash, work.Bridge); hash.Add(work.Brutality); hash.Add(work.VictimDead);
            hash.Add(work.Maximum); hash.Add(work.Part is not null); if (work.Part is { } part) hash.Add(Rng.StableHash(part.StringId));
            hash.Add(work.Robot); hash.Add(work.Animal);
        }
        hash.Add(bridges.Count);
        foreach (var (key, bridge) in bridges.OrderBy(p => p.Key.Tick).ThenBy(p => Rng.Key(p.Key.Attacker))
            .ThenBy(p => Rng.Key(p.Key.Victim)).ThenBy(p => p.Key.Source).ThenBy(p => p.Key.Sequence).ThenBy(p => p.Key.Blow))
        {
            hash.Add(key.Tick); hash.Add((int)key.Source); hash.Add(key.Attacker); hash.Add(key.Victim); hash.Add(key.Sequence); hash.Add(key.Blow);
            HashBridge(ref hash, bridge);
        }
    }
    static void HashActor(ref StateHasher hash, float experience, SevereHeadWoundState severe, HeadCutState? head, TorsoBisectionState? torso, IReadOnlyList<LimbCutState> limbs, BodyWoundState wounds)
    {
        hash.Add(experience); hash.Add(severe.HeavyHits); hash.Add((int)severe.Kind); hash.Add(severe.Profile); hash.Add((int)severe.Mechanism);
        hash.Add(severe.Placement is not null);
        if (severe.Placement is { } p) { hash.Add(p.Point); hash.Add(p.Normal); hash.Add(p.Tangent); hash.Add(p.LengthScale); hash.Add(p.WidthScale); }
        hash.Add(head is not null); if (head is { } h) { HashPlane(ref hash, h.Plane); hash.Add((int)h.Mode); }
        hash.Add(torso is not null); if (torso is { } t) { HashPlane(ref hash, t.Plane); hash.Add(t.FullBody); }
        if (wounds.StateKey() is var woundsKey && woundsKey != 0) { hash.Add(0x57554e44); hash.Add(woundsKey); }
        hash.Add(limbs.Count);
        foreach (var limb in limbs) { hash.Add(limb.LimbSlot); hash.Add(limb.Fraction); hash.Add(limb.Slant); }
    }
    static void HashHit(ref StateHasher hash, in CombatHit hit)
    {
        hash.Add(hit.Tick); hash.Add((int)hit.Source); hash.Add(hit.Attacker); hash.Add(hit.Victim); hash.Add(hit.Sequence); hash.Add(hit.Blow); hash.Add(hit.Part);
        hash.Add(hit.Damage.Cut); hash.Add(hit.Damage.Blunt); hash.Add(hit.Damage.Pierce); hash.Add(hit.Damage.ExtraStun); hash.Add(hit.Damage.Scale);
        hash.Add(hit.Absorbed.Cut); hash.Add(hit.Absorbed.Blunt); hash.Add(hit.Absorbed.Pierce); hash.Add(hit.VictimPosition); hash.Add(hit.Travel);
        hash.Add(hit.VictimYaw); hash.Add(hit.CutDirection); hash.Add(hit.LowStrike); hash.Add(hit.Intended); hash.Add(hit.VictimWasAlive); hash.Add(hit.VictimWasIncapacitated);
    }
    static void HashBridge(ref StateHasher hash, GoreHitBridge? bridge)
    {
        hash.Add(bridge is not null); if (bridge is null) return;
        hash.Add((int)bridge.Mechanism); hash.Add(bridge.WeaponCategory); HashNumber(ref hash, bridge.UsefulRemainingBeforeHit); HashNumber(ref hash, bridge.ChallengeFactor);
        HashNumber(ref hash, bridge.PreArmourCut); HashNumber(ref hash, bridge.PartMaximumBeforeHit);
        HashFlag(ref hash, bridge.OrganicBeforeHit); HashFlag(ref hash, bridge.HeadSeveranceAccepted); HashFlag(ref hash, bridge.AllowsMedicalSeverance); hash.Add(bridge.CounterpartResolved);
        hash.Add(bridge.SeveredLimbSlots.Count); foreach (int limb in bridge.SeveredLimbSlots) hash.Add(limb);
        hash.Add(bridge.UnarmouredPacket is not null);
        if (bridge.UnarmouredPacket is { } packet)
        { hash.Add(packet.Cut); hash.Add(packet.Blunt); hash.Add(packet.Pierce); hash.Add(packet.Stun); hash.Add(packet.BleedMultiplier); hash.Add(packet.ArmourPenetration); }
    }
    static void HashNumber(ref StateHasher hash, float? value) { hash.Add(value.HasValue); if (value is { } number) hash.Add(number); }
    static void HashFlag(ref StateHasher hash, bool? value) { hash.Add(value.HasValue); if (value is { } flag) hash.Add(flag); }
    static void HashPlane(ref StateHasher hash, Vector4 plane) { hash.Add(plane.X); hash.Add(plane.Y); hash.Add(plane.Z); hash.Add(plane.W); }
}
