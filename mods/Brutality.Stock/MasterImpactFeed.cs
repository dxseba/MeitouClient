using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.World;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Terrain;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality.Effects;

/// <summary>Consumes the public host feed. Pose and race data come from renderer assets and published characters.</summary>
internal sealed class MasterImpactFeed
{
    const int PendingLimit = 256;
    const double MaxHitAge = 1;
    static MasterImpactFeed? active;
    readonly Queue<CombatHit> pending = new();
    readonly Dictionary<GameRecord, (RaceData Race, BodyPartTemplate[] Parts)> races = new();
    readonly Dictionary<CharacterId, CharacterSnapshot> characters = new();
    readonly Dictionary<(CharacterId Owner, int Part), string> robotBones = new();
    readonly Dictionary<CharacterId, RobotPunch> robotPunches = [];
    readonly Random decalRandom = new(79513);

    sealed record RobotPunch(int Part, Func<int, CutFluidContact?> Follow);
    readonly GoreEffectsSimulation scene = new(new Random(60707));
    readonly GameDatabase data;
    readonly string folder;
    StockTorsoEffects? tissues;
    readonly StockOrganicSplinters splinters;
    readonly HashSet<(CharacterId Owner, long Tick)> punches = [];
    readonly Queue<(CharacterId Owner, long Tick)> punchHistory = [];
    readonly bool particlesDisabled = Environment.GetCommandLineArgs().Contains("--no-particles", StringComparer.Ordinal);
    readonly FieldInfo sessionField, gpuField, charactersField, terrainField, optionsField, bloodField;
    readonly FieldInfo? decalStoreField;
    readonly MethodInfo? rendererRigs;
    readonly Func<object, CharacterId, long> drawKey;
    readonly Func<object, long, string, Vector3?> bonePosition;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    object? drawnHost;
    World? world;
    int renderThread;
    double effectsTime;

    internal MasterImpactFeed(ModContext context)
    {
        data = context.Data;
        folder = context.Folder;
        var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new InvalidOperationException("Brutality impact effects require the game host.");
        sessionField = RequireField(host, "Session");
        gpuField = RequireField(host, "Gpu");
        charactersField = RequireField(gpuField.FieldType, "Characters");
        splinters = new(host, gpuField.FieldType, charactersField.FieldType);
        terrainField = RequireField(gpuField.FieldType, "Terrain");
        rendererRigs = AccessTools.PropertyGetter(charactersField.FieldType, "Rigs");
        decalStoreField = AccessTools.Field(gpuField.FieldType, "DecalStore");
        optionsField = RequireField(host, "WorldOptions");
        bloodField = RequireField(optionsField.FieldType, "MeitouBlood");
        var keyMethod = AccessTools.Method(host, "DrawKey", [typeof(CharacterId)]) ?? throw new MissingMethodException(host.FullName, "DrawKey");
        var boneMethod = AccessTools.Method(charactersField.FieldType, "BonePosition", [typeof(long), typeof(string)])
            ?? throw new MissingMethodException(charactersField.FieldType.FullName, "BonePosition");
        var target = Expression.Parameter(typeof(object));
        var id = Expression.Parameter(typeof(CharacterId));
        var key = Expression.Parameter(typeof(long));
        var bone = Expression.Parameter(typeof(string));
        drawKey = Expression.Lambda<Func<object, CharacterId, long>>(
            Expression.Call(Expression.Convert(target, host), keyMethod, id), target, id).Compile();
        bonePosition = Expression.Lambda<Func<object, long, string, Vector3?>>(
            Expression.Call(Expression.Convert(target, charactersField.FieldType), boneMethod, key, bone), target, key, bone).Compile();
        var modding = RequireField(host, "Modding").FieldType;
        var update = AccessTools.Method(modding, "UpdateModHits") ?? throw new MissingMethodException(modding.FullName, "UpdateModHits");
        context.Harmony.Patch(update, postfix: new HarmonyMethod(typeof(MasterImpactFeed), nameof(UpdatePostfix)));
        var decapitation = RequireField(host, "Decapitation").FieldType;
        if (AccessTools.Method(decapitation, "Spurt", [typeof(long), typeof(CharacterAppearance), typeof(Vector3)]) is { } spurt)
            context.Harmony.Patch(spurt, prefix: new HarmonyMethod(typeof(MasterImpactFeed), nameof(NativeSpurtPrefix)));
        context.AddCombatHitFeed(Receive);
        active = this;
    }

