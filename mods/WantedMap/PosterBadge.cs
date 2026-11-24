using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.WantedMap;

/// <summary>
/// The poster badge on an item cell (the original's <c>InventoryIcon</c> hook, <b>Verified</b> original DLL (decompiled)): nothing until a target
/// is dead or captive; then "done/total" in the cell's lower part while some are left, and once all are, <c>wantedmap_captive.png</c> (any
/// captive) or <c>wantedmap_done.png</c> in its lower left corner, a side of 2/5 of the cell's smaller side held to 14 to 28 px. Without the
/// picture there is no badge, as in the original. The pictures are the original's, read from its installed folder (<see cref="OriginalFiles"/>).
/// </summary>
sealed class PosterBadge(OriginalFiles files)
{
    /// <summary>
    /// The count's colour (the DLL's constants 1, 0.42, 0.36).
    /// </summary>
    static readonly Vector4 CountColour = new(1, 0.42f, 0.36f, 1);

    readonly Dictionary<string, string?> _pictures = new(StringComparer.Ordinal);

    public UiNode? Build(UiScreenContext context)
    {
        if (!WantedMapMod.Settings.Enabled || context.Item is not { } cell || WantedMapMod.Running is not { } system) return null;
        if (!system.Progress.TryGetValue(cell.Item.Record, out var p) || p.Done == 0) return null;

        var size = context.Anchor;
        return p.Done < p.Total ? Count(p, size.Width, size.Height) : Picture(p, size.Width, size.Height);
    }

    /// <summary>
    /// The count: a text box 3 px in, 18 px high, its top 20 px above the cell's bottom (the DLL's coordinates).
    /// </summary>
    static UiNode Count(PosterProgress p, float width, float height) => new("count", UiNodeKind.Label)
    {
        Placement = new UiPlacement(Math.Max(0, width - 6), 18, 3, height - 20),
        Text = $"{p.Done}/{p.Total}",
        TextColour = CountColour,
    };

    UiNode? Picture(PosterProgress p, float width, float height)
    {
        if (Find(p.Captive > 0 ? "wantedmap_captive.png" : "wantedmap_done.png") is not { } file) return null;

        float side = Math.Clamp(MathF.Floor(Math.Min(width, height) * 2 / 5), 14, 28);
        return new UiNode("done", UiNodeKind.Image) { Placement = new UiPlacement(side, side, 2, height - side - 2), Image = file };
    }

    /// <summary>
    /// The picture in a folder's <c>gui/gfx</c> or the folder itself; null when absent.
    /// </summary>
    string? Find(string name)
    {
        if (_pictures.TryGetValue(name, out var known)) return known;

        return _pictures[name] = files.Find(Path.Combine("gui", "gfx", name), name);
    }
}
