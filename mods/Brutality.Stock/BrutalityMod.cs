using System.Globalization;
using System.Text.Json;
using Meitou.Brutality.Effects;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>The impact-effects build for hosts without the runtime character presentation API.</summary>
public sealed class BrutalityMod : IMod
{
    public void Initialize(ModContext context)
    {
        string path = Path.Combine(context.Folder, "brutality.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<StockSettings>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new StockSettings()
            : new StockSettings();
        settings.ReadLegacyMenuDefaults();
        context.AddSettings(settings);
        if (!settings.Enabled) return;

        var effects = new MasterImpactFeed(context);
        StockRobotDebris.Install(context);
        Physics.StockNativeRagdollContacts.Install(context);
        _ = new MasterParticleHooks(context, effects.SceneForHost, allowFlatCombat: true);
        StockHeadRules.Enabled = StockHeadFragments.Install(context) && StockHeadGate.Install(context);
        StockTorsoRules.Enabled = StockTorsoFragments.Install(context);
        if (StockTorsoRules.Enabled)
        {
            StockTorsoLaunch.Install(context);
            StockTorsoProxies.Install(context);
        }
        if (StockTorsoRules.Enabled || StockHeadRules.Enabled) StockTorsoSave.Install(context);
        if (StockTorsoRules.Enabled) StockTorsoProxySave.Install(context);
        StockPartLabels.Install(context);
        StockLimbRules.Enabled = StockLimbFragments.Install(context) && StockLimbRules.Install(context);
        StockCutEquipment.Install(context);
        if (StockLimbRules.Enabled) StockLimbSave.Install(context);
        StockWeaponStains.Install(context);
        StockGarmentDamage.Install(context);
        StockTargetHooks.Install(context);
        StockWoundRules.Install(context, () => settings.ChestHoleRule);
        if (StockDetachedPools.Install(context))
        {
            StockNativeLimbPools.Install(context);
            StockNativeHeadPools.Install(context);
        }
        StockBowelSave.Install(context);
#if BRUTALITY_STOCK_SAVE_PROBE
        StockTorsoSaveProbe.Install(context);
        StockSandboxCombatProbe.Install(context);
#endif
        StockSaveMigration.Install(context);
        StockProgression.Install(context);
        StockSandboxProfile.Install(context, settings.DeveloperTools);
        if (settings.DeveloperTools) StockDebugPreview.Install(context);
        if (StockLimbRules.Enabled) context.Log("Brutality stock limb fragments initialized");
        if (StockTorsoRules.Enabled) context.Log("Brutality stock torso fragments initialized");
        if (StockHeadRules.Enabled) context.Log("Brutality stock head fragments initialized");
        context.Log("Brutality impact effects and progression initialized on stock master");
    }

    sealed class StockSettings
    {
        public bool DeveloperTools { get; set; }
        [Setting("Brutality (restart required)", Hint = "Enables Brutality progression, advanced wounds and extra combat effects.")]
        public bool Enabled { get; set; } = true;

        [Setting("Martial arts punch-through", Hint = "A bare-handed blow to the chest or stomach with enough damage leaves a hole through the body.")]
        public bool ChestHoles { get; set; } = true;

        [Setting("Punch-through only on killing blows", Hint = "On: only a punch that kills opens the hole (fights usually end in a knockout first).")]
        public bool ChestHoleLethalOnly { get; set; }

        [Setting("Punch-through damage (% of part health)",
            Hint = "Damage after armour needed, as a share of the struck chest's or stomach's maximum health.", Min = 10, Max = 150, Step = 5)]
        public int ChestHoleDamagePercent { get; set; } = 50;

        [Setting("Punch-through size (%)", Hint = "Scales the holes of new punches; holes already made keep their size.", Min = 50, Max = 150, Step = 5)]
        public int ChestHoleSizePercent { get; set; } = 100;

        // The host applies saved stock values after these fallbacks, keyed by the stock manifest id.
        // Match its config path precedence and leave the old mod's settings file intact.
        internal void ReadLegacyMenuDefaults()
        {
            const string fileName = "meitou.user.json";
            var path = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                .Select(directory => Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);
            if (path is null) return;
            try
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) return;
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("modSettings", out var groups) || groups.ValueKind != JsonValueKind.Object
                    || !groups.TryGetProperty("dxseba.brutality", out var old) || old.ValueKind != JsonValueKind.Object) return;

                Enabled = Switch("GoreEnabled", Enabled);
                ChestHoles = Switch(nameof(ChestHoles), ChestHoles);
                ChestHoleLethalOnly = Switch(nameof(ChestHoleLethalOnly), ChestHoleLethalOnly);
                ChestHoleDamagePercent = Slider(nameof(ChestHoleDamagePercent), ChestHoleDamagePercent, 10);
                ChestHoleSizePercent = Slider(nameof(ChestHoleSizePercent), ChestHoleSizePercent, 50);

                string? Value(string key) => old.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
                    ? property.GetString() : null;
                bool Switch(string key, bool fallback) => bool.TryParse(Value(key), out var value) ? value : fallback;
                int Slider(string key, int fallback, int minimum) =>
                    int.TryParse(Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    && value >= minimum && value <= 150 && value % 5 == 0 ? value : fallback;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }

        public ChestHoleRule ChestHoleRule => new(ChestHoles, ChestHoleLethalOnly, ChestHoleDamagePercent / 100f, ChestHoleSizePercent / 100f);
    }
}
