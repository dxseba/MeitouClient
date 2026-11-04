using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

/// <summary>Version-pinned presentation adapter. GPU allocation starts only in the live renderer's Prepare callback.</summary>
internal static partial class EffectsBridge
{
    sealed class FxWorld
    {
        public readonly GoreEffectsSimulation Scene = new(new Random());
        public readonly RobotScrapSimulation Scrap = new();
        public readonly RobotCableSimulation Cables = new();
        public readonly List<RobotCableContact> CableContacts = [];
        public BowelPresentation? Bowels;
        public Meitou.Simulation.Ragdolls.IRagdollEnvironment? BowelGround;
        public readonly Dictionary<long, GoreCutRequest> Pending = [];
        public readonly Dictionary<(CharacterId, int), (GoreEffectCut Cut, double Born)> BowelCuts = [];
        public readonly Dictionary<(CharacterId, int), GoreEffectCut> CaptureCuts = [];
        public RobotScrapMesh[]? ScrapMeshes;
        public readonly CutAnchorGeometry Samples = new();
        public readonly Dictionary<(CharacterId, int), (GoreEffectCut Cut, double Born)> Recent = [];
        public readonly Dictionary<(CharacterId, int, int), CutFluidContact> Contacts = [];
        public readonly Dictionary<long, CharacterInstance> Drawn = [];
        public readonly Dictionary<long, Matrix4x4[]> Skin = [];
        public readonly HashSet<long> Posed = [];
        public readonly List<ActiveLayer> Layers = [];
        public long Sequence;
        public readonly OrdinaryImpactState Impacts = new();
        public bool BowelTraceReported;
    }
    sealed class Binding
    {
        public FxWorld? World;
        public GoreEffectRenderer? Renderer;
        public bool OpaqueOnly;
    }
    static readonly ConditionalWeakTable<World, FxWorld> worlds = new();
    static readonly ConditionalWeakTable<ParticleRenderer, Binding> bindings = new();
    static RenderResources? resources;
    static string folder = "";
    static FieldInfo? hostGpu, particlesField;
    static readonly FieldInfo particleGpu = AccessTools.Field(typeof(ParticleRenderer), "gpu");
    static Func<object, CharacterId, long>? drawKey;

    public static void Install(ModContext context, string modFolder, RenderResources source)
    {
        resources = source; folder = modFolder;
        RenderBridge.AfterFill = Capture;
        context.AddSystem("organ-labels", _ => new BowelLabelSystem(), SystemAnchor.AfterPart(StandardParts.Animation));
        var type = typeof(ParticleRenderer);
        void Patch(string name, string callback) => context.Harmony.Patch(AccessTools.Method(type, name), postfix: new(AccessTools.Method(typeof(EffectsBridge), callback)));
        Patch(nameof(ParticleRenderer.Prepare), nameof(PreparePostfix));
        Patch(nameof(ParticleRenderer.Record), nameof(RecordPostfix));
        Patch(nameof(ParticleRenderer.CountFor), nameof(CountPostfix));
        Patch(nameof(ParticleRenderer.DrawCoverage), nameof(CoveragePostfix));
        Patch(nameof(ParticleRenderer.Dispose), nameof(DisposePostfix));
        context.Harmony.Patch(AccessTools.PropertyGetter(type, nameof(ParticleRenderer.FullCount)), postfix: new(AccessTools.Method(typeof(EffectsBridge), nameof(FullCountPostfix))));
        context.Harmony.Patch(AccessTools.PropertyGetter(type, nameof(ParticleRenderer.HasCoverage)), postfix: new(AccessTools.Method(typeof(EffectsBridge), nameof(HasCoveragePostfix))));
        var host = AccessTools.TypeByName("Meitou.Game.GameHost");
        if (host is null) return;
        hostGpu = AccessTools.Field(host, "gpu") ?? throw new MissingFieldException(host.FullName, "gpu");
        particlesField = AccessTools.Field(hostGpu.FieldType, "Particles") ?? throw new MissingFieldException(hostGpu.FieldType.FullName, "Particles");
        var keyMethod = AccessTools.Method(host, "DrawKey") ?? throw new MissingMethodException(host.FullName, "DrawKey");
        var target = Expression.Parameter(typeof(object)); var id = Expression.Parameter(typeof(CharacterId));
        drawKey = Expression.Lambda<Func<object, CharacterId, long>>(Expression.Call(Expression.Convert(target, host), keyMethod, id), target, id).Compile();
        context.Harmony.Patch(AccessTools.Method(host, "UpdateDebris"), postfix: new(AccessTools.Method(typeof(EffectsBridge), nameof(StyleOrganicDebris))));
        context.Harmony.Patch(AccessTools.Method(host, "SetUpBlood"), postfix: new(AccessTools.Method(typeof(EffectsBridge), nameof(BloodSetupPostfix))));
    }

