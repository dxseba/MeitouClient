using System.Globalization;
using Meitou.Data;
using Meitou.Game.Ui;

namespace Meitou.Transmog;

/// <summary>The right-hand list: the slot's items (all or the favorites), each with its picture and favorite star, or the outfit presets.</summary>
sealed partial class TransmogWindow
{
    void RightList(UiNode window, float x)
    {
        float w = Width - x - 12;
        ModeButton(window, "tm-mode-items", "Equipment", ListMode.Items, x, 110);
        ModeButton(window, "tm-mode-fav", "Favorites", ListMode.Favorites, x + 114, 110);
        ModeButton(window, "tm-mode-presets", "Presets", ListMode.Presets, x + 228, 110);
        if (_mode == ListMode.Presets) PresetList(window, x, w);
        else ItemList(window, x, w);
    }

    void ModeButton(UiNode window, string id, string caption, ListMode mode, float x, float w)
    {
        var node = Button(id, caption, x, Top, w, Row, _modeAction, mode.ToString());
        window.Children.Add(_mode == mode ? Restyle(node, _chosen) : node);
    }

    void ItemList(UiNode window, float x, float w)
    {
        float y = Top + Row + 4;
        window.Children.Add(Text("tm-search-label", "Search", x, y, 70, _text));
        window.Children.Add(new UiNode("tm-search", UiNodeKind.TextBox)
        {
            Placement = new(w - 70, Row, Left: x + 70, Top: y), Text = _search, Action = _typed, MaxLength = 48,
        });
        var matches = Matches();
        var list = new UiNode("tm-items", UiNodeKind.ScrollView)
        {
            Placement = new(w, 196, Left: x, Top: y + Row + 4), Flow = UiFlow.Column, ScrollStep = Row,
        };
        if (Slots.LimbOf(_slot) >= 0 && _mode == ListMode.Items) list.Children.Add(ItemRow("tm-item-natural", null, w, false));
        for (int i = 0; i < matches.Count; i++) list.Children.Add(ItemRow($"tm-item{i}", matches[i], w, i < MaxPictures));
        if (matches.Count == 0)
        {
            string none = _mode == ListMode.Favorites ? "No matching favorites" : "No matching items";
            list.Children.Add(Text("tm-item-none", none, 0, 0, 300, _dim));
        }

        window.Children.Add(list);
    }

    /// <summary>An item's row: picture, name (a click adds it to the slot) and the favorite star; null is a limb's natural limb.</summary>
    /// <summary>How many rows get a picture (each asks the renderer for one the first time).</summary>
    const int MaxPictures = 60;

    UiNode ItemRow(string id, GameRecord? record, float w, bool picture)
    {
        var row = new UiNode(id) { Placement = new(UiLength.Percent(100), Row) };
        string item = record?.StringId ?? "";
        if (picture && record is not null && Previews.Node(id + "-pic", item, new(Thumb, Thumb, Left: 0, Top: 1)) is { } pic) row.Children.Add(pic);

        string caption = record?.ShownName() ?? context.Tr("Natural limb");
        bool spawn = settings.DebugSpawn && record is not null;
        row.Children.Add(Button(id + "-name", caption, Thumb + 2, 0, w - Thumb - (spawn ? 124 : 52), Row, _add, item));
        if (record is null) return row;

        if (spawn) row.Children.Add(Button(id + "-spawn", "Spawn", w - 118, 0, 70, Row, _spawn, item));

        bool favourite = library.Favorites.Contains(item);
        var star = Button(id + "-star", favourite ? "*" : "+", w - 46, 0, 28, Row, _favorite, item);
        row.Children.Add(favourite ? Restyle(star, _chosen) : star);
        return row;
    }

    void PresetList(UiNode window, float x, float w)
    {
        float y = Top + Row + 4;
        window.Children.Add(new UiNode("tm-preset-name", UiNodeKind.TextBox)
        {
            Placement = new(w - 120, Row, Left: x, Top: y), Text = _presetName, Action = _presetTyped, MaxLength = 64,
        });
        window.Children.Add(Button("tm-preset-add", "Add preset", x + w - 114, y, 114, Row, _presetAdd));
        var list = new UiNode("tm-presets", UiNodeKind.ScrollView)
        {
            Placement = new(w, 196, Left: x, Top: y + Row + 4), Flow = UiFlow.Column, ScrollStep = Row,
        };
        var presets = library.Presets;
        for (int i = 0; i < presets.Count; i++) list.Children.Add(PresetRow(i, presets[i].Name, w));
        if (presets.Count == 0) list.Children.Add(Text("tm-preset-none", "No presets", 0, 0, 300, _dim));
        window.Children.Add(list);
    }

    UiNode PresetRow(int i, string name, float w)
    {
        string arg = i.ToString(CultureInfo.InvariantCulture);
        var row = new UiNode($"tm-preset{i}") { Placement = new(UiLength.Percent(100), Row) };
        row.Children.Add(Text($"tm-preset{i}-name", name, 0, 0, w - 200, _text));
        row.Children.Add(Button($"tm-preset{i}-apply", "Apply", w - 196, 0, 90, Row, _presetApply, arg));
        row.Children.Add(Button($"tm-preset{i}-delete", "Delete", w - 102, 0, 84, Row, _presetDelete, arg));
        return row;
    }
}
