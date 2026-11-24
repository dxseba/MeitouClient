using Meitou.Modding;

namespace Meitou.BodyMorphCap;

/// <summary>
/// Port of BodyMorphCap (regulareverydaynormalmazaf, Workshop 3800922376): a character's body is drawn as if Strength, the two smithing skills
/// (bulk) and Dexterity, Martial Arts, Swimming and Athletics (muscle definition) were no higher than their caps. Only the drawn body changes; the
/// stats do not. A Harmony postfix on the publish of the body (<see cref="BodyPatch"/>) rebuilds it from the capped stats.
/// </summary>
public sealed class BodyMorphCapMod : IMod
{
    internal static readonly BodyMorphCapSettings Settings = new();

    public void Initialize(ModContext context)
    {
        var group = context.AddSettings(Settings);
        group.Changed += _ => BodyPatch.SettingsChanged();
        OriginalLabels.Add(context, Settings.Language);

        BodyPatch.Install(context);
        context.Log($"BodyMorphCap initialized (caps: strength {Settings.Cap}, weapon smith {Settings.CapWeaponSmith}, "
            + $"armour smith {Settings.CapArmourSmith}, definition {Settings.CapMuscle})");
    }
}

/// <summary>
/// The original's <c>BodyMorphCap.config</c> keys and its Mod Hub rows, with the shipped defaults (README "Facts"). Labels and hints are the original's
/// English texts, so its Russian ones apply through <see cref="OriginalLabels"/>.
/// </summary>
public sealed class BodyMorphCapSettings
{
    [Setting("Enabled", Hint = "Turn the whole mod on/off.")]
    public bool Enabled { get; set; } = true;

    [Setting("Strength cap (body)", Hint = "Body morph only: render the body as if Strength were at most this. Gameplay is unchanged.",
        Min = 1, Max = 1000, Step = 1)]
    public float Cap { get; set; } = 100;

    [Setting("Weapon smith cap (body)", Hint = "Body morph only: build also grows with Weapon Smithing. Render as if it were at most this.",
        Min = 1, Max = 1000, Step = 1)]
    public float CapWeaponSmith { get; set; } = 100;

    [Setting("Armour smith cap (body)", Hint = "Body morph only: build also grows with Armour Smithing. Render as if it were at most this.",
        Min = 1, Max = 1000, Step = 1)]
    public float CapArmourSmith { get; set; } = 100;

    [Setting("Muscle definition cap (body)",
        Hint = "Body morph only: muscle definition (abs) grows with Dexterity/Martial arts/Swimming/Athletics. Render as if those were at most this.",
        Min = 1, Max = 1000, Step = 1)]
    public float CapMuscle { get; set; } = 100;

    [Setting("Apply to player", Hint = "Also cap the player's own body.")]
    public bool ApplyToPlayer { get; set; } = true;

    [Setting("Menu language", Hint = "auto follows the game; ru shows the original's Russian labels. Takes effect at the next start.",
        Options = ["auto", "en", "ru"])]
    public string Language { get; set; } = "auto";

    [Setting("Debug log", Hint = "Write the first clamped bodies to the mod log.")]
    public bool Debug { get; set; }
}