    internal static IReadOnlyDictionary<CharacterId, SavedBowelOwner>? CaptureBowels(World world) =>
        active is { } feed && ReferenceEquals(feed.world, world) && feed.renderThread == Environment.CurrentManagedThreadId
            ? feed.tissues?.Capture(feed.effectsTime) : null;

    internal GoreEffectsSimulation? SceneForHost(object host) => ReferenceEquals(host, drawnHost) ? scene : null;

    internal static void BeginTorso(object host, StockTorsoRules.CutRequest request, CharacterAppearance look, SkeletonRig rig, Vector3 position, double time)
    {
        if (active is not { } feed || feed.SceneForHost(host) is not { } scene) return;
        bool mechanical = request.Robot && look.Race.GetBool("is robot");
        (feed.tissues ??= new(feed.folder)).Begin(request with { Robot = mechanical }, look, rig, time);
        if (request.Restoring) return;
        var cut = new GoreEffectCut(request.Victim, request.Part, 2, -1, request.Tick, request.Travel, request.Force,
            request.BloodColour, request.Seed, mechanical, position)
        { ExplosionSeed = (uint)RobotVisualRandom.Mix(request.Seed ^ 0x544F52534FUL) };
        StockRobotDebris.BeginTorso(host, cut, time);
        if (scene.Begin(cut, time) && feed.trace)
            Console.WriteLine($"Brutality stock torso rupture robot={mechanical} at={position}");
    }

    internal static bool AcceptsWound(object host, CharacterId owner) => active is { } feed
        && feed.SceneForHost(host) is not null && feed.tissues?.HasSpilled(owner) != true
        && (feed.world is null || StockBowelSave.Pending(feed.world, owner) is null);

    internal static void BeginWound(object host, CharacterId owner, CharacterAppearance look, SkeletonRig rig,
        CutFluidContact contact, double now)
    {
        if (active is not { } feed || feed.SceneForHost(host) is null) return;
        (feed.tissues ??= new(feed.folder)).BeginWound(owner, look, rig, contact, now);
    }

    internal static bool AcceptsPunch(object host, CharacterId owner, long born, long shownTick, CharacterAppearance look) => active is { } feed
        && feed.SceneForHost(host) is not null && feed.world is { } world && !feed.particlesDisabled
        && (look.Race.GetBool("is robot") || feed.OrganicEffectsEnabled(host))
        && shownTick >= born && (shownTick - born) * world.TickSeconds <= 3 && !feed.punches.Contains((owner, born));

