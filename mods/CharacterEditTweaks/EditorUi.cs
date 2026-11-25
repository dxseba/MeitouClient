using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Simulation.Editing;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// The editor's added controls, at the original's places (sizes are its pixels at 1080 lines, scaled with the name panel): CHANGES and CAMERA just
/// above CLOTHES; the changes window (Undo list, Redo label and list) right of the name panel, hidden until CHANGES; the camera height bar right of
/// the name panel, shown until CAMERA hides it; in play, Cancel on the right half of CONFIRM. Ctrl+Z undoes and Ctrl+Shift+Z redoes while the
/// editor is open.
/// </summary>
sealed class EditorUi(Func<string, string> tr) : IUiExtension
{
    const string Screen = "editor";

    /// <summary>The name panel's height at 1080 lines (0.398148 of the window), the original's pixel unit.</summary>
    const float PanelPixels = 430;

    /// <summary>The height bar's positions (the original's 161, 80 the middle) and the lift of one (units).</summary>
    const int HeightSteps = 80;
    const float HeightPerStep = 0.125f;

    /// <summary>How many lines of each list show.</summary>
    const int Lines = 5;

    static readonly Vector4 UndoColour = new(1, 0.5f, 0.5f, 1), RedoColour = new(0.5f, 1, 0.5f, 1), Lit = new(1, 0.85f, 0.45f, 1);

    string _button = "", _panel = "", _line = "", _first = "", _track = "", _thumb = "";
    string _changes = "", _camera = "", _cancel = "", _undo = "", _redo = "", _height = "";

    bool _showChanges;
    bool _showCamera = true;

    /// <summary>The height bar's track on the screen (as last laid out) and its position.</summary>
    UiRect _trackRect;
    int _position = HeightSteps;

    /// <summary>What the camera's height was set for: a reset follows a change of tab or character, as the original's category update.</summary>
    (CharacterDraft? Draft, EditorTab Tab) _seen;

    public string Id => "dxseba.characteredittweaks.ui";

    public void Register(UiRegistration registration)
    {
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_FloatingPanelSkin" });
        _line = registration.RegisterStyle("line", new() { TextScale = 0.8f });
        _first = registration.RegisterStyle("first", new() { TextScale = 0.8f, Background = new(1, 1, 1, 0.12f), Hover = new(1, 1, 1, 0.22f) });
        _track = registration.RegisterStyle("track", new() { Background = new(0, 0, 0, 0.45f) });
        _thumb = registration.RegisterStyle("thumb", new() { Background = new(0.78f, 0.7f, 0.52f, 0.95f) });

        _changes = registration.RegisterAction("changes", _ => _showChanges = !_showChanges);
        _camera = registration.RegisterAction("camera", _ => _showCamera = !_showCamera);
        _cancel = registration.RegisterAction("cancel", _ => Cancel());
        _undo = registration.RegisterAction("undo", _ => Step(redo: false));
        _redo = registration.RegisterAction("redo", _ => Step(redo: true));
        _height = registration.RegisterAction("height", Drag);

        registration.AddScreenControls(Screen, "ClothesButton", UiAnchorPlacement.Above, Toggles);
        registration.AddScreenControls(Screen, "NamePanel", UiAnchorPlacement.Right, Side);
        registration.AddScreenControls(Screen, "ConfirmPanel", UiAnchorPlacement.Inside, CancelButton);
        registration.AddScreenOverrides(Screen, "ConfirmButton", HalveConfirm);
        registration.AddKeyInput(Keys);
    }

    static bool InPlay => EditorAccess.View() is { Kind: not EditKind.Creation, Draft: not null };

    /// <summary>CHANGES and CAMERA side by side just above CLOTHES (the original: 2 pixels up, each half CLOTHES' width plus 2, CAMERA 4 less).</summary>
    UiNode? Toggles(UiScreenContext context)
    {
        if (EditorAccess.View() is not { Draft: not null }) return null;

        float half = context.Anchor.Width / 2;
        var root = new UiNode("cet-toggles") { Pickable = false };
        root.Children.Add(Toggle("cet-changes", tr("Changes"), _changes, _showChanges, new(half + 2, UiLength.Fill, Top: -2)));
        root.Children.Add(Toggle("cet-camera", tr("Camera"), _camera, _showCamera, new(half - 2, UiLength.Fill, Left: half + 2, Top: -2)));
        return root;
    }

    UiNode Toggle(string id, string caption, string action, bool on, UiPlacement placement) => new(id, UiNodeKind.Button)
    {
        Placement = placement, Text = caption.ToUpperInvariant(), Style = _button, Action = action, TextColour = on ? Lit : null,
    };

    /// <summary>Right of the name panel: the height bar and, when CHANGES is on, the changes window.</summary>
    UiNode? Side(UiScreenContext context)
    {
        if (EditorAccess.View() is not { Draft: { } draft } view) return null;

        ResetHeightOnChange(draft, view.Tab);
        float k = context.Anchor.Height / PanelPixels;
        var root = new UiNode("cet-side") { Pickable = false };
        if (_showCamera) root.Children.Add(HeightBar(context.Anchor, k));
        if (_showChanges) root.Children.Add(ChangesWindow(k));
        return root;
    }

    void ResetHeightOnChange(CharacterDraft draft, EditorTab tab)
    {
        if (_seen == (draft, tab)) return;

        _seen = (draft, tab);
        _position = HeightSteps;
        CameraPatches.Height = 0;
    }

