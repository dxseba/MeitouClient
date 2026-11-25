using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.DirectorPanel;

/// <summary>The rows backed by the Harmony patches and the game's internals: inventories, theft, colours, stance, poses, blood, opacity, the log.</summary>
internal sealed partial class DirectorWindow
{
    CharacterId _shown = CharacterId.None;
    bool _noTheft, _bloodbath, _separate = true, _customBlood;
    int _cut;
    Entry? _colour, _pose;
    readonly HashSet<CharacterId> _kept = [];

    /// <summary>The character whose inventory opens: the HUD's (an inspected NPC) when it is not the first selected, else the panel's.</summary>
    CharacterId InventoryTarget => !_shown.IsNone && (_selected.Count == 0 || _selected[0] != _shown) ? _shown : _target;

    IEnumerable<UiNode> InventoryRows()
    {
        yield return Heading("iv-h", "Character inventory:");
        yield return Hint("iv-hint", "Opens the normal inventory window for the character chosen above, the same one your own characters use. Pressing again closes it. The window itself");
        var line = Row("iv");
        var target = InventoryTarget;
        bool canOpen = _selected.Count > 0 && !target.IsNone && _selected[0] != target;
        line.Children.Add(Button("iv-open", texts["Open inventory"], () => Send(new OpenInventory(_selected[0], target, false) { Tick = context.Tick }), 220,
            canOpen));
        line.Children.Add(Button("iv-close", "X", () => Send(new OpenInventory(CharacterId.None, target, true) { Tick = context.Tick }), 60));
        line.Children.Add(Tick("iv-theft", "Moving items does not count as stealing", _noTheft, NoTheft));
        yield return line;
    }

    void NoTheft(bool on)
    {
        _noTheft = on;
        Send(new SetNoTheft(on) { Tick = context.Tick });
    }

    IEnumerable<UiNode> ColourRows()
    {
        yield return Heading("co-h", "Clothing colours:");
        foreach (var n in PickList("co", catalog.Colours, _colour?.Id, c => _colour = c, rows: 5)) yield return n;
        yield return Button("co-go", texts["Paint worn items"], PaintWorn, 220, !_target.IsNone);
    }

    void PaintWorn()
    {
        if (_colour is not { } colour)
        {
            _note = texts["Choose a colour scheme first"];
            return;
        }

        Send(new PaintWorn(_target, colour.Id) { Tick = context.Tick });
    }

    IEnumerable<UiNode> StanceRows()
    {
        yield return Heading("ws-h", "Weapon stance:");
        var line = Row("ws");
        line.Children.Add(Button("ws-draw", texts["Draw weapon"], () => ChangeStance(DirectorPanel.Stance.Draw), 160, !_target.IsNone));
        line.Children.Add(Button("ws-sheathe", texts["Sheathe weapon"], () => ChangeStance(DirectorPanel.Stance.Sheathe), 160, !_target.IsNone));
        line.Children.Add(Button("ws-leave", texts["Leave combat"], () => ChangeStance(DirectorPanel.Stance.LeaveCombat), 160, !_target.IsNone));
        yield return line;
        yield return Tick("ws-keep", "Keep weapon ready", _kept.Contains(_target), Keep);
    }

    void ChangeStance(Stance stance)
    {
        if (stance != DirectorPanel.Stance.Draw) _kept.Remove(_target);
        Send(new SetStance(_target, stance) { Tick = context.Tick });
    }

    void Keep(bool on)
    {
        if (on) _kept.Add(_target);
        else _kept.Remove(_target);
        Send(new KeepReady(_target, on) { Tick = context.Tick });
    }

    IEnumerable<UiNode> PoseRows()
    {
        yield return Heading("po-h", "Idle pose:");
        yield return Hint("po-hint", "These are the same poses the editor shows. The game may change the pose on its own once the character starts doing something.");
        foreach (var n in PickList("po", catalog.Poses, _pose?.Id, p => _pose = p, rows: 6)) yield return n;
        yield return Button("po-go", texts["Set pose"], () => Send(new SetPose(_target, _pose!.Id) { Tick = context.Tick }), 200,
            _pose is not null && !_target.IsNone);
    }

