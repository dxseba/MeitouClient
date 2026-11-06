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
            LimbDropAdapter.Install(context);
            RenderBridge.Install(context, resources);
            BodyWoundRenderBridge.Install(context, resources);
            Meitou.Brutality.Rendering.Surface.NativeSurfaceBridge.Install(context, resources);
            EffectsBridge.Install(context, context.Folder, resources);
        }
        var builds = new ConditionalWeakTable<SystemBuildContext, BrutalityGameplaySystem>();
        BrutalityGameplaySystem Gameplay(SystemBuildContext build) => builds.GetValue(build, b => new BrutalityGameplaySystem(b.Find<CombatSystem>(),
            context.Population.Bodies.Constants, context.Population.BodyOptions, context.RngPurpose, chestHoleRule: () => menu.ChestHoleRule));
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

    [Setting("Martial arts punch-through", Hint = "A bare-handed blow to the chest or stomach with enough damage leaves a hole through the body.")]
    public bool ChestHoles { get; set; } = true;

    [Setting("Punch-through only on killing blows", Hint = "On: only a punch that kills opens the hole (fights usually end in a knockout first).")]
    public bool ChestHoleLethalOnly { get; set; }

    [Setting("Punch-through damage (% of part health)",
        Hint = "Damage after armour needed, as a share of the struck chest's or stomach's maximum health.", Min = 10, Max = 150, Step = 5)]
    public int ChestHoleDamagePercent { get; set; } = 50;

    [Setting("Punch-through size (%)", Hint = "Scales the holes of new punches; holes already made keep their size.", Min = 50, Max = 150, Step = 5)]
    public int ChestHoleSizePercent { get; set; } = 100;

    public ChestHoleRule ChestHoleRule => new(ChestHoles, ChestHoleLethalOnly, ChestHoleDamagePercent / 100f, ChestHoleSizePercent / 100f);
}

public sealed record BrutalitySettings
{
    public bool Enabled { get; init; } = true;
    public bool DeveloperTools { get; init; }
}
