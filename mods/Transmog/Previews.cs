using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.Transmog;

/// <summary>
/// The workbench's item pictures: the game's own inventory icons (<see cref="ItemIcons.Choose"/>: a saved icon file, or the picture the renderer
/// draws of the item's mesh). A file goes on an image node as is; a rendered picture has a texture name an image node cannot carry, so a postfix
/// on <c>UiHost.Control</c> gives the preview nodes (ids in <see cref="Rendered"/>) that texture, flipped as the game's item cells are. A picture
/// is shown only once it is ready (asking for it starts the render); until then the row shows its name only. Host thread only.
/// </summary>
static class Previews
{
    /// <summary>The rendered picture of each preview node shown this frame, by node id.</summary>
    static readonly ConcurrentDictionary<string, string> Rendered = new();

    static ModContext? s_context;
    static int s_faulted;
    static Func<ModFrame, object?>? s_player;
    static FieldInfo? s_painter;
    static PropertyInfo? s_cells;
    static FieldInfo? s_icons;

    static UiPainter? s_livePainter;
    static ItemIcons? s_liveIcons;

    public static void Install(ModContext context)
    {
        s_context = context;
        var host = AccessTools.Field(typeof(ModFrame), "_host");
        var playerField = host is null ? null : AccessTools.Field(host.FieldType, "Player");
        var player = playerField?.FieldType;
        s_painter = player is null ? null : AccessTools.Field(player, "UiPainter");
        s_cells = player is null ? null : AccessTools.Property(player, "ItemCells");
        s_icons = s_cells is null ? null : AccessTools.Field(s_cells.PropertyType, "ItemIcons");
        var control = AccessTools.Method(typeof(UiHost), "Control");
        if (host is null || playerField is null || s_painter is null || s_icons is null || control is null)
        {
            context.LogError("the interface's item icons were not found: the workbench lists names without pictures");
            return;
        }

        s_player = frame => playerField.GetValue(host.GetValue(frame));
        context.Harmony.Patch(control, postfix: new HarmonyMethod(typeof(Previews), nameof(ControlPostfix)));
    }

    /// <summary>Once a frame: the painter and the icon chooser of the interface now shown.</summary>
    public static void Frame(ModFrame frame)
    {
        try
        {
            if (s_player?.Invoke(frame) is not { } player) return;

            s_livePainter = s_painter!.GetValue(player) as UiPainter;
            s_liveIcons = s_cells!.GetValue(player) is { } cells ? s_icons!.GetValue(cells) as ItemIcons : null;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// The picture node of item <paramref name="record"/> (dyed <paramref name="colour"/>, a COLOR_DATA id or empty) with id <paramref name="id"/>
    /// at <paramref name="placement"/>; null while it has none ready.
    /// </summary>
    public static UiNode? Node(string id, string record, UiPlacement placement, string colour = "")
    {
        Rendered.TryRemove(id, out _);
        if (s_livePainter is not { } painter || s_liveIcons is not { } icons || record.Length == 0) return null;

        var choice = icons.Choose(record, "", colour);
        if (choice is { Source: ItemIconSource.File, File: { } file })
            return painter.IconReady(file) ? new UiNode(id, UiNodeKind.Image) { Image = file, Placement = placement } : null;

        if (choice.Source != ItemIconSource.Render) return null;

        string texture = UiPainter.RenderedIconTexture(record, "", choice.Colour);
        if (!painter.RenderedIconReady(texture)) return null;

        Rendered[id] = texture;
        return new UiNode(id, UiNodeKind.Image) { Placement = placement };
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void ControlPostfix(UiNode node, ref UiControl __result)
    {
        try
        {
            if (node.Kind != UiNodeKind.Image || !Rendered.TryGetValue(node.Id, out var texture)) return;

            var properties = new Dictionary<string, string>(__result.Properties) { ["ImageTexture"] = texture, ["ImageFlipV"] = "true" };
            __result = __result with { Properties = properties };
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"an item picture failed, names are shown: {e}");
    }
}