    IEnumerable<UiNode> BloodRows()
    {
        yield return Label("ef-cut", texts["Cut type:"]);
        yield return Choices("ef-cuts1", DirectorSystem.SplashCuts[..4], _cut, c => _cut = c);
        yield return Choices("ef-cuts2", DirectorSystem.SplashCuts[4..], _cut - 4, c => _cut = 4 + c);
        yield return Tick("ef-bath", "\"Bloodbath\" mode", _bloodbath, Bloodbath);
        yield return Hint("ef-bathhint", "Adds more blood to bladed carnage");

        yield return Heading("ef-bb", "Blood on body:");
        yield return Hint("ef-bbhint", "0 - clean, 1 - as if all health is lost, 2 - the body part is fully covered. Wounds are not changed.");
        var line = Row("ef-bbset");
        line.Children.Add(Tick("ef-custom", "Custom body blood", _customBlood, CustomBlood, 260));
        line.Children.Add(Field("ef-bbval", 80, "1"));
        line.Children.Add(Button("ef-bbgo", texts["Apply"], () => CustomBlood(true), 120));
        yield return line;
    }

    void Bloodbath(bool on)
    {
        _bloodbath = on;
        Send(new SetBloodbath(on) { Tick = context.Tick });
    }

    /// <summary>The body's drawn blood (display only): the value over 0 to 1 a part; the original's 2 (fully covered) draws as 1, Meitou's most.</summary>
    void CustomBlood(bool on)
    {
        _customBlood = on;
        if (on) Patches.BodyBlood[_target] = Math.Clamp(Number("ef-bbval", 1), 0, 2);
        else Patches.BodyBlood.TryRemove(_target, out _);
    }

    IEnumerable<UiNode> OpacityRows()
    {
        yield return Heading("op-h", "Text above characters:");
        yield return Hint("op-hint", "For filming: 100% - as in the game, 0% - completely hidden. Conversations and actions go on as usual, only the look changes.");
        yield return OpacityRow("op-speech", "Speech above heads:", Patches.SpeechOpacity, v => Patches.SpeechOpacity = v);
        yield return OpacityRow("op-pop", "Pop-up text (cats, work):", Patches.FloaterOpacity, v => Patches.FloaterOpacity = v);
        yield return Button("op-log", texts["Write data to the log"], WriteLog, 260);
    }

    UiNode OpacityRow(string id, string english, float now, Action<float> set)
    {
        var line = Row(id);
        line.Children.Add(Label(id + "-l", texts[english] + " " + (now * 100).ToString("0", CultureInfo.InvariantCulture) + "%", width: 300));
        line.Children.Add(Field(id + "-v", 70, "100"));
        line.Children.Add(Button(id + "-go", texts["Apply"], () => set(Math.Clamp(Number(id + "-v", 100), 0, 100) / 100), 120));
        return line;
    }

    /// <summary>"Write data to the log": the panel's lists and character as the mod's log lines (the original's diagnostics for its author).</summary>
    void WriteLog()
    {
        context.Log($"panel lists: {catalog.Items.Count} items, {catalog.Makers.Count} makers, {catalog.People.Count} people, "
            + $"{catalog.Animals.Count} animals, {catalog.Factions.Count} factions, {catalog.Research.Count} research, {catalog.Packages.Count} packages, "
            + $"{catalog.Effects.Count} effects, {catalog.Colours.Count} colours, {catalog.Poses.Count} poses, {points.All.Count} points");
        if (Target is { } t)
            context.Log($"panel character: {t.Name} ({t.Id.Slot}.{t.Id.Generation}), faction {t.Faction}, squad {t.Squad}, at {t.Position}, "
                + $"packages [{string.Join(", ", t.Packages)}], bounty {t.Bounty}");
        _note = texts["Written. Send me the log."];
    }
}
