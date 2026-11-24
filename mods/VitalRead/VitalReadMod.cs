using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// Port of Vital Read (MCM fork): a badge on each squad portrait naming the character's worst state (PD, DY, RC, ZZ, CrL, CrA, ST, and the fork's
/// bed states), drawn into the portrait bar by <see cref="PortraitBadges"/>.
/// </summary>
public sealed class VitalReadMod : IMod
{
    internal static readonly VitalReadSettings Settings = new();

    /// <summary>The running world's system, for the badges' facts; null before a world starts.</summary>
    internal static volatile VitalSystem? Running;

    public void Initialize(ModContext context)
    {
        AddTranslations(context);
        context.AddSettings(Settings);
        context.AddSystem("vitals", _ => Running = new VitalSystem(Settings, context.Population), SystemAnchor.End);
        PortraitBadges.Install(context);
        context.Log("Vital Read initialized");
    }

    /// <summary>
    /// The original's gettext catalogues (<c>vital_read</c>: en_GB, ru_RU, zh_CN), read from the installed original, for the settings' texts.
    /// </summary>
    static void AddTranslations(ModContext context)
    {
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("vital_read", original);
    }
}
