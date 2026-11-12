using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Objects;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Brutality.Effects;

/// <summary>Uses the stock debris list for real mechanical pieces, without requiring a tinted-debris renderer API.</summary>
internal sealed class StockRobotDebris
{
    const string Steel = "__brutality_stock_scrap_steel";
    const int PendingLimit = 32, ContactLimit = 64;
    const double MaximumScrapSeconds = 60;
    static readonly string[] ItemIds = ["42159-gamedata.base", "43397-changes_otto.mod", "43396-changes_otto.mod"];
    static StockRobotDebris? installed;
    readonly GameDatabase data;
    readonly FieldInfo session, gpu, debris, debrisList, gpuContext, textures, nativeHeads, headFrom, headStart, headVelocity, headAside, headSpin;
    readonly FieldInfo rendererStates, stateBones, options, noParticles, metalRenderer, battle, assets;
    readonly PropertyInfo isFlat;
    readonly MethodInfo rgba8, drawKey, activeRenderer, bonePosition, lastDrawn, loadTexture;
    readonly ConstructorInfo textureConstructor;
    readonly ConditionalWeakTable<object, Scene> scenes = new();
    readonly ConditionalWeakTable<object, object> headsSeen = new();
    readonly ConditionalWeakTable<DebrisRenderer, Meshes> available = new();
    readonly Dictionary<GameRecord, (uint Blood, BodyPartTemplate[] Parts)> races = new();
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly bool particlesDisabled = Environment.GetCommandLineArgs().Contains("--no-particles", StringComparer.Ordinal);

    sealed class Meshes
    {
        public readonly List<RobotScrapMesh> List = [];
        public int Next;
        public bool SteelReady, Faulted;
    }

    sealed class Scene(World world)
    {
        public readonly World World = world;
        public readonly RobotScrapSimulation Motion = new();
        public readonly Queue<(GoreEffectCut Cut, double Born)> Pending = new();
        public readonly Dictionary<(CharacterId Owner, int Part, int Kind), (double Expires, Func<int, CutFluidContact?> Follow)> Contacts = [];
        public bool LoggedUnavailableRenderer;
    }

    StockRobotDebris(ModContext context, Type host, Type renderer)
    {
        data = context.Data;
        session = Field(host, "Session");
        gpu = Field(host, "Gpu");
        debris = Field(gpu.FieldType, "Debris");
        debrisList = Field(gpu.FieldType, "DebrisPieces");
        gpuContext = Field(typeof(DebrisRenderer), "_gpu");
        textures = Field(typeof(DebrisRenderer), "_textures");
        loadTexture = Method(typeof(DebrisRenderer), "Texture", typeof(string));
        assets = Field(host, "Assets");
        var image = typeof(DebrisRenderer).Assembly.GetType("Meitou.Game.Rendering.Sky.SampledImage")
            ?? throw new TypeLoadException("SampledImage");
        rgba8 = Method(image, "Rgba8", typeof(IGpuContext), typeof(int), typeof(int), typeof(byte[]), typeof(bool), typeof(bool), typeof(string));
        var texture = textures.FieldType.GetGenericArguments()[1];
        textureConstructor = AccessTools.Constructor(texture, [typeof(IGpuContext), image])
            ?? throw new MissingMethodException(texture.Name, ".ctor");
        nativeHeads = Field(Field(host, "Decapitation").FieldType, "_heads");
        var nativeHead = nativeHeads.FieldType.GetGenericArguments()[0];
        headFrom = Field(nativeHead, "From"); headStart = Field(nativeHead, "Start");
        headVelocity = Field(nativeHead, "Velocity"); headAside = Field(nativeHead, "Aside"); headSpin = Field(nativeHead, "Spin");
        options = Field(host, "WorldOptions");
        noParticles = Field(options.FieldType, "NoParticles");
        metalRenderer = Field(host, "MetalRenderer");
        battle = Field(host, "Battle");
        isFlat = AccessTools.Property(battle.FieldType, "IsFlat") ?? throw new MissingMemberException(battle.FieldType.Name, "IsFlat");
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        activeRenderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMemberException("ActiveCharacters");
        bonePosition = Method(renderer, "BonePosition", typeof(long), typeof(string));
        lastDrawn = Method(renderer, "LastDrawn", typeof(long), typeof(string));
        rendererStates = Field(renderer, "_states");
        stateBones = Field(rendererStates.FieldType.GetGenericArguments()[1], "Bones");
    }