    static void BloodSetupPostfix(object __instance, IGpuContext context)
    {
        if (resources is null || hostGpu?.GetValue(__instance) is not { } gpu || particlesField is null) return;
        if (particlesField.GetValue(gpu) is null)
        {
            // The host still calls Prepare/Record for this empty container. Disabled native sprites stay disabled; opaque tissue reuses its pass.
            var renderer = ParticleRenderer.Create(context, resources.Assets);
            renderer.Enabled = Enabled(__instance);
            particlesField.SetValue(gpu, renderer);
        }
    }

    /// <summary>Called by the mod's render bridge after the full draw list has been assembled, including independent fragments.</summary>
    public static void Capture(object host, CharacterDrawList list, WorldSession session)
    {
        if (resources is null || drawKey is null || hostGpu?.GetValue(host) is not { } gpu || particlesField is null) return;
        var particles = particlesField.GetValue(gpu) as ParticleRenderer;
        if (particles is null && list.Context is { } context)
        {
            particles = ParticleRenderer.Create(context, resources.Assets);
            particles.Enabled = Enabled(host);
            particlesField.SetValue(gpu, particles);
        }
        if (particles is null) return;
        var world = session.World;
        var effects = worlds.GetValue(world, static _ => new());
        var binding = bindings.GetValue(particles, static _ => new());
        binding.World = effects;
        bool particlesEnabled = Enabled(host);
        binding.OpaqueOnly = !particlesEnabled;
        if (!particlesEnabled)
        {
            effects.Scene.Clear(); effects.Scrap.Clear(); effects.Recent.Clear();
        }
        double now = (session.ShownTick + session.SimulationAlpha) * world.TickSeconds;
        var gameplay = BrutalityRuntime.For(world);
        if (gameplay is null) return;
        foreach (var request in gameplay.CutRequests)
        {
            if (request.Sequence <= effects.Sequence) continue;
            if (request.Tick > session.ShownTick) break;
            effects.Sequence = request.Sequence;
            if (request.Kind == GoreCutKind.PairedLimbAttempt) continue;
            if (effects.Pending.Count >= 128) effects.Pending.Remove(effects.Pending.Keys.Min());
            effects.Pending[request.Sequence] = request;
        }
        foreach (var (sequence, request) in effects.Pending.ToArray())
        {
            double born = request.Tick * world.TickSeconds;
            if (now - born > BowelPresentation.Lifetime) { effects.Pending.Remove(sequence); continue; }
            var cold = world.Characters.Cold(request.Victim.Slot);
            if (cold?.Race is not { } race) continue;
            if (!particlesEnabled && (race.IsRobot || request.Kind != GoreCutKind.Torso)) { effects.Pending.Remove(sequence); continue; }
            double lifetime = !race.IsRobot && request.Kind == GoreCutKind.Torso ? BowelPresentation.Lifetime : race.IsRobot ? 30 : LiquidBloodSimulation.FlowSeconds;
            if (now - born > lifetime) { effects.Pending.Remove(sequence); continue; }
            long key = drawKey(host, request.Victim);
            if (!list.Items.Any(instance => instance.Key == key && instance.Geometry is not null)) continue;
            var hit = request.Hit;
            int kind = request.Kind == GoreCutKind.Torso ? 2 : request.Kind is GoreCutKind.Head or GoreCutKind.SevereHead ? 1 : 0;
            int stable = kind == 2 ? 1 : kind == 1 ? 2 : 10 + request.LimbSlot;
            int part = hit?.Part ?? (kind == 2 ? 5 : kind == 1 ? 0 : 10 + request.LimbSlot);
            var travel = hit is { } h ? new Vector2(h.Travel.X, h.Travel.Z) : Vector2.UnitX;
            ulong explosionSeed = Rng.Mix(world.Seed ^ Rng.Key(request.Victim) ^ (ulong)request.Tick ^ ((ulong)(uint)part << 32));
            var cut = new GoreEffectCut(request.Victim, part, kind, stable, request.Tick, travel,
                hit?.Damage.Cut ?? request.Bridge?.PreArmourCut ?? 70, race.BloodColour, VisualSeed(request, world.Seed), race.IsRobot,
                hit?.VictimPosition + new Vector3(0, kind == 2 ? 12 : kind == 1 ? 18 : 10, 0) ?? Vector3.Zero)
                { ExplosionSeed = (uint)(explosionSeed ^ explosionSeed >> 32) };
            if (!cut.Robot && cut.Kind == 2)
            {
                if (effects.BowelCuts.Count >= 64) effects.BowelCuts.Remove(effects.BowelCuts.MinBy(p => p.Value.Born).Key);
                effects.BowelCuts[(cut.Owner, cut.Part)] = (cut, born);
            }
            if (particlesEnabled && now - born <= (cut.Robot ? 30 : LiquidBloodSimulation.FlowSeconds))
            {
                effects.Scene.Begin(cut, born);
                if (!effects.Recent.ContainsKey((cut.Owner, cut.Part)) && effects.Recent.Count >= 128)
                    effects.Recent.Remove(effects.Recent.MinBy(p => p.Value.Born).Key);
                effects.Recent[(cut.Owner, cut.Part)] = (cut, born);
            }
            effects.Pending.Remove(sequence);
        }
        foreach (var key in effects.Recent.Where(p => now - p.Value.Born > (p.Value.Cut.Robot ? 30 : LiquidBloodSimulation.FlowSeconds))
            .Select(p => p.Key).ToArray()) effects.Recent.Remove(key);
        effects.Drawn.Clear(); effects.Posed.Clear(); effects.Contacts.Clear();
        foreach (var instance in list.Items) effects.Drawn[instance.Key] = instance;
        var frame = world.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault()?.FrameAt(session.ShownTick);
        // The pin reconstructs bowel props from visible saved torso state, with age three when no live impact birth exists.
        // This is organ-only discovery: saved injuries never restart liquid blood, robot discharge or explosions.
        if (frame is not null)
            foreach (var actor in frame.Characters.Values)
            {
                var id = actor.Character;
                if (actor.State.Torso is not { IsValid: true } || actor.Appearance.Race.GetBool("is robot")
                    || effects.Bowels?.HasSpilled(id) == true || effects.BowelCuts.Keys.Any(key => key.Item1 == id)
                    || !effects.Drawn.TryGetValue(drawKey(host, id), out var instance) || instance.Geometry is null) continue;
                uint blood = unchecked((uint)actor.Appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
                var restored = new GoreEffectCut(id, 5, 2, 1, session.ShownTick,
                    new(MathF.Cos(instance.Yaw), -MathF.Sin(instance.Yaw)), 0, blood, 0, false, instance.Position);
                if (effects.BowelCuts.Count >= 64) effects.BowelCuts.Remove(effects.BowelCuts.MinBy(p => p.Value.Born).Key);
                effects.BowelCuts[(id, 5)] = (restored, now - 3);
            }
        effects.CaptureCuts.Clear();
        foreach (var (key, value) in effects.Recent) effects.CaptureCuts[key] = value.Cut;
        foreach (var (key, value) in effects.BowelCuts) effects.CaptureCuts[key] = value.Cut;
        foreach (var cut in effects.CaptureCuts.Values)
        {
            if (frame?.Characters.GetValueOrDefault(cut.Owner) is not { } actor || resources.Rigs.For(actor.Appearance) is not { } setup
                || resources.Mesh(actor.Appearance.BodyMesh) is not { } body) continue;
            if (!resources.Geometry.TryCached(actor.Appearance, setup.Rig, body, actor.State, out var geometry)) continue;
            var detached = geometry.Detached.FirstOrDefault(p => p.StableKey == cut.StableKey);
            Vector4 plane = detached?.CutPlane ?? default;
            int retainedSide = cut.Kind == 1 ? -1 : 1;
            CapturePiece(0, geometry.Retained, retainedSide, drawKey(host, cut.Owner));
            if (detached is not null && RenderBridge.FragmentDrawKey(world, cut.Owner, cut.StableKey) is { } fragmentKey)
                CapturePiece(1, detached.Geometry, -retainedSide, fragmentKey);

            void CapturePiece(int half, RuntimeCharacterGeometry shape, int side, long key)
            {
                if (!effects.Drawn.TryGetValue(key, out var instance)) return;
                var samples = effects.Samples.Samples(shape, plane, side, setup.Rig, actor.Appearance);
                var skin = Skin(effects, instance, setup);
                var matrix = instance.World ?? (instance.Rotation is { } q ? CharacterRigs.Placement(setup, instance.Position, q, instance.Scale)
                    : CharacterRigs.Placement(setup, instance.Position, instance.Yaw, instance.Scale));
                if (CutAnchorGeometry.Pose(samples, skin, matrix) is { } contact) effects.Contacts[(cut.Owner, cut.Part, half)] = contact;
            }
        }
        effects.CableContacts.Clear();
        if (particlesEnabled && frame is not null)
            foreach (var actor in frame.Characters.Values)
            {
                if (!actor.Appearance.Race.GetBool("is robot") || resources.Rigs.For(actor.Appearance) is not { } setup
                    || resources.Mesh(actor.Appearance.BodyMesh) is not { } body
                    || !resources.Geometry.TryCached(actor.Appearance, setup.Rig, body, actor.State, out var geometry)) continue;
                foreach (var fragment in geometry.Detached)
                {
                    int side = fragment.Kind is GeometryFragmentKind.HeadPositive ? -1 : 1;
                    if (fragment.Kind == GeometryFragmentKind.TorsoNegative) AddCable(geometry.Retained, side, drawKey(host, actor.Character));
                    if (RenderBridge.FragmentDrawKey(world, actor.Character, fragment.StableKey) is { } key) AddCable(fragment.Geometry, -side, key);

                    void AddCable(RuntimeCharacterGeometry shape, int keepSide, long key)
                    {
                        if (!effects.Cables.Accepts(key, effects.CableContacts.Count) || !effects.Drawn.TryGetValue(key, out var instance)) return;
                        var samples = effects.Samples.Samples(shape, fragment.CutPlane, keepSide, setup.Rig, actor.Appearance);
                        var matrix = instance.World ?? (instance.Rotation is { } q ? CharacterRigs.Placement(setup, instance.Position, q, instance.Scale)
                            : CharacterRigs.Placement(setup, instance.Position, instance.Yaw, instance.Scale));
                        if (CutAnchorGeometry.Pose(samples, Skin(effects, instance, setup), matrix) is { } contact)
                            effects.CableContacts.Add(new(key, contact));
                    }
                }
            }
        effects.Cables.Advance(now, effects.CableContacts, at => world.Walkability.GroundHeight(at.X, at.Z, at.Y), particlesEnabled);
        effects.Scene.Cables = effects.Cables.Rendered;
        CaptureImpacts(host, session, particles, effects, now, particlesEnabled);
        effects.Scene.Advance(now, (owner, half) =>
        {
            foreach (var entry in effects.Recent.Values)
                if (entry.Cut.Owner == owner && entry.Cut.Kind == 2)
                    return effects.Contacts.TryGetValue((owner, entry.Cut.Part, half), out var contact) ? contact : null;
            return null;
        }, (cut, half) => effects.Contacts.TryGetValue((cut.Owner, cut.Part, half), out var point) ? point : null,
            world.Characters.IsAlive, at => world.Walkability.GroundHeight(at.X, at.Z, at.Y));
        if (!ReadBool(Member(host, "o"), "MeitouBlood", true)) effects.Scene.Blood.Clear();
        foreach (var key in effects.BowelCuts.Where(p => now - p.Value.Born >= BowelPresentation.Lifetime || !world.Characters.IsAlive(p.Key.Item1)).Select(p => p.Key).ToArray())
            effects.BowelCuts.Remove(key);
        // Ordinary wounds do not allocate a fragment-physics owner. Pose the projected wound samples with the exact live draw-list skin instead.
        if (gameplay.WoundFrameAt(session.ShownTick) is { } wounds)
            foreach (var state in wounds.Characters.Values)
            {
                if (!HasDeepAbdomen(state) || effects.Bowels?.HasSpilled(state.Character) == true
                    || !world.Characters.IsAlive(state.Character)
                    || !effects.Drawn.TryGetValue(drawKey(host, state.Character), out var instance)
                    || instance.Appearance.Race.Name is not ("Greenlander" or "Scorchlander")
                    || resources.Rigs.For(instance.Appearance) is not { } setup || resources.Mesh(instance.Appearance.BodyMesh) is not { } body) continue;
                if (!resources.Geometry.TryCached(instance.Appearance, setup.Rig, body, state, out var geometry)
                    || geometry.AbdomenAnchor.Count == 0) continue;
                var matrix = instance.World ?? (instance.Rotation is { } q ? CharacterRigs.Placement(setup, instance.Position, q, instance.Scale)
                    : CharacterRigs.Placement(setup, instance.Position, instance.Yaw, instance.Scale));
                if (CutAnchorGeometry.Pose(geometry.AbdomenAnchor.ToArray(), Skin(effects, instance, setup), matrix) is not { } contact) continue;
                effects.Bowels ??= new(resources.Geometry.Anatomy);
                effects.BowelGround ??= new Meitou.Simulation.Ragdolls.WalkabilityEnvironment(world.Walkability);
                uint blood = unchecked((uint)instance.Appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
                // Like pinned SpawnWoundGuts, an ordinary saved deep wound creates its loose prop on first visibility.
                effects.Bowels.BeginWound(state.Character, instance.Appearance, setup.Rig, now, now, contact, blood, effects.BowelGround);
            }
        if (effects.BowelCuts.Count > 0 || effects.Bowels is not null)
        {
            effects.Bowels ??= new(resources.Geometry.Anatomy);
            effects.BowelGround ??= new Meitou.Simulation.Ragdolls.WalkabilityEnvironment(world.Walkability);
            foreach (var (cut, born) in effects.BowelCuts.Values)
            {
                if (frame?.Characters.GetValueOrDefault(cut.Owner) is not { } actor || resources.Rigs.For(actor.Appearance) is not { } setup) continue;
                effects.Bowels.Begin(cut.Owner, actor.Appearance, setup.Rig, born, now,
                    half => effects.Contacts.TryGetValue((cut.Owner, cut.Part, half), out var contact) ? contact : null,
                    cut.Travel, cut.BloodColour, effects.BowelGround);
            }
            effects.Bowels.Advance(now, (owner, half) =>
            {
                foreach (var (cut, _) in effects.BowelCuts.Values)
                    if (cut.Owner == owner) return effects.Contacts.TryGetValue((owner, cut.Part, half), out var contact) ? contact : null;
                return null;
            }, world.Characters.IsAlive, effects.BowelGround, session.ShownTick, world.PhysicsSpeed);
            foreach (var key in effects.BowelCuts.Keys.Where(key => effects.Bowels.HasSpilled(key.Item1) && !effects.Bowels.HasOwner(key.Item1)).ToArray()) effects.BowelCuts.Remove(key);
            effects.Scene.Bowels = BowelDiagnostics.Hide ? [] : effects.Bowels.Rendered;
            if (BowelDiagnostics.Trace && !effects.BowelTraceReported && effects.Bowels.Rendered.Count > 0)
            {
                Console.WriteLine($"Brutality bowel frame: now={now:F3}, bodies={effects.Bowels.Count}, attached={effects.Bowels.Attached}, bloodSources={effects.Scene.Blood.SourceCount}, streams={effects.Scene.Blood.Streams.Count}, drops={effects.Scene.Blood.Drops.Count}, hidden={BowelDiagnostics.Hide}");
                effects.BowelTraceReported = true;
            }
            world.Systems.OfType<BowelLabelSystem>().FirstOrDefault()?.Publish(session.CurrentSnapshot.Tick, effects.Bowels.Labels);
        }
        if (AccessTools.Field(gpu.GetType(), "Debris")?.GetValue(gpu) is DebrisRenderer debris
            && AccessTools.Field(gpu.GetType(), "DebrisPieces")?.GetValue(gpu) is List<DebrisPiece> drawnScrap)
        {
            effects.Scrap.Advance(now, BodySystem.CorpseTicksOf(world) * world.TickSeconds);
            foreach (var (cut, born) in effects.Recent.Values)
            {
                if (!cut.Robot || !cut.Rupture || now - born > 2) continue;
                var contact = effects.Contacts.TryGetValue((cut.Owner, cut.Part, 0), out var at) ? at.Position
                    : now - born > .15 ? cut.Fallback : (Vector3?)null;
                if (contact is not { } point) continue;
                effects.ScrapMeshes ??= ScrapMeshes(debris);
                effects.Scrap.Begin(cut, point, world.Walkability.GroundHeight(point.X, point.Z, point.Y), now, world.Seed, effects.ScrapMeshes);
            }
            drawnScrap.AddRange(effects.Scrap.Drawn);
        }
        // Native decals remain the host's; this module adds only bounded impacts through its existing store.
        if (AccessTools.Field(gpu.GetType(), "DecalStore")?.GetValue(gpu) is Meitou.Data.World.TerrainDecalStore store)
            foreach (var impact in effects.Scene.Blood.Impacts)
            {
                uint rgb = (uint)(impact.Colour.X * 255) << 16 | (uint)(impact.Colour.Y * 255) << 8 | (uint)(impact.Colour.Z * 255);
                uint abgr = (rgb >> 16 & 255) | (rgb & 0xFF00) | (rgb & 255) << 16;
                store.Add(Meitou.Data.World.TerrainDecalType.Splash, impact.At, new(impact.Velocity.X, impact.Velocity.Z),
                    Math.Clamp(impact.Width * .38f, .025f, .09f), 0xDF000000 | abgr, Meitou.Data.World.TerrainDecalStore.NewLife, Random.Shared);
            }
        if (effects.Skin.Count > 512)
            foreach (long key in effects.Skin.Keys.Where(key => !effects.Posed.Contains(key)).ToArray()) effects.Skin.Remove(key);
    }

    internal static bool HasDeepAbdomen(BrutalityActorView state) => state.Torso is null && state.Wounds.BellyHits >= 10
        && state.Wounds.Entries.Any(entry => (entry.Region == 1 || entry.Region < 0 && entry.Mark.Segment == 9 && entry.Mark.T <= .54f)
            && entry.Mark.Shape.Kind is WoundKind.Slash or WoundKind.Thrust);

    static RobotScrapMesh[] ScrapMeshes(DebrisRenderer debris)
    {
        if (resources is null) return [];
        var result = new List<RobotScrapMesh>();
        var resolver = new MaterialResolver(resources.Rigs.Database, resources.Assets.Materials, resources.Assets);
        foreach (string id in new[] { "42159-gamedata.base", "43397-changes_otto.mod", "43396-changes_otto.mod" })
        {
            if (resources.Rigs.Database.Find(id) is not { } item) continue;
            string context = item.GetPath("mesh");
            string name = item.GetPath("ground mesh") is { Length: > 0 } ground ? ground : context;
            if (resources.Assets.Find(name) is not { } path || resources.Mesh(name) is not { } mesh || mesh.SubMeshes.Count == 0) continue;
            if (context.Length == 0) context = path;
            // Loading under the item's own material context avoids the untextured white ground-mesh fallback.
            if (!resolver.Candidates(context, mesh.SubMeshes[0]).Any(m => m.Diffuse is { } diffuse && resources.Assets.Find(diffuse) is not null)) continue;
            if (debris.Extent(path, context) is not { } extent || !float.IsFinite(extent.LengthSquared()) || extent.LengthSquared() < 1e-10f) continue;
            result.Add(new(path, extent));
        }
        return result.ToArray();
    }

    static Matrix4x4[] Skin(FxWorld world, CharacterInstance instance, CharacterPoseSetup setup)
    {
        if (!world.Skin.TryGetValue(instance.Key, out var skin) || skin.Length != setup.Rig.BoneCount) world.Skin[instance.Key] = skin = new Matrix4x4[setup.Rig.BoneCount];
        if (!world.Posed.Add(instance.Key)) return skin;
        world.Layers.Clear();
        foreach (var pose in instance.Poses)
            if (resources is not null && setup.Rig.Layer(resources.Rigs.Database, pose.Animation, setup.Animal) is { } layer) world.Layers.Add(new(layer, pose.Time, pose.Weight));
        foreach (var (layer, time) in setup.Fixed) world.Layers.Add(new(layer, time, 1));
        var scratch = setup.Rig.Rent();
        try { scratch.Pose(setup.Shape, CollectionsMarshal.AsSpan(world.Layers), skin, instance.Bones); }
        finally { setup.Rig.Return(scratch); }
        return skin;
    }

    static ulong VisualSeed(in GoreCutRequest request, ulong worldSeed)
    {
        if (request.Hit is not { } hit) return Rng.Mix(worldSeed ^ Rng.Key(request.Victim) ^ (ulong)request.Sequence);
        static ulong Identity(CharacterId id) => ((ulong)(uint)id.Slot << 32) | (uint)id.Generation;
        return Rng.Mix(worldSeed ^ Rng.Mix(Identity(hit.Victim)) ^ Rng.Mix(Identity(hit.Attacker)) ^ Rng.Mix((ulong)hit.Tick)
            ^ Rng.Mix((uint)hit.Part + 0x524F424F5453504BUL) ^ Rng.Mix(BitConverter.SingleToUInt32Bits(hit.Damage.Cut)) ^ Rng.Mix((uint)hit.CutDirection));
    }
    static bool Enabled(object host) => !ReadBool(Member(host, "o"), "NoParticles")
        || ReadBool(host, "IsFlat") && ReadBool(Member(host, "g"), "FlatCombatParticles");
    static object? Member(object? value, string name) => value is null ? null : AccessTools.Field(value.GetType(), name)?.GetValue(value)
        ?? AccessTools.Property(value.GetType(), name)?.GetValue(value);
    static bool ReadBool(object? value, string name, bool fallback = false) => Member(value, name) is bool result ? result : fallback;

    static void PreparePostfix(ParticleRenderer __instance, Matrix4x4 viewProjectionMatrix, Matrix4x4 view, Vector3 eye, Vector3 sunDirection)
    {
        if (!bindings.TryGetValue(__instance, out var binding) || binding.World is null) return;
        binding.Renderer ??= new((IGpuContext)particleGpu.GetValue(__instance)!, folder);
        binding.Renderer.Prepare(binding.World.Scene, viewProjectionMatrix, view, eye, __instance.Enabled && !binding.OpaqueOnly, sunDirection);
    }
    static void RecordPostfix(ParticleRenderer __instance, int divisor)
    { if (divisor == 1 && bindings.TryGetValue(__instance, out var binding)) binding.Renderer?.Record(); }
    static void CoveragePostfix(ParticleRenderer __instance, ColorMask mask, bool depthTest)
    { if (bindings.TryGetValue(__instance, out var binding)) binding.Renderer?.Record(mask, depthTest); }
    static void CountPostfix(ParticleRenderer __instance, int divisor, ref int __result)
    { if (divisor == 1) FullCountPostfix(__instance, ref __result); }
    static void FullCountPostfix(ParticleRenderer __instance, ref int __result)
    { if (bindings.TryGetValue(__instance, out var binding) && binding.Renderer is { HasDraws: true } renderer) __result += renderer.BatchCount; }
    static void HasCoveragePostfix(ParticleRenderer __instance, ref bool __result)
    { if (bindings.TryGetValue(__instance, out var binding) && binding.Renderer is { HasDraws: true }) __result = true; }
    static void DisposePostfix(ParticleRenderer __instance)
    { if (bindings.TryGetValue(__instance, out var binding)) binding.Renderer?.Dispose(); bindings.Remove(__instance); }

    internal static void Release(World world)
    {
        if (!worlds.TryGetValue(world, out var effects)) return;
        effects.Bowels?.Dispose(); effects.Scene.Clear(); effects.Scrap.Clear(); effects.Cables.Clear(); effects.Impacts.Active.Clear(); effects.BowelCuts.Clear(); effects.Recent.Clear();
        worlds.Remove(world);
    }
}
