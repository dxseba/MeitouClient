using Meitou.Content;
using Meitou.Data.Ui;
using Meitou.Modding;

namespace Meitou.ExtraInventorySections;

/// <summary>A section's place in the character window (layout pixels at UI scale 1 from its top-left corner) and its caption, or null.</summary>
sealed record SectionPlace(int X, int Y, string? Caption);

/// <summary>
/// The sections' places and captions read from the installed original's character window layout (the mod named
/// "Extra Inventory Sections Plugin" without Meitou code, Nexus 1838). Nothing of the original is shipped with the port.
/// </summary>
static class OriginalLayout
{
    const string OriginalName = "Extra Inventory Sections Plugin";
    const string LayoutFile = "gui/layout/Custom_InventoryCharacterWindow.layout";

    /// <summary>The reference viewport the places are measured at: UI scale 1 is a 1080-pixel-high view.</summary>
    const float ViewWidth = 1920, ViewHeight = 1080;

    /// <summary>The client area of the window skin <c>Kenshi_WindowCXP</c> (kenshi_templates.xml): inset 3 px each side, 41 px from the top.</summary>
    const float ClientLeft = 3, ClientTop = 41, ClientWidthInset = 6, ClientHeightInset = 45;

    /// <summary>
    /// The places by section widget name (<paramref name="sections"/>, each with the name of its caption widget); empty when the original is not
    /// installed or its layout cannot be read.
    /// </summary>
    public static Dictionary<string, SectionPlace> Read(ModContext context, IReadOnlyList<(string Widget, string Caption)> sections)
    {
        if (Find(context) is not { } path)
        {
            context.Log($"the original mod ({OriginalName}, Nexus 1838) is not installed: the sections use the places in the settings");
            return [];
        }

        try
        {
            return Places(MyGuiReader.ReadLayout(path), sections);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            context.Log($"the original's layout was not read ({e.Message}): the sections use the places in the settings");
            return [];
        }
    }

    static Dictionary<string, SectionPlace> Places(MyGuiLayout layout, IReadOnlyList<(string Widget, string Caption)> sections)
    {
        var result = new Dictionary<string, SectionPlace>(StringComparer.Ordinal);
        var window = layout.Roots.FirstOrDefault(w => w.Type == "Window");
        if (window is not { Skin: "Kenshi_WindowCXP", Children.Count: > 0 }) return result;

        var size = window.Position.RelativeTo(new(0, 0, ViewWidth, ViewHeight), window.RealPosition);
        var client = new UiRect(0, 0, size.Width - ClientWidthInset, size.Height - ClientHeightInset);
        var panel = window.Children[0];
        var panelRect = panel.Position.RelativeTo(client, panel.RealPosition);
        var inPanel = new UiRect(0, 0, panelRect.Width, panelRect.Height);

        foreach (var (name, captionName) in sections)
        {
            if (Named(panel, name) is not { } widget) continue;

            var at = widget.Position.RelativeTo(inPanel, widget.RealPosition);
            string? caption = Named(panel, captionName)?.Properties.GetValueOrDefault("Caption");
            result[name] = new((int)(ClientLeft + panelRect.X + at.X), (int)(ClientTop + panelRect.Y + at.Y), caption);
        }

        return result;
    }

    static MyGuiWidget? Named(MyGuiWidget parent, string name) => parent.Children.FirstOrDefault(c => c.Name == name);

    static string? Find(ModContext context)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            var original = ModList.Discover(install).FirstOrDefault(m => m.Name == OriginalName && !m.HasCode);
            string? path = original is null ? null : Path.Combine(original.Folder, LayoutFile);
            return path is not null && File.Exists(path) ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
