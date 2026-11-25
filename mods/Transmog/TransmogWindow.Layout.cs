using System.Globalization;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.Transmog;

sealed partial class TransmogWindow
{
    const float Width = 900, Height = 690, Row = 26, Gap = 6, SlotWidth = 150, Thumb = 24;

    string _panel = "", _title = "", _text = "", _dim = "", _chosen = "", _noticeStyle = "";
    string _close = "", _drag = "", _openAction = "", _toggle = "", _toggleWeapons = "", _slotAction = "", _layerAction = "", _remove = "",
        _clearSlot = "", _clearAll = "", _copyEquipped = "", _copySelected = "", _typed = "", _add = "", _favorite = "", _modeAction = "",
        _presetTyped = "", _presetAdd = "", _presetApply = "", _presetDelete = "", _hexTyped1 = "", _hexTyped2 = "", _presetStep = "",
        _resetColour = "", _channelAction = "", _pick = "", _nudge = "", _resetOffset = "", _copy = "", _paste = "", _rendered = "",
        _originalFinish = "", _finishStep = "", _spawn = "";

    public void Register(UiRegistration registration)
    {
        RegisterStyles(registration);
        RegisterActions(registration);
        RegisterEditorActions(registration);
        registration.Contribute(UiSlots.Overlay, Build, order: 64);
        registration.AddScreenControls("inventory", "ArrangeButton", UiAnchorPlacement.Left, InventoryButton);
    }

