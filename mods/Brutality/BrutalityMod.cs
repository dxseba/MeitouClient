using System.Text.Json;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation.Ragdolls;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Ui;
using Meitou.Content;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>The separately loaded code mod. Its state and assets belong to the mod, never to PR #270.</summary>
public sealed class BrutalityMod : IMod
{
    public bool Enabled { get; private set; }
    public AnatomyLibrary? Anatomy { get; private set; }

    public void Initialize(ModContext context)
    {
        var path = Path.Combine(context.Folder, "brutality.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<BrutalitySettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Empty Brutality settings")
            : new BrutalitySettings();
        var menu = new BrutalityMenuSettings { GoreEnabled = settings.Enabled };
        context.AddSettings(menu);
        Enabled = menu.GoreEnabled;
        if (!Enabled) { context.Log("gore disabled at startup"); return; }
        SandboxProfileAdapter.Install(context, settings.DeveloperTools);
        DeveloperCombatDiagnostics.Configure(settings.DeveloperTools, Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS"));
        Anatomy = new AnatomyLibrary(new AnatomyAssets(context.Folder));
        Meitou.Brutality.Physics.RagdollPhysics.WarmUpOnce();
        CombatAdapter.Install(context);
        SaveIdentityAdapter.Install(context);
        IncapacitatedCombatAdapter.Install(context);
        BodyFragmentProxySystem.Install(context);
        ImpactEffects.Install(context);
        var uiPreferences = BrutalityUiAdapter.Install(context);
        context.AddUi(new BrutalityUiExtension(uiPreferences));
        BrutalityDebugCommands.Register(context, settings.DeveloperTools);
        RenderResources? resources = GameInstall.Locate() is { } install ? new RenderResources(install, context.Data, Anatomy) : null;
        CutEquipmentAdapter.Install(context, resources?.Rigs);
        if (resources is not null)
        {
            RenderBridge.Install(context, resources);
            BodyWoundRenderBridge.Install(context, resources);
            Meitou.Brutality.Rendering.Surface.NativeSurfaceBridge.Install(context, resources);
            EffectsBridge.Install(context, context.Folder, resources);
        }
        var builds = new ConditionalWeakTable<SystemBuildContext, BrutalityGameplaySystem>();
        BrutalityGameplaySystem Gameplay(SystemBuildContext build) => builds.GetValue(build, b => new BrutalityGameplaySystem(b.Find<CombatSystem>(),
            context.Population.Bodies.Constants, context.Population.BodyOptions, context.RngPurpose));
        context.AddSystem("gore", Gameplay, SystemAnchor.AfterPart(StandardParts.Combat));
        if (resources is not null)
            context.AddSystem("fragments", build => new FragmentPhysicsSystem(
                Gameplay(build), resources.Rigs,
                build.Find<RagdollSystem>() is { } native ? AccessTools.Field(typeof(RagdollSystem), "environment").GetValue(native) as IRagdollEnvironment : null),
                SystemAnchor.AfterPart(StandardParts.Animation));
        if (settings.DeveloperTools && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is { Length: > 0 } preview)
        {
            GorePreviewCamera.Install(context);
            context.AddSystem("visual-check", _ => new GoreVisualCheckSystem(preview), SystemAnchor.Before(StandardParts.Combat));
        }
        context.Log("Brutality DLL initialized; anatomy is resolved from this mod folder");
    }
}

sealed class BrutalityMenuSettings
{
    [Setting("Brutality gore (restart required)", Hint = "Enables Brutality progression, advanced wounds and extra combat effects. Restart to apply.")]
    public bool GoreEnabled { get; set; }
}

public sealed record BrutalitySettings
{
    public bool Enabled { get; init; } = true;
    public bool DeveloperTools { get; init; }
}
