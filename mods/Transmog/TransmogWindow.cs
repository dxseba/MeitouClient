using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Transmog;

/// <summary>
/// The transmog workbench of one character, opened from the button at the inventory's top (the original's icon button there): the switch, the
/// slots, a slot's appearances, a search of the items that fit it, and the chosen appearance's dye and model adjustment. Each change is sent to
/// the world as the character's whole outfit (applied at once, also while paused); the window edits its own copy meanwhile.
/// </summary>
sealed partial class TransmogWindow(ModContext context, TransmogSettings settings, Library library, string? icon) : IUiExtension
{
    /// <summary>The character the window is open on; none while closed.</summary>
    CharacterId _open = CharacterId.None;

    /// <summary>The outfit being edited (the world's when opened, then the window's changes).</summary>
    Outfit _draft = Outfit.Empty;

    string _slot = Slots.All[0].Id;
    int _layer = -1;
    string _search = "";
    string _hex1 = "", _hex2 = "";
    int _preset = -1;
    Layer? _copied;
    Vector2 _moved;

    /// <summary>The snapshot of the last frame (host thread).</summary>
    WorldSnapshot? _snapshot;

    /// <summary>The player's selection in the last frame.</summary>
    IReadOnlyList<CharacterId> _selected = [];

    /// <summary>What the right-hand list shows: the slot's items, the favorites among them, or the outfit presets.</summary>
    enum ListMode { Items, Favorites, Presets }

    ListMode _mode = ListMode.Items;
    string _presetName = "";

    /// <summary>The dye channel the colour picker sets (1 or 2).</summary>
    int _channel = 1;

    List<GameRecord>? _makers;

    /// <summary>The item lists of each slot, sorted by shown name, made on first use.</summary>
    readonly Dictionary<string, List<GameRecord>> _items = [];

    List<GameRecord>? _presets;

    public string Id => "dxseba.transmog.window";

    /// <summary>Once a frame: the snapshot the window reads, and closing when its character is gone.</summary>
    public void Frame(ModFrame frame)
    {
        _snapshot = frame.Snapshot;
        _selected = frame.Selected;
        Previews.Frame(frame);
        if (!_open.IsNone && Character() is null) _open = CharacterId.None;
    }

    CharacterSnapshot? Character() => _snapshot?.Characters.FirstOrDefault(c => c.Id == _open);

    #region Opening and sending

    void Open(CharacterSnapshot character)
    {
        _open = character.Id;
        _draft = TransmogSystem.Of(character.LastingId) ?? Outfit.Empty;
        SelectLayer(-1);
    }

    /// <summary>The edited outfit becomes the character's.</summary>
    void Send(Outfit outfit)
    {
        _draft = outfit;
        if (_open.IsNone) return;

        context.Enqueue(new SetOutfitOrder(_open, outfit) { Tick = context.Tick });
    }

    ImmutableList<Layer> Layers => _draft.Of(_slot);

    Layer? Chosen => _layer >= 0 && _layer < Layers.Count ? Layers[_layer] : null;

    void SelectLayer(int index)
    {
        _layer = index;
        _preset = -1;
        _hex1 = Chosen is { Dye1: >= 0 } a ? Hex(a.Dye1) : "";
        _hex2 = Chosen is { Dye2: >= 0 } b ? Hex(b.Dye2) : "";
    }

    void ReplaceChosen(Func<Layer, Layer> change)
    {
        if (Chosen is not { } layer) return;

        Send(_draft.With(_slot, Layers.SetItem(_layer, change(layer))));
    }

    #endregion

    #region Actions

    void Toggle() => Send(_draft with { On = !_draft.On });

    void ToggleWeapons() => Send(_draft with { WeaponsOn = !_draft.WeaponsOn });