    void RegisterStyles(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _title = registration.RegisterStyle("title", new() { Text = new(.92f, .86f, .74f, 1), FontName = "Kenshi_PaintedTextFont_Medium" });
        _text = registration.RegisterStyle("text", new() { Text = new(.90f, .86f, .76f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        _dim = registration.RegisterStyle("dim", new() { Text = new(.70f, .65f, .56f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        _chosen = registration.RegisterStyle("chosen", new() { Text = new(1, .95f, .8f, 1), Background = new(1, 1, 1, .18f) });
        _noticeStyle = registration.RegisterStyle("notice", new() { Text = new(1, .7f, .45f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        RegisterPalette(registration);
    }

    void RegisterActions(UiRegistration r)
    {
        _close = r.RegisterAction("close", _ => _open = Simulation.CharacterId.None);
        _drag = r.RegisterAction("drag", c => _moved += c.Kind == UiEventKind.Drag ? c.Delta : default);
        _openAction = r.RegisterAction("open", _ => OpenFromInventory());
        _toggle = r.RegisterAction("toggle", _ => Toggle());
        _toggleWeapons = r.RegisterAction("toggle-weapons", _ => ToggleWeapons());
        _slotAction = r.RegisterAction("slot", c => ChooseSlot(c.Argument));
        _layerAction = r.RegisterAction("layer", c => SelectLayer(Index(c.Argument)));
        _remove = r.RegisterAction("remove", c => RemoveLayer(Index(c.Argument)));
        _clearSlot = r.RegisterAction("clear-slot", _ => ClearSlot());
        _clearAll = r.RegisterAction("clear-all", _ => ClearAll());
        _copyEquipped = r.RegisterAction("copy-equipped", _ => CopyEquipped());
        _copySelected = r.RegisterAction("copy-selected", _ => CopyToSelected());
        _typed = r.RegisterAction("search", c => Typed(c.Text));
        _add = r.RegisterAction("add", c => AddItem(c.Argument));
        _favorite = r.RegisterAction("favorite", c => ToggleFavorite(c.Argument));
        _modeAction = r.RegisterAction("mode", c => ChooseMode(c.Argument));
        _presetTyped = r.RegisterAction("preset-name", c => _presetName = c.Text);
        _presetAdd = r.RegisterAction("preset-add", _ => AddPreset());
        _presetApply = r.RegisterAction("preset-apply", c => ApplyPreset(Index(c.Argument)));
        _presetDelete = r.RegisterAction("preset-delete", c => DeletePreset(Index(c.Argument)));
        _spawn = r.RegisterAction("spawn", c => Spawn(c.Argument));
    }

    static int Index(string argument) => int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : -1;

    #region Inventory button

    Simulation.CharacterSnapshot? _inventoryCharacter;

    /// <summary>The button left of ARRANGE in a player character's (human) inventory: the original's icon when installed, else a text button.</summary>
    UiNode? InventoryButton(UiScreenContext screen)
    {
        if (screen.Character is not { IsPlayer: true, Appearance: { Animal: null } } character) return null;

        _inventoryCharacter = character;
        float side = screen.Anchor.Height;
        var root = new UiNode("tm-inv") { Placement = UiPlacement.Fill, Pickable = false };
        var button = new UiNode("tm-inv-button", UiNodeKind.Button)
        {
            Placement = new(icon is null ? 90 : side, side, Right: 4), Text = icon is null ? "Transmog" : "", Action = _openAction,
        };
        if (icon is not null) button.Children.Add(new UiNode("tm-inv-icon", UiNodeKind.Image) { Image = icon, Placement = UiPlacement.Fill });
        root.Children.Add(button);
        return root;
    }

    void OpenFromInventory()
    {
        if (_inventoryCharacter is { } character) Open(character);
    }

    #endregion

    #region Window

    UiNode Build(UiFrameContext frame)
    {
        if (_open.IsNone || Character() is not { } character) return new UiNode("tm-hidden") { Visible = false };

        var root = new UiNode("tm") { Placement = UiPlacement.Fill, Pickable = false };
        var window = new UiNode("tm-window")
        {
            Placement = new(Width, Height, Left: (frame.Viewport.Width - Width) / 2 + _moved.X, Top: (frame.Viewport.Height - Height) / 2 + _moved.Y),
            Style = _panel,
        };
        Header(window, character);
        SlotList(window);
        float x = SlotWidth + 20;
        LayerList(window, x);
        RightList(window, x + 250);
        Editor(window, x, 370);
        root.Children.Add(window);
        return root;
    }

    void Header(UiNode window, Simulation.CharacterSnapshot character)
    {
        window.Children.Add(new UiNode("tm-title", UiNodeKind.Label)
        {
            Placement = new(Width - 60, Row, Left: 10, Top: 8), Text = "TRANSMOG  -  " + character.Name, Style = _title, Pickable = true,
            Pointer = UiPointerEvents.Drag, Action = _drag,
        });
        window.Children.Add(Button("tm-close", "X", Width - 38, 8, 28, Row - 2, _close));
        float y = 8 + Row + Gap, x = 10;
        window.Children.Add(Button("tm-toggle", _draft.On ? "Transmog: on" : "Transmog: off", x, y, 150, Row, _toggle));
        x += 156;
        if (settings.IndependentWeapons)
        {
            string weapons = _draft.WeaponsOn ? "Weapon transmog: ON" : "Weapon transmog: OFF";
            window.Children.Add(Button("tm-toggle-weapons", weapons, x, y, 190, Row, _toggleWeapons));
            x += 196;
        }

        window.Children.Add(Button("tm-copy-equipped", "Copy equipped gear", x, y, 170, Row, _copyEquipped));
        window.Children.Add(Button("tm-clear-all", "Clear all gear", x + 176, y, 140, Row, _clearAll));
        window.Children.Add(Button("tm-copy-selected", "Copy to selected characters", x + 322, y, 220, Row, _copySelected));
        if (_notice.Until > Environment.TickCount64) window.Children.Add(Text("tm-notice", _notice.Text, 10, Height - Row - 6, Width - 20, _noticeStyle));
    }

    const float Top = 8 + 2 * (Row + Gap);

    void SlotList(UiNode window)
    {
        float y = Top;
        foreach (var (id, caption) in Slots.All)
        {
            string label = _draft.Of(id).Count is > 0 and var n ? $"{context.Tr(caption)} ({n})" : context.Tr(caption);
            var node = Button($"tm-slot-{id}", label, 10, y, SlotWidth, Row, _slotAction, id);
            window.Children.Add(id == _slot ? Restyle(node, _chosen) : node);
            y += Row + 2;
        }
    }

    void LayerList(UiNode window, float x)
    {
        var layers = Layers;
        window.Children.Add(Text("tm-layers", context.Tr("Added: {count}").Replace("{count}", layers.Count.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal), x, Top, 240, _text));
        var list = new UiNode("tm-layer-list", UiNodeKind.ScrollView)
        {
            Placement = new(240, 220, Left: x, Top: Top + Row), Flow = UiFlow.Column, ScrollStep = Row,
        };
        for (int i = 0; i < layers.Count; i++) list.Children.Add(LayerRow(i, layers[i]));
        window.Children.Add(list);
        window.Children.Add(Button("tm-clear-slot", "Clear slot", x, Top + Row + 226, 240, Row, _clearSlot));
    }

    UiNode LayerRow(int i, Layer layer)
    {
        string arg = i.ToString(CultureInfo.InvariantCulture);
        var row = new UiNode($"tm-layer{i}") { Placement = new(UiLength.Percent(100), Row) };
        if (Previews.Node($"tm-layer{i}-pic", layer.Item, new(Thumb, Thumb, Left: 0, Top: 1)) is { } picture) row.Children.Add(picture);
        var name = Button($"tm-layer{i}-name", NameOf(layer), Thumb + 2, 0, 170, Row, _layerAction, arg);
        row.Children.Add(i == _layer ? Restyle(name, _chosen) : name);
        row.Children.Add(Button($"tm-layer{i}-x", "X", Thumb + 176, 0, 28, Row, _remove, arg));
        return row;
    }

    #endregion

    #region Nodes

    static UiNode Text(string id, string text, float x, float y, float w, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(w, Row, Left: x, Top: y), Text = text, Style = style };

    static UiNode Button(string id, string text, float x, float y, float w, float h, string action, string argument = "") =>
        new(id, UiNodeKind.Button) { Placement = new(w, h, Left: x, Top: y), Text = text, Action = action, Argument = argument };

    static UiNode Restyle(UiNode node, string style) =>
        new(node.Id, UiNodeKind.Button) { Placement = node.Placement, Text = node.Text, Action = node.Action, Argument = node.Argument, Style = style };

    #endregion
}