    internal static void Install(ModContext context)
    {
        if (installed is not null) return;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var renderer = Field(Field(host, "Gpu").FieldType, "Characters").FieldType;
            var hooks = new StockRobotDebris(context, host, renderer);
            context.Harmony.Patch(Method(Field(host, "Modding").FieldType, "UpdateModHits"), postfix: Patch(nameof(AfterFrame)));
            context.Harmony.Patch(Method(Field(host, "Decapitation").FieldType, "CutHead", typeof(CharacterSnapshot), typeof(CharacterAppearance)),
                postfix: Patch(nameof(AfterHead)));
            context.Harmony.Patch(Method(host, "Run"), finalizer: Patch(nameof(AfterRun)));
            installed = hooks;
            context.Log("Brutality stock robot scrap initialized");
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock robot scrap unavailable: {error.GetBaseException().Message}");
        }
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockRobotDebris), name);
    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);

    static void AfterFrame(object __instance)
    {
        if (StockHostParts.HostOf(__instance) is { } host) installed?.Update(host);
    }

    static void AfterHead(object __instance, CharacterSnapshot c, CharacterAppearance look)
    {
        if (StockHostParts.HostOf(__instance) is { } host) installed?.Head(host, __instance, c, look);
    }
    static void AfterRun(object __instance) => installed?.scenes.Remove(__instance);

    internal static void BeginTorso(object host, in GoreEffectCut cut, double time)
    {
        if (!cut.Robot || installed is not { } hooks) return;
        hooks.Queue(host, cut, time, null);
    }

    internal static void BeginLimb(object host, StockLimbRules.Request request, CharacterAppearance look,
        SkeletonRig rig, Vector4 plane, Matrix4x4 world, Matrix4x4[] birth, AdvancedFragmentScene.Piece piece,
        Func<bool> stillPresent, double time)
    {
        if (installed is not { } hooks || !hooks.Enabled(host)) return;
        var (blood, parts) = hooks.Race(look.Race);
        int slot = request.Shape.LimbSlot, index = Array.FindIndex(parts, part => part.LimbSlot == slot);
        if (index < 0) return;
        var rule = new CutHalfspace(plane, Group: slot + 1);
        int cutBone = MeshCut.SectionBone(rig, rule);
        if ((uint)cutBone >= (uint)birth.Length || rig.ByHandle[cutBone]?.Name is not { } boneName) return;
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        var bind = rig.BindPosition[cutBone];
        var localPoint = bind - normal * (Vector3.Dot(normal, bind) + plane.W);
        var initial = birth[cutBone] * world;
        var point = Vector3.Transform(localPoint, initial);
        int root = piece.Definition.Root;
        var start = piece.InitialPose[root];
        var rootLocal = start.Inverse().Apply(point);
        var rootNormal = Vector3.Transform(Unit(Vector3.TransformNormal(normal, initial)), Quaternion.Inverse(start.Rotation));
        if (!request.Robot || !look.Race.GetBool("is robot"))
        {
            if (look.Race.GetFloat("bleed rate", 1) > 0) MasterImpactFeed.BeginOrganicLimb(host, request, blood, Follow, time);
            return;
        }
        var cut = new GoreEffectCut(request.Owner, index, 0, slot + 10, request.Tick, request.Travel, 100,
            blood, request.Seed, true, point) { ExplosionSeed = (uint)RobotVisualRandom.Mix(request.Seed ^ 0x4C494D42UL) };
        hooks.Queue(host, cut, time, Follow);
        MasterImpactFeed.BeginMechanical(host, cut, time);
        if (hooks.trace) Console.WriteLine($"Brutality stock robot limb rupture owner={request.Owner} slot={slot} at={point}");

        CutFluidContact? Follow(int half)
        {
            if (half == 0) return hooks.Retained(host, request.Owner, boneName, cutBone, localPoint, -normal);
            if (!stillPresent()) return null;
            var pose = piece.Pose[root];
            return new(pose.Apply(rootLocal), Unit(Vector3.Transform(rootNormal, pose.Rotation)));
        }
    }

    void Head(object host, object decapitation, CharacterSnapshot actor, CharacterAppearance look)
    {
        if (!look.Headless || !look.Race.GetBool("is robot") || !Enabled(host)
            || session.GetValue(host) is not WorldSession shown || nativeHeads.GetValue(decapitation) is not IList { Count: > 0 } heads
            || heads[heads.Count - 1] is not { } head || drawKey.Invoke(host, [actor.Id]) is not long key
            || headFrom.GetValue(head) is not long from || key != from || headStart.GetValue(head) is not Vector3 start
            || headsSeen.TryGetValue(head, out _)) return;
        headsSeen.Add(head, new());
        var (blood, parts) = Race(look.Race);
        int part = Array.FindIndex(parts, template => template.Type == BodyPartType.Head);
        if (part < 0) return;
        var travel = new Vector2(actor.Knockdown.X, actor.Knockdown.Z);
        ulong seed = RobotVisualRandom.Mix(shown.World.Seed ^ Rng.Key(actor.Id) ^ (ulong)shown.ShownTick);
        var cut = new GoreEffectCut(actor.Id, part, 1, -1, shown.ShownTick, travel, 100, blood, seed, true, start)
            { ExplosionSeed = (uint)RobotVisualRandom.Mix(seed ^ 0x48454144UL) };
        double time = (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;
        float weight = RobotRagdollWeight.Response(true);
        if (headVelocity.GetValue(head) is Vector3 velocity) headVelocity.SetValue(head, velocity * weight);
        if (headAside.GetValue(head) is Vector2 aside)
            headAside.SetValue(head, Vector2.Lerp(new(start.X, start.Z), aside, weight));
        if (headSpin.GetValue(head) is float spin) headSpin.SetValue(head, spin * weight);
        Queue(host, cut, time, Follow);
        MasterImpactFeed.BeginMechanical(host, cut, time);
        if (trace) Console.WriteLine($"Brutality stock robot head rupture owner={actor.Id} at={start}");

        CutFluidContact? Follow(int half)
        {
            if (StockHeadFragments.MechanicalContact(host, actor.Id, half) is { } split) return split;
            long shownKey = half == 0 ? key : key ^ (1L << 54);
            return activeRenderer.Invoke(host, null) is { } renderer
                && bonePosition.Invoke(renderer, [shownKey, half == 0 ? "Bip01 Neck" : "Bip01 Head"]) is Vector3 at
                ? new(at, half == 0 ? Vector3.UnitY : -Vector3.UnitY) : null;
        }
    }

    internal static CutFluidContact? Contact(object host, in GoreEffectCut cut, int half) =>
        installed is { } hooks && hooks.scenes.TryGetValue(host, out var scene)
        && scene.Contacts.TryGetValue((cut.Owner, cut.Part, cut.Kind), out var anchor) ? anchor.Follow(half) : null;

    internal static bool HasRupture(object host, CharacterId owner, int part) =>
        installed is { } hooks && hooks.scenes.TryGetValue(host, out var scene)
        && (scene.Contacts.ContainsKey((owner, part, 0)) || scene.Contacts.ContainsKey((owner, part, 1)));

    CutFluidContact? Retained(object host, CharacterId owner, string boneName, int bone, Vector3 local, Vector3 normal)
    {
        if (activeRenderer.Invoke(host, null) is not { } renderer || drawKey.Invoke(host, [owner]) is not long key
            || lastDrawn.Invoke(renderer, [key, boneName]) is not ValueTuple<Matrix4x4, CharacterAppearance, Vector3> shown
            || rendererStates.GetValue(renderer) is not IDictionary states || states[key] is not { } state
            || stateBones.GetValue(state) is not Matrix4x4[] skin || (uint)bone >= (uint)skin.Length) return null;
        var transform = skin[bone] * shown.Item1;
        return new(Vector3.Transform(local, transform), Unit(Vector3.TransformNormal(normal, transform)));
    }

    static Vector3 Unit(Vector3 vector)
    {
        float length = vector.LengthSquared();
        return float.IsFinite(length) && length > 1e-10f ? vector / MathF.Sqrt(length) : Vector3.UnitY;
    }

    (uint Blood, BodyPartTemplate[] Parts) Race(GameRecord record)
    {
        if (races.TryGetValue(record, out var known)) return known;
        var race = RaceData.From(record, data);
        known = (race.BloodColour, race.Anatomy.Select(entry => entry.Part).OrderBy(part => part.MedicalOrder).ToArray());
        races.Add(record, known);
        return known;
    }

    bool Enabled(object host)
    {
        if (particlesDisabled || metalRenderer.GetValue(host) is true
            && Environment.GetEnvironmentVariable("MEITOU_METAL_NO_PARTICLES") == "1") return false;
        // Flat arenas disable weather and map effects in Program. Combat debris has its own stock renderer.
        return IsFlat(host) || options.GetValue(host) is { } value && noParticles.GetValue(value) is not true;
    }

    bool IsFlat(object host) => battle.GetValue(host) is { } arena && isFlat.GetValue(arena) is true;

    void Queue(object host, in GoreEffectCut cut, double time, Func<int, CutFluidContact?>? follow)
    {
        bool enabled = Enabled(host);
        if (!enabled || session.GetValue(host) is not WorldSession shown)
        {
            if (trace) Console.WriteLine($"Brutality stock robot scrap skipped kind={cut.Kind} enabled={enabled} flat={IsFlat(host)} explicitNoParticles={particlesDisabled}");
            return;
        }
        var scene = For(host, shown.World);
        if (scene.Pending.Count >= PendingLimit) scene.Pending.Dequeue();
        scene.Pending.Enqueue((cut, time));
        if (trace) Console.WriteLine($"Brutality stock robot scrap queued kind={cut.Kind} owner={cut.Owner} tick={cut.Tick}");
        if (follow is null) return;
        if (scene.Contacts.Count >= ContactLimit && !scene.Contacts.ContainsKey((cut.Owner, cut.Part, cut.Kind)))
            scene.Contacts.Remove(scene.Contacts.MinBy(contact => contact.Value.Expires).Key);
        scene.Contacts[(cut.Owner, cut.Part, cut.Kind)] = (time + GoreEffectsSimulation.MaximumDischargeSeconds, follow);
    }

    Scene For(object host, World world)
    {
        if (scenes.TryGetValue(host, out var scene))
        {
            if (ReferenceEquals(scene.World, world)) return scene;
            scenes.Remove(host);
        }
        return scenes.GetValue(host, _ => new(world));
    }

    void Update(object host)
    {
        if (!scenes.TryGetValue(host, out _) || session.GetValue(host) is not WorldSession shown) return;
        if (!Enabled(host)) { scenes.Remove(host); return; }
        var scene = For(host, shown.World);
        double now = (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;
        if (scene.Contacts.Count > 0)
            foreach (var key in scene.Contacts.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                scene.Contacts.Remove(key);
        if (scene.Motion.Count == 0 && scene.Pending.Count == 0) return;
        if (gpu.GetValue(host) is not { } frame || debris.GetValue(frame) is not DebrisRenderer render
            || debrisList.GetValue(frame) is not List<DebrisPiece> drawn)
        {
            if (trace && !scene.LoggedUnavailableRenderer)
            {
                scene.LoggedUnavailableRenderer = true;
                Console.WriteLine("Brutality stock robot scrap awaiting stock debris renderer");
            }
            return;
        }

        scene.Motion.Advance(now, Math.Min(MaximumScrapSeconds, BodySystem.CorpseTicksOf(shown.World) * shown.World.TickSeconds));
        if (scene.Pending.Count > 0)
        {
            var meshes = available.GetValue(render, static _ => new());
            Prepare(render, meshes, assets.GetValue(host) as AssetLocator);
            if (meshes.Faulted) scene.Pending.Clear();
            else if (meshes.Next == ItemIds.Length)
            {
                if (meshes.List.Count == 0 && trace) Console.WriteLine("Brutality stock robot scrap found no installed mechanical meshes");
                while (scene.Pending.TryDequeue(out var request))
                {
                    if (now - request.Born > 2)
                    {
                        if (trace) Console.WriteLine($"Brutality stock robot scrap expired kind={request.Cut.Kind} elapsed={now - request.Born:F2}");
                        continue;
                    }
                    var point = Contact(host, request.Cut, request.Cut.Kind == 0 ? 1 : 0)?.Position ?? request.Cut.Fallback;
                    float floor = shown.World.Walkability.GroundHeight(point.X, point.Z, point.Y);
                    int before = scene.Motion.Count;
                    if (scene.Motion.Begin(request.Cut, point, floor, now, shown.World.Seed, meshes.List) && trace)
                        Console.WriteLine($"Brutality stock robot scrap kind={request.Cut.Kind} meshes={meshes.List.Count} active={scene.Motion.Count} added={scene.Motion.Count - before} at={point}");
                    else if (trace) Console.WriteLine($"Brutality stock robot scrap rejected kind={request.Cut.Kind} floor={floor} at={point}");
                }
            }
        }
        drawn.AddRange(scene.Motion.Drawn);
    }

    void Prepare(DebrisRenderer renderer, Meshes meshes, AssetLocator? locator)
    {
        if (meshes.Faulted || meshes.Next >= ItemIds.Length) return;
        try
        {
            if (data.Find(ItemIds[meshes.Next++]) is not { } item) return;
            string material = item.GetPath("mesh");
            string mesh = item.GetPath("ground mesh") is { Length: > 0 } ground ? ground : material;
            if (mesh.Length == 0) return;
            var context = (IGpuContext)gpuContext.GetValue(renderer)!;
            context.EnsureFrame();
            // The flat world has no object material resolver. Give the native debris cache the item atlas explicitly.
            string? diffuse = locator is null ? null : NativeDiffuse(renderer, locator, item);
            if (diffuse is null && !meshes.SteelReady)
            {
                var cache = (IDictionary)textures.GetValue(renderer)!;
                const int width = 8;
                var image = rgba8.Invoke(null, [context, width, width, SteelPixels(width), true, false, Steel]);
                if (image is not IDisposable resource) throw new InvalidOperationException("Stock steel texture upload failed.");
                try { cache[Steel] = textureConstructor.Invoke([context, image]); }
                catch { resource.Dispose(); throw; }
                // The stock DebrisRenderer now owns this texture and releases it with the rest of its cache.
                meshes.SteelReady = true;
            }
            string textured = mesh + DebrisPiece.TextureMark + (diffuse ?? Steel);
            if (renderer.Extent(textured, material.Length > 0 ? material : mesh) is { } extent
                && float.IsFinite(extent.LengthSquared()) && extent.LengthSquared() > 1e-10f)
            {
                meshes.List.Add(new(textured, extent));
                if (trace) Console.WriteLine($"Brutality stock robot scrap material item={item.Name} diffuse={diffuse ?? Steel}");
            }
        }
        catch (Exception error)
        {
            meshes.Faulted = true;
            if (trace) Console.WriteLine($"Brutality stock robot scrap unavailable: {error.GetBaseException().Message}");
        }
    }

    string? NativeDiffuse(DebrisRenderer renderer, AssetLocator locator, GameRecord item)
    {
        if (Available(item.GetPath("texture map")) is { } direct) return direct;
        foreach (var reference in item.GetReferences("material"))
            if (data.Find(reference.TargetStringId) is { } material && Available(material.GetPath("texture map")) is { } diffuse)
                return diffuse;
        return null;

        string? Available(string texture)
        {
            string name = Path.GetFileName(texture.Replace('\\', '/'));
            return name.Length > 0 && !MaterialResolver.Placeholders.Contains(name) && locator.Find(name) is not null
                && loadTexture.Invoke(renderer, [name]) is not null ? name : null;
        }
    }

    static byte[] SteelPixels(int width)
    {
        var pixels = new byte[width * width * 4];
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                int grain = (x * 19 + y * 27 + x * y * 3) % 23;
                int streak = y is 2 or 5 ? 11 : 0;
                int oxide = (x * 5 + y * 11 + x * y) % 17 > 10 ? 10 : 0;
                pixels[i] = (byte)(55 + grain + streak + oxide);
                pixels[i + 1] = (byte)(50 + grain + streak + oxide / 2);
                pixels[i + 2] = (byte)(44 + grain + streak - oxide / 3);
                pixels[i + 3] = (byte)(112 + grain * 2 + streak);
            }
        return pixels;
    }
}
