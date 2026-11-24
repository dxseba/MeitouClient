using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.ExtraInventorySections;

/// <summary>
/// Port of Extra Inventory Sections (Genpretz, Nexus 1838): the original's eyes, gloves and neck inventory sections through the engine's mod slots
/// (<c>AddEquipmentSlot</c>), taking the ARMOUR and ITEM records of FCS slot ATTACH_EYES (4), ATTACH_GLOVES (10) and ATTACH_NECK (11), at the
/// places and with the captions of the installed original's character window layout (else the settings' places).
/// </summary>
public sealed class ExtraInventorySectionsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SectionSettings();
        context.AddSettings(settings);

        var widgets = Sections.All.Select(s => (s.Section, s.CaptionWidget)).ToList();
        var original = settings.OriginalPlaces ? OriginalLayout.Read(context, widgets) : [];
        var captions = new List<(string Section, string Caption)>();
        foreach (var section in Sections.All)
        {
            var (x, y) = section.Fallback(settings);
            var place = original.GetValueOrDefault(section.Section);
            context.AddEquipmentSlot(new ModSlot(section.Section, section.FcsSlot, section.Width, section.Height, place?.X ?? x, place?.Y ?? y));
            captions.Add((section.Section, place?.Caption ?? section.Caption));
        }

        if (settings.Labels)
        {
            context.AddFrame(frame => SectionLabels.UiScale = frame.UiScale);
            context.AddUi(new SectionLabels(captions));
        }

        context.Log("Extra Inventory Sections initialized");
    }
}

/// <summary>
/// One section as the original creates it: its name (as saves store it and its layout names the widget), FCS slot and size in cells (the
/// original's DLL), its caption widget in the original's layout and a caption of the port's own when that layout is missing.
/// </summary>
sealed record Sections(
    string Section, int FcsSlot, int Width, int Height, string CaptionWidget, string Caption, Func<SectionSettings, (int, int)> Fallback)
{
    public static readonly Sections[] All =
    [
        new("eyes_eyes", 4, 3, 1, "lbEyes", "EYES", s => (s.EyesX, s.EyesY)),
        new("gloves", 10, 2, 2, "lbGloves", "HANDS", s => (s.HandsX, s.HandsY)),
        new("neck", 11, 2, 2, "lbNeck", "NECK", s => (s.NeckX, s.NeckY)),
    ];
}

/// <summary>Where each section sits, in layout pixels from the character window's top-left corner at UI scale 1 (takes effect after a restart).</summary>
public sealed class SectionSettings
{
    [Setting("Original's places",
        Hint = "Places and names the sections as the installed original's layout does; off, or without it, the places below. Restart to apply.")]
    public bool OriginalPlaces { get; set; } = true;

    [Setting("Section labels", Hint = "Names each section above its cells. Restart to apply.")]
    public bool Labels { get; set; } = true;

    [Setting("Eyes X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesX { get; set; } = 280;

    [Setting("Eyes Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesY { get; set; } = 240;

    [Setting("Hands X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Hands")]
    public int HandsX { get; set; } = 362;

    [Setting("Hands Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Hands")]
    public int HandsY { get; set; } = 240;

    [Setting("Neck X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Neck")]
    public int NeckX { get; set; } = 424;

    [Setting("Neck Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Neck")]
    public int NeckY { get; set; } = 240;
}

/// <summary>
/// Each section's caption just above its cells, centred (<c>AddScreenControls</c> anchored to the section's widget, which the engine names by the
/// section), in the character and loot windows, in the look of the game's <c>Kenshi_TextboxPaintedText</c> labels.
/// </summary>
sealed class SectionLabels(IReadOnlyList<(string Section, string Caption)> captions) : IUiExtension
{
    const float LabelHeight = 16;

    static readonly string[] Screens = ["inventory", "loot"];

    /// <summary>The interface's scale, taken each frame.</summary>
    internal static volatile float UiScale = 1;

    string _style = "";

    public string Id => "dxseba.extrainventorysections.ui";

    public void Register(UiRegistration registration)
    {
        // The template's font and text colour (kenshi_templates.xml); the original's labels set no colour of their own.
        _style = registration.RegisterStyle("label", new UiStyle
        {
            FontName = "Kenshi_PaintedTextFont_Medium", TextAlign = "Center", Text = new(.4f, .380392f, .356863f, 1),
        });

        foreach (string screen in Screens)
        {
            foreach (var (section, caption) in captions)
                registration.AddScreenControls(screen, section, UiAnchorPlacement.Above, _ => Label(section, caption));
        }
    }

    UiNode Label(string section, string caption)
    {
        float scale = UiScale;
        return new("label-" + section, UiNodeKind.Label)
        {
            Placement = new(UiLength.Fill, LabelHeight * scale, Bottom: 0), Text = caption, Style = _style, TextScale = scale, Pickable = false,
        };
    }
}
