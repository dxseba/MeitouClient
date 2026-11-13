using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Content;
using Meitou.Data.Ogre;
using Meitou.Data.Particles;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Particles;
using Meitou.Game.Rendering.Terrain;
using Meitou.Modding;

namespace Meitou.Brutality.Effects;

/// <summary>Attaches Brutality's draw batches to the stock particle passes without a character geometry override.</summary>
internal sealed class MasterParticleHooks
{
    sealed class Binding(MasterParticleHooks hooks, object host)
    {
        public readonly MasterParticleHooks Hooks = hooks;
        public readonly object Host = host;
        public GoreEffectRenderer? Renderer;
        public bool Faulted;
        public bool LoggedBlood, LoggedDischarge;
    }

    static MasterParticleHooks? installed;
    static readonly ConditionalWeakTable<ParticleRenderer, Binding> bindings = new();
    static readonly FieldInfo? particleContext = AccessTools.Field(typeof(ParticleRenderer), "_gpu");
    static readonly FieldInfo? terrainContext = AccessTools.Field(typeof(TerrainRenderer), "_gpu");
    readonly FieldInfo? hostGpu, hostParticles, hostTerrain, metalRenderer, battle;
    readonly PropertyInfo? isFlat;
    readonly string folder;
    readonly Func<object, GoreEffectsSimulation?> sceneForHost;
    readonly bool allowFlatCombat;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly ConditionalWeakTable<object, object> flatAttempts = new();

    internal MasterParticleHooks(ModContext context, Func<object, GoreEffectsSimulation?> sceneForHost, bool allowFlatCombat = false)
    {
        this.sceneForHost = sceneForHost;
        folder = context.Folder;
        this.allowFlatCombat = allowFlatCombat && !Environment.GetCommandLineArgs().Contains("--no-particles");
        if (AccessTools.TypeByName("Meitou.Game.GameHost") is not { } host) return;

        hostGpu = AccessTools.Field(host, "Gpu") ?? throw new MissingFieldException(host.FullName, "Gpu");
        hostParticles = AccessTools.Field(hostGpu.FieldType, "Particles") ?? throw new MissingFieldException(hostGpu.FieldType.FullName, "Particles");
        hostTerrain = AccessTools.Field(hostGpu.FieldType, "Terrain");
        metalRenderer = AccessTools.Field(host, "MetalRenderer");
        battle = AccessTools.Field(host, "Battle");
        if (battle is not null) isFlat = AccessTools.Property(battle.FieldType, "IsFlat");
        installed = this;

        var renderer = typeof(ParticleRenderer);
        Patch(AccessTools.Method(AccessTools.Field(host, "BloodDecals").FieldType, "SetUpBlood"), nameof(SetupPostfix));
        // Metal can set up blood before loading mods. This also binds the current renderer after a host swap.
        Patch(AccessTools.Method(AccessTools.Field(host, "Modding").FieldType, "UpdateModHits"), nameof(FramePostfix));
        Patch(AccessTools.Method(renderer, nameof(ParticleRenderer.Prepare)), nameof(PreparePostfix));
        Patch(AccessTools.Method(renderer, nameof(ParticleRenderer.Record)), nameof(RecordPostfix));
        Patch(AccessTools.Method(renderer, nameof(ParticleRenderer.DrawCoverage)), nameof(CoveragePostfix));
        Patch(AccessTools.Method(renderer, nameof(ParticleRenderer.CountFor)), nameof(CountPostfix));
        Patch(AccessTools.PropertyGetter(renderer, nameof(ParticleRenderer.FullCount)), nameof(FullCountPostfix));
        Patch(AccessTools.PropertyGetter(renderer, nameof(ParticleRenderer.HasCoverage)), nameof(HasCoveragePostfix));
        Patch(AccessTools.Method(renderer, nameof(ParticleRenderer.Dispose)), nameof(DisposePostfix));

        void Patch(MethodInfo? method, string callback) =>
            context.Harmony.Patch(method ?? throw new MissingMethodException($"Brutality particle hook: {callback}"),
                postfix: new(AccessTools.Method(typeof(MasterParticleHooks), callback)));
    }

    static void SetupPostfix(object __instance, IGpuContext context)
    {
        if (StockHostParts.HostOf(__instance) is { } host) installed?.Bind(host, context);
    }

    static void FramePostfix(object __instance)
    {
        if (StockHostParts.HostOf(__instance) is { } host) installed?.Bind(host);
    }