    internal static void BeginPunch(object host, CharacterId owner, long born, CharacterAppearance look, int region,
        CutFluidContact contact, Func<int, CutFluidContact?> follow, double time)
    {
        if (active is not { } feed || feed.SceneForHost(host) is not { } scene || feed.world is not { } world
            || !feed.punches.Add((owner, born))) return;
        feed.punchHistory.Enqueue((owner, born));
        if (feed.punchHistory.Count > 128) feed.punches.Remove(feed.punchHistory.Dequeue());
        uint blood = unchecked((uint)look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        var at = contact.Position;
        ulong seed = Rng.Mix(world.Seed ^ Rng.Key(owner) ^ (ulong)born);
        if (look.Race.GetBool("is robot"))
        {
            var (_, parts) = feed.Race(look.Race);
            int part = Array.FindIndex(parts, template => ChestHolePolicy.RegionOf(template.StringId) == region);
            if (part < 0) return;
            var cut = new GoreEffectCut(owner, part, GoreEffectCut.MechanicalPunch, -1, born,
                new(contact.Normal.X, contact.Normal.Z), 70, blood, seed, true, at)
                { ExplosionSeed = (uint)RobotVisualRandom.Mix(seed ^ 0x50554E4348UL) };
            if (scene.Begin(cut, time)) feed.robotPunches[owner] = new(part, follow);
            if (feed.trace) Console.WriteLine($"Brutality stock robot punch-through short circuit owner={owner} at={at}");
            return;
        }
        scene.Blood.SplashExit(contact, world.Walkability.GroundHeight(at.X, at.Z, at.Y), blood);
        feed.splinters.Queue(contact, blood, seed, exit: true, time);
        if (feed.trace) Console.WriteLine($"Brutality stock punch-through exit spray owner={owner} at={at}");
    }

    internal static void BeginOrganicLimb(object host, StockLimbRules.Request request, uint blood,
        Func<int, CutFluidContact?> follow, double time)
    {
        if (active is not { } feed || feed.SceneForHost(host) is not { } scene || !feed.OrganicEffectsEnabled(host)) return;
        if (scene.Blood.BeginLimb(request.Owner, request.Shape.LimbSlot, time, request.Travel, blood, follow) && feed.trace)
            Console.WriteLine($"Brutality stock organic limb stream owner={request.Owner} slot={request.Shape.LimbSlot}");
    }

    bool OrganicEffectsEnabled(object host) => !particlesDisabled
        && optionsField.GetValue(host) is { } options && bloodField.GetValue(options) is true;

    internal static void EmitHeadBurst(object host, Vector3 at, Vector2 travel, uint blood)
    {
        if (active is not { } feed || feed.SceneForHost(host) is not { } scene
            || feed.world is not { } world || !feed.OrganicEffectsEnabled(host)) return;
        scene.Blood.SplashHead(at, travel, world.Walkability.GroundHeight(at.X, at.Z, at.Y), blood);
        if (feed.trace) Console.WriteLine($"Brutality stock head burst spray at release: {at}");
    }

    internal static void BeginMechanical(object host, in GoreEffectCut cut, double time)
    {
        active?.SceneForHost(host)?.Begin(cut, time);
    }

    void Receive(IReadOnlyList<CombatHit> hits)
    {
        // The host reuses its list and can invoke this callback with a simulation tick in flight.
        foreach (var hit in hits)
        {
            if (pending.Count == PendingLimit) pending.Dequeue();
            pending.Enqueue(hit);
        }
    }

    static bool NativeSpurtPrefix(object __instance, long from)
    {
        if (active is not { } feed || StockHostParts.HostOf(__instance) is not { } host
            || feed.sessionField.GetValue(host) is not WorldSession shown) return true;
        foreach (var actor in shown.CurrentSnapshot.Characters)
            if (feed.drawKey(host, actor.Id) == from)
                return !StockHeadRules.TryGet(shown.World, actor.Id, shown.ShownTick, out var cut) || cut.Shape.Mode != HeadCutMode.Burst;
        return true;
    }

    static void UpdatePostfix(object __instance)
    {
        if (StockHostParts.HostOf(__instance) is { } host) active?.Update(host);
    }

    void Update(object host)
    {
        if (sessionField.GetValue(host) is not WorldSession session || gpuField.GetValue(host) is not { } gpu
            || charactersField.GetValue(gpu) is not { } renderer || terrainField.GetValue(gpu) is not TerrainRenderer terrain) return;
        drawnHost = host;
        renderThread = Environment.CurrentManagedThreadId;
        if (!ReferenceEquals(world, session.World))
        {
            // UpdateModHits attaches a fresh queue on the first frame of a new world and delivers no batch that frame.
            world = session.World;
            pending.Clear(); scene.Clear(); robotBones.Clear(); robotPunches.Clear();
            splinters.Clear(); punches.Clear(); punchHistory.Clear();
            tissues?.Dispose();
            tissues = null;
        }

        double now = effectsTime = (session.ShownTick + session.SimulationAlpha) * world.TickSeconds;
        if (StockBowelSave.HasPending(world) && rendererRigs?.Invoke(renderer, null) is CharacterRigs setup)
            (tissues ??= new(folder)).Restore(host, session, setup, now);
        float Ground(Vector3 position) => terrain.HeightAt(position.X, position.Z);
        bool bloodEnabled = optionsField.GetValue(host) is { } options && bloodField.GetValue(options) is true;
        if (!bloodEnabled) { scene.Blood.Clear(); splinters.Clear(); }
        else if (!particlesDisabled) splinters.Advance(host, gpu, renderer, now, Ground);
        if (pending.Count == 0 && scene.Blood.Drops.Count == 0 && scene.Blood.SourceCount == 0 && scene.Blood.Streams.Count == 0 && scene.RobotSources.Count == 0 && scene.ExplosionCount == 0
            && scene.Anchors.Count == 0 && scene.Explosions.Count == 0 && tissues?.Active != true) return;

        bool resolvingHit = pending.TryPeek(out var next) && next.Tick < session.ShownTick;
        if (resolvingHit || scene.RobotSources.Count != 0 || scene.ExplosionCount != 0)
            foreach (var character in session.CurrentSnapshot.Characters) characters[character.Id] = character;
        scene.Advance(now, (owner, half) => StockTorsoFragments.Contact(host, owner, half), RobotContact,
            _ => true, Ground, ValidDischarge, fallbackSeconds: .40f);

        CutFluidContact? RobotContact(GoreEffectCut cut, int half) => cut.Kind == 2 ? StockTorsoFragments.Contact(host, cut.Owner, half)
            : cut.Kind is 0 or 1 ? StockRobotDebris.Contact(host, cut, half)
            : cut.Kind == GoreEffectCut.MechanicalPunch ? PunchContact(cut.Owner, half)
            : half == 0 && robotBones.TryGetValue((cut.Owner, cut.Part), out string? bone)
            && TryBone(host, renderer, cut.Owner, bone) is { } point ? new CutFluidContact(point, Vector3.UnitY) : null;

        bool ValidDischarge(GoreEffectCut cut)
        {
            if (characters.TryGetValue(cut.Owner, out var victim) && victim.Appearance is { } look)
            {
                var (race, anatomy) = Race(look.Race);
                return (uint)cut.Part < (uint)anatomy.Length
                    && MechanicalPart(victim, anatomy[cut.Part], race.IsRobot, cut.Kind, cut.StableKey);
            }
            // The original owner may leave the snapshot while a tracked piece still exists.
            return cut.Kind is 0 or 1 or 2 && RobotContact(cut, 1) is not null;
        }
        if (bloodEnabled && !particlesDisabled && scene.Blood.Impacts.Count > 0
            && decalStoreField?.GetValue(gpu) is TerrainDecalStore store)
            foreach (var impact in scene.Blood.Impacts)
            {
                uint abgr = 0xDF000000 | Byte(impact.Colour.X) | Byte(impact.Colour.Y) << 8 | Byte(impact.Colour.Z) << 16;
                store.Add(TerrainDecalType.Splash, impact.At, new(impact.Velocity.X, impact.Velocity.Z),
                    Math.Clamp(impact.Width * .38f, .025f, .09f), abgr, TerrainDecalStore.NewLife, decalRandom);
            }
        tissues?.Advance(host, session, now, scene);

        // CombatHit.Tick is the resolving tick, published by the host in the following simulation snapshot.
        while (pending.TryPeek(out var hit) && hit.Tick < session.ShownTick)
        {
            pending.Dequeue();
            if (now - hit.Tick * world.TickSeconds > MaxHitAge || !hit.VictimWasAlive || hit.Part < 0
                || !characters.TryGetValue(hit.Victim, out var victim) || victim.Appearance is not { } look) continue;
            float edged = hit.Damage.Cut + hit.Damage.Pierce;
            if (!float.IsFinite(edged) || edged <= 0) continue;
            var (victimRace, parts) = Race(look.Race);
            if (hit.Part >= parts.Length) continue;
            var part = parts[hit.Part];
            string bone = hit.LowStrike && part.BoneName2.Length != 0 ? part.BoneName2 : part.BoneName;
            Vector3 position = TryBone(host, renderer, hit.Victim, bone)
                ?? hit.VictimPosition + Vector3.UnitY * FallbackHeight(part.Type, victimRace.HullY * victim.Scale);
            var travel = new Vector2(hit.Travel.X, hit.Travel.Z);
            bool prosthetic = Prosthetic(victim, part);
            if (victimRace.IsRobot || prosthetic)
            {
                if (hit.Source is CombatHitSource.Projectile || hit.Damage.Cut <= 0
                    || StockRobotDebris.HasRupture(host, hit.Victim, hit.Part)
                    || robotPunches.TryGetValue(hit.Victim, out var punch) && punch.Part == hit.Part) continue;
                ulong seed = RobotVisualRandom.DischargeSeed(hit, world.Seed);
                var cut = new GoreEffectCut(hit.Victim, hit.Part, 3, -1, hit.Tick, travel, hit.Damage.Cut,
                    victimRace.BloodColour, seed, true, position) { Rupture = false };
                if (scene.Begin(cut, hit.Tick * world.TickSeconds)) robotBones[(hit.Victim, hit.Part)] = bone;
                if (trace) Console.WriteLine($"Brutality stock hit tick={hit.Tick} source={hit.Source} part={hit.Part} effect=short-circuit race={victimRace.StringId} prosthetic={prosthetic} at={position}");
            }
            else if (bloodEnabled && victimRace.BleedRate > 0)
            {
                if (part.Type == BodyPartType.Head && StockHeadRules.TryGet(world, hit.Victim, session.ShownTick, out var head)
                    && head.Tick == hit.Tick && head.Shape.Mode == HeadCutMode.Burst) continue;
                scene.Blood.SplashImpact(position, travel, Ground(position), victimRace.BloodColour, edged);
                if (!particlesDisabled && edged >= 15)
                    splinters.Queue(new(position, new(travel.X, 0, travel.Y)), victimRace.BloodColour,
                        RobotVisualRandom.DischargeSeed(hit, world.Seed), exit: false, now);
                if (trace) Console.WriteLine($"Brutality stock hit tick={hit.Tick} source={hit.Source} part={hit.Part} effect=liquid-blood at={position}");
            }
        }
        characters.Clear();
        if (robotPunches.Count != 0)
        {
            var retainedPunches = scene.RobotSources.Where(source => source.Cut.Kind == GoreEffectCut.MechanicalPunch)
                .Select(source => source.Cut.Owner).ToHashSet();
            foreach (var owner in robotPunches.Keys.Where(owner => !retainedPunches.Contains(owner)).ToArray()) robotPunches.Remove(owner);
        }

        if (robotBones.Count > scene.RobotSources.Count)
        {
            var retained = scene.RobotSources.Select(source => (source.Cut.Owner, source.Cut.Part)).ToHashSet();
            foreach (var key in robotBones.Keys.Where(key => !retained.Contains(key)).ToArray()) robotBones.Remove(key);
        }
    }

    static bool MechanicalPart(CharacterSnapshot victim, BodyPartTemplate part, bool robot, int kind, int key) => kind switch
    {
        0 => robot && part.LimbSlot is >= 0 and < 4 && key == part.LimbSlot + 10,
        1 => robot && part.Type == BodyPartType.Head,
        2 or GoreEffectCut.MechanicalPunch => robot && ChestHolePolicy.RegionOf(part.StringId) >= 0,
        3 => robot || Prosthetic(victim, part),
        _ => false,
    };

    static bool Prosthetic(CharacterSnapshot victim, BodyPartTemplate part) => part.LimbSlot is >= 0 and < 4
        && (LimbState)((victim.LimbBits >> (2 * part.LimbSlot)) & 3) == LimbState.Replaced;

    CutFluidContact? PunchContact(CharacterId owner, int half) => robotPunches.TryGetValue(owner, out var punch) ? punch.Follow(half) : null;

    (RaceData Race, BodyPartTemplate[] Parts) Race(GameRecord record)
    {
        if (races.TryGetValue(record, out var existing)) return existing;
        var race = RaceData.From(record, data);
        var resolved = (race, race.Anatomy.Select(entry => entry.Part).OrderBy(part => part.MedicalOrder).ToArray());
        races.Add(record, resolved);
        return resolved;
    }

    static uint Byte(float value) => (uint)(Math.Clamp(value, 0, 1) * 255);

    Vector3? TryBone(object host, object renderer, CharacterId id, string bone) => bone.Length == 0
        ? null : bonePosition(renderer, drawKey(host, id), bone);

    static float FallbackHeight(BodyPartType type, float height) => height * (type switch
    {
        BodyPartType.Head => .92f,
        BodyPartType.Leg => .33f,
        BodyPartType.Arm => .66f,
        _ => .72f,
    });

    static FieldInfo RequireField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
}
