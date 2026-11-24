using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.BestMinimap;

/// <summary>
/// The information strip under the map (<c>InfoScale</c>, <c>InfoClock</c>, <c>InfoCoordinates</c>, <c>InfoHeading</c>, in the original's order
/// and formats: <c>Zoom:%s</c> with 2000 / Range as a percentage, <c>PC time:%s</c> as HH:MM, <c>X:%d Z:%d</c> in metres of ten units,
/// <c>Dir:%03d</c> clockwise from map north; packed into centred rows by measured width, at least 100 pixels wide, hidden with every field off)
/// and the tooltip over a hovered town or character (name, faction and relations, squad, bounty).
/// </summary>
sealed partial class MinimapUi
{
    const float InfoMinWidth = 100, InfoPad = 12, InfoRow = 22;

    void AddInfo(UiNode root, Projection v, UiFrameContext frame)
    {
        var fields = InfoFields();
        if (fields.Count == 0) return;

        float width = MathF.Max(InfoMinWidth, v.Rect.Width);
        var rows = PackRows(fields, width - InfoPad * 2, frame);
        var r = new UiRect(v.Rect.X + (v.Rect.Width - width) / 2, v.Rect.Y + v.Rect.Height + 4, width, rows.Count * InfoRow + 8);
        var strip = new UiNode("bm-info") { Style = _panel, Pickable = true, Placement = At(r) };
        for (int i = 0; i < rows.Count; i++)
        {
            strip.Children.Add(new UiNode($"bm-info{i}", UiNodeKind.Label)
            {
                Text = rows[i], Style = _label, Pickable = false, Placement = new(width, InfoRow, Left: 0, Top: 4 + i * InfoRow),
            });
        }

        root.Children.Add(strip);
    }

    List<string> InfoFields()
    {
        var s = Minimap.Settings;
        var fields = new List<string>();
        var invariant = CultureInfo.InvariantCulture;
        if (s.InfoScale) fields.Add(tr("Zoom:%s").Replace("%s", (2000f / s.Range * 100).ToString("0", invariant) + "%"));
        if (s.InfoClock) fields.Add(tr("PC time:%s").Replace("%s", DateTime.Now.ToString("HH:mm", invariant)));
        if (s.InfoCoordinates)
        {
            var c = Minimap.View.Centre / 10;
            fields.Add(string.Create(invariant, $"X:{(int)c.X} Z:{(int)c.Y}"));
        }

        if (s.InfoHeading)
        {
            int degrees = ((int)MathF.Round(Minimap.HeadingAngle * 180 / MathF.PI) % 360 + 360) % 360;
            fields.Add(tr("Dir:%03d").Replace("%03d", degrees.ToString("000", invariant)));
        }

        return fields;
    }

    static List<string> PackRows(List<string> fields, float width, UiFrameContext frame)
    {
        var rows = new List<string>();
        string row = "";
        foreach (string f in fields)
        {
            string joined = row.Length == 0 ? f : row + "   " + f;
            if (row.Length > 0 && frame.Measure(joined).X > width)
            {
                rows.Add(row);
                joined = f;
            }

            row = joined;
        }

        if (row.Length > 0) rows.Add(row);
        return rows;
    }

    /// <summary>The tooltip of the town or character under the pointer.</summary>
    void AddTooltip(UiNode root, Projection v, UiFrameContext frame)
    {
        if (!Minimap.Settings.Tooltips || !v.Inside(Minimap.Pointer) || TipText() is not { Length: > 0 } text) return;

        var size = frame.Measure(text) + new Vector2(14, 10);
        var p = Minimap.Pointer + new Vector2(16, 16);
        if (p.X + size.X > Minimap.ScreenSize.X) p.X = Minimap.Pointer.X - size.X - 4;
        if (p.Y + size.Y > Minimap.ScreenSize.Y) p.Y = Minimap.Pointer.Y - size.Y - 4;
        var box = new UiRect(p.X, p.Y, size.X, size.Y);
        root.Children.Add(new UiNode("bm-tip", UiNodeKind.Label) { Text = text, Style = _tip, Pickable = false, Placement = At(box) });
    }

    string? TipText()
    {
        var pointer = Minimap.Pointer;
        foreach (var d in _dots)
        {
            if (Vector2.Distance(d.Screen, pointer) <= MathF.Max(4, d.Size / 2 + 2)) return CharacterTip(d.Character);
        }

        foreach (var p in _places)
        {
            if (Vector2.Distance(p.Screen, pointer) <= p.Size / 2) return PlaceTip(p);
        }

        return null;
    }

    string CharacterTip(Simulation.CharacterSnapshot c)
    {
        var lines = new List<string> { c.Name };
        if (FactionLine(c.Faction) is { } faction) lines.Add(faction);
        if (Minimap.Snapshot?.PlayerSquads.FirstOrDefault(s => s.Members.Contains(c.Id)) is { Name.Length: > 0 } squad) lines.Add(squad.Name);
        if (Bounty(c) is > 0 and var bounty) lines.Add($"{bounty} c");
        return string.Join("\n", lines);
    }

    string PlaceTip(Place p)
    {
        int faction = p.Faction is { } id && Minimap.Population is { } data ? data.FactionIndex(id) : -1;
        return FactionLine(faction) is { } line ? $"{p.Name}\n{line}" : p.Name;
    }

    /// <summary>"Faction (relation, ally / neutral / hostile / your faction)"; null for none.</summary>
    string? FactionLine(int faction)
    {
        if (faction < 0 || Minimap.Population is not { } data || faction >= data.Factions.Count) return null;

        int mine = Minimap.PlayerFaction;
        string name = data.ShownFactionName(faction);
        if (mine < 0) return name;
        if (mine == faction) return $"{name} ({tr("your faction")})";

        float relation = data.Relations.Get(mine, faction);
        string word = data.Relations.IsAlly(mine, faction) ? tr("ally") : relation < -30 ? tr("hostile") : tr("neutral");
        return string.Create(CultureInfo.InvariantCulture, $"{name} ({relation:0}, {word})");
    }
}