    /// <summary>The outfit goes to every selected player character too (each is sent the whole outfit, as the window's own).</summary>
    void CopyToSelected()
    {
        var targets = _selected.Where(id => id != _open && _snapshot?.Characters.FirstOrDefault(c => c.Id == id)
            is { IsPlayer: true, Appearance: { Animal: null } }).ToList();
        foreach (var id in targets) context.Enqueue(new SetOutfitOrder(id, _draft) { Tick = context.Tick });
        Notify(context.Tr("Selected: {count}").Replace("{count}", targets.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    void ChooseMode(string mode) => _mode = Enum.TryParse<ListMode>(mode, out var m) ? m : ListMode.Items;

    void ToggleFavorite(string item)
    {
        string name = context.Data.Find(item)?.ShownName() ?? item;
        string text = library.ToggleFavorite(item) ? "Added {item} to favorites" : "Removed {item} from favorites";
        Notify(context.Tr(text).Replace("{item}", name, StringComparison.Ordinal));
    }

    void AddPreset()
    {
        string name = _presetName.Length > 0 ? _presetName
            : context.Tr("Preset {count}").Replace("{count}", (library.Presets.Count + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (!library.AddPreset(name, _draft))
        {
            Notify(context.Tr("Enter a non-empty name that is not already in use."));
            return;
        }

        _presetName = "";
    }

    /// <summary>A preset's slots and switches become the character's; its items the data lacks are left out with a notice.</summary>
    void ApplyPreset(int index)
    {
        if (index < 0 || index >= library.Presets.Count) return;

        var preset = library.Presets[index].Outfit;
        var missing = preset.Slots.Values.SelectMany(l => l).Where(l => l.Item.Length > 0 && context.Data.Find(l.Item) is null)
            .Select(l => l.Item).Distinct().ToList();
        if (missing.Count > 0) Notify(context.Tr("Preset appearances missing. Check these mods or item IDs: {sources}")
            .Replace("{sources}", string.Join(", ", missing.Take(4)), StringComparison.Ordinal));
        Send(preset);
        SelectLayer(Layers.Count > 0 ? 0 : -1);
    }

    void DeletePreset(int index) => library.DeletePreset(index);

    /// <summary>Debug mode's Spawn: a real item on the character, through the game's debug give-item command.</summary>
    void Spawn(string item)
    {
        if (!settings.DebugSpawn || _open.IsNone || context.Data.Find(item) is not { } record) return;

        context.Enqueue(new DebugGiveItem(_open, item, 1) { Tick = context.Tick });
        Notify(context.Tr("Spawned: {item}").Replace("{item}", record.ShownName(), StringComparison.Ordinal));
    }

    void ChooseSlot(string slot)
    {
        _slot = slot;
        _search = "";
        SelectLayer(Layers.Count > 0 ? 0 : -1);
    }

    /// <summary>An item from the list: a new appearance of the slot (a limb slot has one, replaced), the transmog turned on.</summary>
    void AddItem(string item)
    {
        bool limb = Slots.LimbOf(_slot) >= 0;
        if (!limb && Layers.Count >= Outfit.MaxLayers)
        {
            Notify(context.Tr("This slot already has 32 appearances"));
            return;
        }

        var layers = limb ? [new Layer(item)] : Layers.Add(new Layer(item));
        Send(_draft.With(_slot, layers) with { On = true });
        SelectLayer(layers.Count - 1);
    }

    void RemoveLayer(int index)
    {
        if (index < 0 || index >= Layers.Count) return;

        Send(_draft.With(_slot, Layers.RemoveAt(index)));
        SelectLayer(Math.Min(_layer, Layers.Count - 1));
    }

    void ClearSlot()
    {
        Send(_draft.With(_slot, []));
        SelectLayer(-1);
    }

    void ClearAll()
    {
        Send(_draft with { Slots = ImmutableDictionary<string, ImmutableList<Layer>>.Empty });
        SelectLayer(-1);
    }

    /// <summary>Copy equipped gear: every slot shows what the character wears there now (its prosthetics on its limbs).</summary>
    void CopyEquipped()
    {
        if (Character()?.ItemGrid is not { } grid) return;

        var outfit = _draft;
        int copied = 0;
        var held = grid.Items.Where(i => i.Section is "hip" or "back")
            .OrderBy(i => context.Data.Find(i.Record)?.Type == FcsRecordType.Crossbow ? 2 : i.Section == "hip" ? 0 : 1).ThenBy(i => i.Y).ToList();
        foreach (var (slot, _) in Slots.All)
        {
            string section = Slots.LimbOf(slot) is >= 0 and var k ? Data.Gameplay.Bodies.LimbReplacement.SlotOf(k) : slot;
            var item = Slots.WeaponOf(slot) is >= 0 and var w ? held.ElementAtOrDefault(w) : grid.Items.FirstOrDefault(i => i.Section == section);
            if (item is null) continue;

            outfit = outfit.With(slot, [new Layer(item.Record)]);
            copied++;
        }

        if (copied == 0)
        {
            Notify(context.Tr("No equipped gear to copy on this tab"));
            return;
        }

        Send(outfit with { On = true });
        SelectLayer(Layers.Count > 0 ? 0 : -1);
    }

    void Typed(string text) => _search = text;

    void Notify(string text) => _notice = (text, Environment.TickCount64 + 4000);

    (string Text, long Until) _notice = ("", 0);

    #endregion

    #region Dye

    void TypedHex(int channel, string text)
    {
        if (channel == 1) _hex1 = text;
        else _hex2 = text;

        if (ParseHex(text) is not { } colour) return;

        ReplaceChosen(l => channel == 1 ? l with { Dye1 = colour } : l with { Dye2 = colour });
    }

    /// <summary>The previous or next of the game's colour presets (COLOR_DATA), both channels set from it.</summary>
    void StepPreset(int step)
    {
        var presets = _presets ??= [.. context.Data.OfType(FcsRecordType.ColorData).OrderBy(r => r.ShownName(), StringComparer.CurrentCulture)];
        if (presets.Count == 0 || Chosen is null)
        {
            Notify(context.Tr("No colour presets"));
            return;
        }

        _preset = ((_preset < 0 ? (step > 0 ? -1 : 0) : _preset) + step + presets.Count) % presets.Count;
        var preset = presets[_preset];
        int c1 = preset.GetInt("color 1") & 0xFFFFFF, c2 = preset.GetInt("color 2") & 0xFFFFFF;
        (_hex1, _hex2) = (Hex(c1), Hex(c2));
        ReplaceChosen(l => l with { Dye1 = c1, Dye2 = c2 });
    }

    void ResetColour()
    {
        (_hex1, _hex2, _preset) = ("", "", -1);
        ReplaceChosen(l => l with { Dye1 = -1, Dye2 = -1 });
    }

    /// <summary>A colour from the picker into the picker's channel.</summary>
    void Pick(int colour)
    {
        if (_channel == 1) _hex1 = Hex(colour);
        else _hex2 = Hex(colour);

        ReplaceChosen(l => _channel == 1 ? l with { Dye1 = colour } : l with { Dye2 = colour });
    }

    static string Hex(int colour) => "#" + colour.ToString("X6", CultureInfo.InvariantCulture);

    static int? ParseHex(string text)
    {
        string digits = text.Trim().TrimStart('#');
        return digits.Length == 6 && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : null;
    }

    #endregion

    #region Model offset

    /// <summary>One step of an adjustment: an axis of the position (0.05 units) or rotation (5 degrees), or the scale (0.05).</summary>
    void Nudge(string argument)
    {
        if (argument.Length < 3) return;

        char what = argument[0];
        int axis = argument[1] - '0';
        float sign = argument[2] == '+' ? 1 : -1;
        ReplaceChosen(l => what switch
        {
            'p' => l with { Position = l.Position + Axis(axis) * 0.05f * sign },
            'r' => l with { Rotation = l.Rotation + Axis(axis) * 5 * sign },
            _ => l with { Scale = Math.Clamp(l.Scale + 0.05f * sign, 0.05f, 5) },
        });
    }

    static Vector3 Axis(int axis) => axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };

    void ResetOffset() => ReplaceChosen(l => l with { Position = Vector3.Zero, Rotation = Vector3.Zero, Scale = 1 });

    void Copy() => _copied = Chosen;

    void Paste()
    {
        if (_copied is not { } from) return;

        ReplaceChosen(l => l with { Dye1 = from.Dye1, Dye2 = from.Dye2, Position = from.Position, Rotation = from.Rotation, Scale = from.Scale });
        SelectLayer(_layer);
    }

    #endregion

    #region Weapon options

    void ToggleRendered() => ReplaceChosen(l => l with { Rendered = !l.Rendered });

    void ToggleOriginalFinish() => ReplaceChosen(l => l with { OriginalFinish = !l.OriginalFinish });

    /// <summary>The previous or next weapon manufacturer as the appearance's finish (the "Search manufacturer or grade" choice).</summary>
    void StepFinish(int step)
    {
        var makers = _makers ??= [.. context.Data.OfType(FcsRecordType.WeaponManufacturer).OrderBy(r => r.ShownName(), StringComparer.CurrentCulture)];
        if (makers.Count == 0 || Chosen is not { } layer) return;

        int at = makers.FindIndex(m => m.StringId == layer.Finish);
        int next = ((at < 0 ? (step > 0 ? -1 : 0) : at) + step + makers.Count) % makers.Count;
        ReplaceChosen(l => l with { Finish = makers[next].StringId, OriginalFinish = false });
    }

    #endregion

    #region Items

    /// <summary>The items that fit a slot: clothing of its section, bags, or the prosthetics of a limb.</summary>
    List<GameRecord> ItemsOf(string slot)
    {
        if (_items.TryGetValue(slot, out var list)) return list;

        int limb = Slots.LimbOf(slot);
        IEnumerable<GameRecord> fits = Slots.WeaponOf(slot) >= 0
            ? context.Data.OfType(FcsRecordType.Weapon).Concat(context.Data.OfType(FcsRecordType.Crossbow))
            : limb >= 0
            ? context.Data.OfType(FcsRecordType.LimbReplacement).Where(r => LookBuilder.FitsLimb(r, limb))
            : slot == Slots.Backpack
                ? context.Data.OfType(FcsRecordType.Container).Where(r => r.Filenames.ContainsKey("mesh") || r.Strings.ContainsKey("mesh"))
                : context.Data.OfType(FcsRecordType.Armour).Where(r => LookBuilder.SectionOf(r.GetInt("slot", 5)) == slot);
        list = [.. fits.OrderBy(r => r.ShownName(), StringComparer.CurrentCulture)];
        _items[slot] = list;
        return list;
    }

    /// <summary>The slot's items matching the search (name or string id), at most <see cref="MaxListed"/>.</summary>
    List<GameRecord> Matches() =>
        [.. ItemsOf(_slot).Where(r => _mode != ListMode.Favorites || library.Favorites.Contains(r.StringId)).Where(r => _search.Length == 0 || r.ShownName().Contains(_search, StringComparison.CurrentCultureIgnoreCase)
            || r.StringId.Contains(_search, StringComparison.OrdinalIgnoreCase)).Take(MaxListed)];

    const int MaxListed = 200;

    /// <summary>The shown name of an appearance's item ("Natural limb" for a limb slot's empty one).</summary>
    string NameOf(Layer layer) =>
        layer.Item.Length == 0 ? context.Tr("Natural limb") : context.Data.Find(layer.Item)?.ShownName() ?? layer.Item;

    /// <summary>Whether the chosen appearance takes a dye (its record is not "dont colorise", or the setting shows the controls always).</summary>
    bool Dyeable(Layer layer) =>
        settings.ShowAllDyeControls || context.Data.Find(layer.Item) is { } r
            && (r.Type is not (FcsRecordType.Armour or FcsRecordType.Container) || !r.GetBool("dont colorise", true));

    #endregion
}
