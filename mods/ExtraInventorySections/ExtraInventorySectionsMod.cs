using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.ExtraInventorySections;

/// <summary>
/// Port of Extra Inventory Sections (Genpretz, Nexus 1838), rebuilt from its description: Eyes, Hands and Neck equipment sections through the
/// engine's mod slots (<c>AddEquipmentSlot</c>), taking the ARMOUR and ITEM records of FCS slot ATTACH_EYES (4), ATTACH_GLOVES (10) and
/// ATTACH_NECK (11), so items stay a data mod. Their places in the character window are settings (read at start).
/// </summary>
public sealed class ExtraInventorySectionsMod : IMod
{
    const int AttachEyes = 4, AttachGloves = 10, AttachNeck = 11;

    public void Initialize(ModContext context)
    {
        var settings = new SectionSettings();
        context.AddSettings(settings);

        context.AddEquipmentSlot(new ModSlot("eyes", AttachEyes, 3, 1, settings.EyesX, settings.EyesY));
        context.AddEquipmentSlot(new ModSlot("hands", AttachGloves, 2, 2, settings.HandsX, settings.HandsY));
        context.AddEquipmentSlot(new ModSlot("neck", AttachNeck, 2, 2, settings.NeckX, settings.NeckY));
        if (settings.Labels)
        {
            context.AddFrame(frame => SectionLabels.UiScale = frame.UiScale);
            context.AddUi(new SectionLabels());
        }

        context.Log("Extra Inventory Sections initialized");
    }
}

/// <summary>Where each section sits, in layout pixels from the character window's top-left corner at UI scale 1 (takes effect after a restart).</summary>
public sealed class SectionSettings
{
    [Setting("Section labels", Hint = "Names each section above its cells. Restart to apply.")]
    public bool Labels { get; set; } = true;

    [Setting("Eyes X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesX { get; set; } = 282;

    [Setting("Eyes Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesY { get; set; } = 34;

    [Setting("Hands X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Hands")]
    public int HandsX { get; set; } = 282;

    [Setting("Hands Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Hands")]
    public int HandsY { get; set; } = 84;

    [Setting("Neck X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Neck")]
    public int NeckX { get; set; } = 362;

    [Setting("Neck Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Neck")]
    public int NeckY { get; set; } = 84;
}

/// <summary>
/// Each section's name just above its cells (<c>AddScreenControls</c> anchored to the section's widget, which the engine names by the section), in
/// the character and loot windows.
/// </summary>
sealed class SectionLabels : IUiExtension
{
    const float LabelHeight = 16;

    static readonly (string Section, string Caption)[] Sections = [("eyes", "Eyes"), ("hands", "Hands"), ("neck", "Neck")];
    static readonly string[] Screens = ["inventory", "loot"];

    /// <summary>The interface's scale, taken each frame.</summary>
    internal static volatile float UiScale = 1;

    string _style = "";

    public string Id => "dxseba.extrainventorysections.ui";

    public void Register(UiRegistration registration)
    {
        _style = registration.RegisterStyle("label", new UiStyle
        {
            FontName = "Kenshi_PaintedTextFont_Medium", TextAlign = "Left Bottom", Text = new(.84f, .80f, .70f, 1),
        });

        foreach (string screen in Screens)
        {
            foreach (var (section, caption) in Sections)
                registration.AddScreenControls(screen, section, UiAnchorPlacement.Above, _ => Label(section, caption));
        }
    }

    UiNode Label(string section, string caption)
    {
        float scale = UiScale;
        return new("label-" + section, UiNodeKind.Label)
        {
            Placement = new(UiLength.Fill, LabelHeight * scale, Bottom: 0), Text = caption, Style = _style, TextScale = scale * 0.8f,
            Pickable = false,
        };
    }
}