    /// <summary>The original's <c>Kenshi_SliderVBig</c> bar: 2 right of the panel, 10 down, 32 wide, the panel's height less 24; top lifts the camera.</summary>
    UiNode HeightBar(UiRect anchor, float k)
    {
        float top = 10 * k, height = anchor.Height - 24 * k, width = 32 * k;
        _trackRect = new UiRect(anchor.X + 2 * k, anchor.Y + top, width, height);
        float thumb = Math.Max(12 * k, height / 12);
        float at = (height - thumb) * _position / (2f * HeightSteps);
        var bar = new UiNode("cet-height", UiNodeKind.Panel)
        {
            Placement = new(width, height, Left: 2 * k, Top: top), Style = _track, Action = _height,
            Pointer = UiPointerEvents.Drag | UiPointerEvents.Release,
        };
        bar.Children.Add(new UiNode("cet-height-thumb", UiNodeKind.Panel) { Placement = new(width, thumb, Top: at), Style = _thumb, Pickable = false });
        return bar;
    }

    /// <summary>A press or drag on the height bar: the position under the pointer (0 at the top, 160 at the bottom) sets the rig's lift.</summary>
    void Drag(UiActionContext action)
    {
        if (_trackRect.Height <= 0) return;

        float share = Math.Clamp((action.Position.Y - _trackRect.Y) / _trackRect.Height, 0, 1);
        _position = (int)MathF.Round(share * 2 * HeightSteps);
        CameraPatches.Height = (HeightSteps - _position) * HeightPerStep;
    }

    /// <summary>The changes window: 240 x 268 at 32 right of the panel and 4 down; Undo (red) over its list, Redo (green) over the other.</summary>
    UiNode ChangesWindow(float k)
    {
        var window = new UiNode("cet-window") { Placement = new(240 * k, 268 * k, Left: 32 * k, Top: 4 * k), Style = _panel };
        window.Children.Add(Title("cet-undo-title", tr("Undo"), UndoColour, 6 * k, k));
        window.Children.Add(List("cet-undo", ChangeTracker.Undo, _undo, 28 * k, k));
        window.Children.Add(Title("cet-redo-title", tr("Redo"), RedoColour, 134 * k, k));
        window.Children.Add(List("cet-redo", ChangeTracker.Redo, _redo, 158 * k, k));
        return window;
    }

    static UiNode Title(string id, string text, Vector4 colour, float top, float k) => new(id, UiNodeKind.Label)
    {
        Placement = new(UiLength.Fill, 22 * k, Top: top), Text = text, TextColour = colour,
    };

    /// <summary>A list's newest lines; the top one (the original's selected line) undoes or redoes when clicked.</summary>
    UiNode List(string id, IReadOnlyList<Change> changes, string action, float top, float k)
    {
        var list = new UiNode(id) { Placement = new(228 * k, 100 * k, Left: 6 * k, Top: top), Style = _track, Pickable = false };
        float line = 100 * k / Lines;
        for (int i = 0; i < Math.Min(Lines, changes.Count); i++)
        {
            bool last = i == Lines - 1 && changes.Count > Lines;
            string text = last ? $"+{changes.Count - i}" : changes[i].Text(tr);
            list.Children.Add(new UiNode($"{id}-{i}", i == 0 ? UiNodeKind.Button : UiNodeKind.Label)
            {
                Placement = new(UiLength.Fill, line, Top: i * line), Text = " " + text, Style = i == 0 ? _first : _line,
                Action = i == 0 ? action : "",
            });
        }

        return list;
    }

    /// <summary>In play, Cancel on the right half of CONFIRM's place in its panel (the original halves CONFIRM and puts Cancel beside it).</summary>
    UiNode? CancelButton(UiScreenContext context)
    {
        if (!InPlay || !EditorAccess.ConfirmReady) return null;

        // CONFIRM's place in its panel (the layout's position_real), its right half.
        const float left = 0.0645161f, top = 0.192308f, width = 0.870968f, height = 0.615385f;
        return new UiNode("cet-cancel", UiNodeKind.Button)
        {
            Placement = new(new UiLength(0, width / 2), new UiLength(0, height), new UiLength(0, left + width / 2), new UiLength(0, top)),
            Text = tr("Cancel").ToUpperInvariant(), Style = _button, Action = _cancel,
        };
    }

    static UiWidgetOverride? HalveConfirm(UiScreenContext context)
    {
        if (!InPlay || !EditorAccess.ConfirmReady) return null;

        var at = context.Anchor;
        return new UiWidgetOverride { Bounds = at with { Width = at.Width / 2 } };
    }

    /// <summary>Ctrl+Z undoes, Ctrl+Shift+Z redoes; with the editor open the key is taken either way (as the original's key hook).</summary>
    static bool Keys(UiKeyInput input)
    {
        if (input.Key != Key.Z || !input.Ctrl || input.Alt || !input.OpenScreens.Contains(Screen)) return false;

        Step(redo: input.Shift);
        return true;
    }

    static void Step(bool redo)
    {
        if (EditorAccess.View() is { Draft: { } draft }) ChangeTracker.Step(draft, redo);
    }

    /// <summary>
    /// Cancel: every draft's look goes back to what it was when the editor made it, then CONFIRM is pressed, as the original undoes every change
    /// and confirms.
    /// </summary>
    static void Cancel()
    {
        foreach (var draft in EditorAccess.Drafts())
        {
            if (ChangeTracker.StartOf(draft) is { } start) DraftRecords.Restore(draft, start);
        }

        EditorAccess.Confirm();
    }
}
