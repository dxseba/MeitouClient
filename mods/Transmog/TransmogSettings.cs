using Meitou.Modding;

namespace Meitou.Transmog;

/// <summary>
/// The original's settings tab (its <c>settings.json</c> keys <c>selectedSlotsOnly</c> and <c>showAllDyeControls</c>, <c>independentWeapons</c>)
/// and <c>debugSpawn</c> under Mod settings.
/// </summary>
public sealed class TransmogSettings
{
    [Setting("Enable independent weapon transmog",
        Hint = "When enabled, the Weapons tab controls body-mounted decorations separately. When disabled, decorations follow the main Transmog "
            + "switch.")]
    public bool IndependentWeapons { get; set; }

    [Setting("Only replace configured equipment slots",
        Hint = "When enabled, Transmog only takes over the appearance of slots with a configured transmog. When disabled, turning on Transmog "
            + "takes over the character's full appearance.")]
    public bool SelectedSlotsOnly { get; set; } = true;

    [Setting("Always show dye controls",
        Hint = "Show dye controls for all gear and limbs. When off, only dyeable items show these controls. The result depends on the item's "
            + "material.")]
    public bool ShowAllDyeControls { get; set; }

    [Setting("Debug mode",
        Hint = "Warning: This option lets you spawn real items and can severely disrupt game balance. Enable it only for debugging.")]
    public bool DebugSpawn { get; set; }
}
