using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.ExtraInventorySections;

/// <summary>
/// Port of Extra Inventory Sections (Genpretz, Nexus 1838): the original's five inventory sections through the engine's mod slots
/// (<c>AddEquipmentSlot</c>): eyes (FCS slot 4), hands (10, up to four items) and neck (11), worn by generated NPCs from their clothing lists,
/// and a second hat (3) and belt (14) filled by hand, in the installed original's character window layout (else the vanilla one at the
/// settings' places).
/// </summary>
public sealed class ExtraInventorySectionsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SectionSettings();
        context.AddSettings(settings);

        var widgets = Sections.All.Select(s => (s.Section, s.CaptionWidget)).ToList();
        var original = settings.OriginalPlaces ? OriginalLayout.Read(context, widgets) : [];
        bool replaced = settings.OriginalPlaces && ReplaceWindow(context);
        var captions = AddSlots(context, settings, original);

        if (settings.Labels && !replaced)
        {
            context.AddFrame(frame => SectionLabels.UiScale = frame.UiScale);
            context.AddUi(new SectionLabels(captions));
        }

        context.Log("Extra Inventory Sections initialized");
    }

    /// <summary>Adds the five sections; answers the captions of those that have one.</summary>
    static List<(string Section, string Caption)> AddSlots(ModContext context, SectionSettings settings, Dictionary<string, SectionPlace> original)
    {
        var captions = new List<(string Section, string Caption)>();
        foreach (var section in Sections.All)
        {
            var (x, y) = section.Fallback(settings);
            var place = original.GetValueOrDefault(section.Section);
            context.AddEquipmentSlot(new ModSlot(section.Section, section.FcsSlot, section.Width, section.Height, place?.X ?? x, place?.Y ?? y)
            {
                MaxItems = section.MaxItems, Secondary = section.Secondary, RollFromClothing = !section.Secondary,
            });

            if (section.Caption is { } fallback) captions.Add((section.Section, place?.Caption ?? fallback));
        }

        return captions;
    }

    /// <summary>
    /// Replaces the character window's layout with the installed original's, as the original's <c>BaseLayout::initialise</c> hook does; the
    /// slots then take its widgets' places and the layout carries the captions. False without the original.
    /// </summary>
    static bool ReplaceWindow(ModContext context)
    {
        if (OriginalLayout.LayoutPath(context) is not { } path) return false;

        context.AddLayoutReplacement("Kenshi_InventoryCharacterWindow.layout", path);
        return true;
    }
}

/// <summary>
/// One section as the original creates it: its name (as saves store it and its layout names the widget), FCS slot and size in cells (the
/// original's DLL), its caption widget in the original's layout and a caption of the port's own when that layout is missing.
/// </summary>
sealed record Sections(
    string Section, int FcsSlot, int Width, int Height, int MaxItems, bool Secondary, string CaptionWidget, string? Caption,
    Func<SectionSettings, (int, int)> Fallback)
{
    public static readonly Sections[] All =
    [
        new("eyes_eyes", 4, 3, 1, 1, false, "lbEyes", "EYES", s => (s.EyesX, s.EyesY)),
        new("eyes_belts", 14, 3, 1, 1, true, "", null, s => (s.BeltsX, s.BeltsY)),
        new("eyes_hats", 3, 3, 1, 1, true, "", null, s => (s.HatsX, s.HatsY)),
        new("gloves", 10, 2, 2, 4, false, "lbGloves", "HANDS", s => (s.HandsX, s.HandsY)),
        new("neck", 11, 2, 2, 1, false, "lbNeck", "NECK", s => (s.NeckX, s.NeckY)),
    ];
}

/// <summary>Where each section sits, in layout pixels from the character window's top-left corner at UI scale 1 (takes effect after a restart).</summary>
public sealed class SectionSettings
{
    [Setting("Original's window",
        Hint = "Uses the installed original's character window layout, with its places and captions; off, or without it, the vanilla window "
            + "and the places below. Restart to apply.")]
    public bool OriginalPlaces { get; set; } = true;

    [Setting("Section labels", Hint = "Names each section above its cells. Restart to apply.")]
    public bool Labels { get; set; } = true;

    [Setting("Eyes X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesX { get; set; } = 280;

    [Setting("Eyes Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int EyesY { get; set; } = 240;

    [Setting("Second belt X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int BeltsX { get; set; } = 280;

    [Setting("Second belt Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int BeltsY { get; set; } = 267;

    [Setting("Second hat X", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int HatsX { get; set; } = 280;

    [Setting("Second hat Y", Hint = "Restart to apply.", Min = 0, Max = 800, SubGroup = "Eyes")]
    public int HatsY { get; set; } = 294;

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