    void Bind(object host, IGpuContext? context = null)
    {
        if (hostGpu?.GetValue(host) is not { } gpu || hostParticles is null) return;
        if (hostParticles.GetValue(gpu) is not ParticleRenderer particles)
        {
            if (!allowFlatCombat || flatAttempts.TryGetValue(gpu, out _)
                || battle?.GetValue(host) is not { } arena || isFlat?.GetValue(arena) is not true
                || (metalRenderer?.GetValue(host) is true && Environment.GetEnvironmentVariable("MEITOU_METAL_NO_PARTICLES") == "1")) return;
            context ??= hostTerrain?.GetValue(gpu) is TerrainRenderer terrain ? terrainContext?.GetValue(terrain) as IGpuContext : null;
            if (context is null) return;
            flatAttempts.Add(gpu, new());
            try
            {
                if (GameInstall.Locate() is not { } install) return;
                // Only the draw pass is needed for the mod's own assets. Leave the arena's native weather disabled.
                particles = ParticleRenderer.Create(context, new AssetLocator(install), new ParticleLibrary());
                hostParticles.SetValue(gpu, particles);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Brutality: could not prepare the flat arena effects pass: {error.Message}");
                return;
            }
        }
        if (!bindings.TryGetValue(particles, out _)) bindings.Add(particles, new(this, host));
    }

    static void PreparePostfix(ParticleRenderer __instance, Matrix4x4 viewProjectionMatrix, Matrix4x4 view, Vector3 eye, Vector3 sunDirection)
    {
        if (!__instance.Enabled || !bindings.TryGetValue(__instance, out var binding) || binding.Faulted
            || binding.Hooks.sceneForHost(binding.Host) is not { } scene) return;
        if (binding.Renderer is null && scene.Anchors.Count == 0 && scene.Explosions.Count == 0
            && scene.Blood.Drops.Count == 0 && scene.Blood.Streams.Count == 0 && scene.Bowels.Count == 0 && scene.Cables.Count == 0) return;

        try
        {
            if (binding.Renderer is null && particleContext?.GetValue(__instance) is IGpuContext context)
                binding.Renderer = new(context, binding.Hooks.folder);
            // The stock pass receives eye-relative matrices (RenderOrigin); the effects renderer takes a world one and removes the eye itself.
            var world = Matrix4x4.CreateTranslation(-eye) * viewProjectionMatrix;
            binding.Renderer?.Prepare(scene, world, view, eye, true, sunDirection);
        }
        catch (Exception error)
        {
            binding.Faulted = true;
            binding.Renderer?.Dispose();
            binding.Renderer = null;
            Console.Error.WriteLine($"Brutality: could not prepare the effects renderer: {error.Message}");
        }
    }

    static GoreEffectRenderer? Ready(ParticleRenderer particles) =>
        bindings.TryGetValue(particles, out var binding) && binding.Renderer is { HasDraws: true } renderer ? renderer : null;

    static void RecordPostfix(ParticleRenderer __instance, int divisor)
    {
        if (divisor != 1 || !bindings.TryGetValue(__instance, out var binding)
            || binding.Renderer is not { HasDraws: true } renderer) return;
        renderer.Record();
        if (!binding.Hooks.trace || binding.Hooks.sceneForHost(binding.Host) is not { } scene) return;
        if (!binding.LoggedBlood && scene.Blood.Drops.Count > 0)
        {
            binding.LoggedBlood = true;
            Console.WriteLine($"Brutality stock renderer drew liquid blood drops={scene.Blood.Drops.Count}");
        }
        if (!binding.LoggedDischarge && scene.Anchors.Count > 0)
        {
            binding.LoggedDischarge = true;
            Console.WriteLine($"Brutality stock renderer drew short-circuit anchors={scene.Anchors.Count}");
        }
    }

    static void CoveragePostfix(ParticleRenderer __instance, ColorMask mask, bool depthTest) =>
        Ready(__instance)?.Record(mask, depthTest);

    static void CountPostfix(ParticleRenderer __instance, int divisor, ref int __result)
    {
        if (divisor == 1) FullCountPostfix(__instance, ref __result);
    }

    static void FullCountPostfix(ParticleRenderer __instance, ref int __result)
    {
        if (Ready(__instance) is { } renderer) __result += renderer.BatchCount;
    }

    static void HasCoveragePostfix(ParticleRenderer __instance, ref bool __result) => __result |= Ready(__instance) is not null;

    static void DisposePostfix(ParticleRenderer __instance)
    {
        if (bindings.TryGetValue(__instance, out var binding)) binding.Renderer?.Dispose();
        bindings.Remove(__instance);
    }
}
