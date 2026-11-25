using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QuestsContracts;

/// <summary>
/// The interface side of the overlay (H, held), the readout (the original's F10, unbound here: F10 is Meitou's debug menu) and the menu's
/// Pickpocket row. Interface thread only: the overlay's marks come from <see cref="LedgerSystem.Marks"/>, drawn over the heads each frame.
/// </summary>
sealed class LedgerOverlay(ModContext context, LedgerTables tables) : IUiExtension
{
    const float Label = 40, Line = 18, ReadoutWidth = 420, Face = 96;
    const double HoldEverySeconds = 0.25;

    ModKeyAction? _key;
    bool _held, _readout;
    double _sent;
    List<(Vector2 At, string Text)> _captions = [];

    public string Id => "dxseba.questscontracts.overlay";

    public void Bind(ModKeyAction overlay) => _key = overlay;

    public void ToggleReadout() => _readout = !_readout;

    /// <summary>Each frame: the held key tells the world the ground under the camera; the marks are projected over the heads.</summary>
    public void Frame(ModFrame frame)
    {
        bool held = _key?.Held == true;
        double now = Environment.TickCount64 / 1000.0;
        if (held && (!_held || now - _sent >= HoldEverySeconds) || !held && _held)
        {
            context.Enqueue(new OverlayHeld(held, frame.CameraTarget) { Tick = frame.Tick });
            _sent = now;
        }

        _held = held;
        _captions = held ? Project(frame) : [];
    }

    List<(Vector2, string)> Project(ModFrame frame)
    {
        var marks = LedgerSystem.Marks;
        var captions = new List<(Vector2, string)>();
        foreach (var c in frame.Snapshot.Characters)
        {
            if (!marks.TryGetValue(c.Id, out var kind)) continue;

            string text = kind switch { MarkKind.Target => tables.MarkTarget, MarkKind.Giver => tables.MarkGiver, _ => tables.MarkTalker };
            if (frame.Project(c.Position + new Vector3(0, c.TagHeight, 0)) is { } at) captions.Add((at, text));
        }

        return captions;
    }

    public void Register(UiRegistration registration)
    {
        string mark = registration.RegisterStyle("mark", new() { Text = new(1, .85f, .3f, 1) });
        string panel = registration.RegisterStyle("panel", new() { Background = new(.05f, .07f, .1f, .85f), Text = new(.92f, .95f, 1, 1) });
        registration.Contribute(UiSlots.Overlay, _ => Marks(mark), order: 59);
        registration.Contribute(UiSlots.Overlay, _ => ReadoutPanel(panel), order: 58);
        registration.AddCharacterMenuVerbs(Rows);
        registration.AddMapMarkers(_ => MapDots());
    }

    UiNode Marks(string style)
    {
        var root = new UiNode("qc-marks") { Placement = UiPlacement.Fill, Pickable = false, Animated = _captions.Count > 0 };
        for (int i = 0; i < _captions.Count; i++)
        {
            var (at, text) = _captions[i];
            root.Children.Add(new UiNode($"qc-mark{i}", UiNodeKind.Label)
            {
                Placement = new(Label, Line, Left: at.X - Label / 2, Top: at.Y - Line),
                Text = text, Style = style, Pickable = false, TextScale = 1.4f,
            });
        }

        return root;
    }

    UiNode ReadoutPanel(string style)
    {
        if (!_readout || LedgerSystem.Running?.View is not { } view) return new UiNode("qc-readout-hidden") { Visible = false };

        var body = new UiNode("qc-readout")
        {
            Placement = new(ReadoutWidth, 8 + view.Readout.Count * (Line + 1) + (view.Faces.Count > 0 ? Face + 4 : 0), Right: 12, Top: 60),
            Style = style, Flow = UiFlow.Column, Padding = 4, Pickable = false,
        };
        for (int i = 0; i < view.Readout.Count; i++)
        {
            var line = new UiNode($"qc-ro{i}", UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = view.Readout[i], Style = style };
            body.Children.Add(line);
        }

        if (view.Faces.Count > 0) body.Children.Add(Faces(view.Faces));
        return body;
    }

    /// <summary>The locked targets' live portraits (the original paints them with the game's portrait manager).</summary>
    static UiNode Faces(IReadOnlyList<CharacterId> faces)
    {
        var row = new UiNode("qc-faces") { Placement = new(UiLength.Fill, Face), Flow = UiFlow.Row, Gap = 4, Pickable = false };
        for (int i = 0; i < faces.Count; i++)
            row.Children.Add(new UiNode($"qc-face{i}", UiNodeKind.Image) { Placement = new(Face, Face), PortraitOf = faces[i], Pickable = false });

        return row;
    }

    /// <summary>The quest dots on the world map: a person's at the target, a camp's at the guild's town.</summary>
    static IEnumerable<UiMapMarker> MapDots()
    {
        if (LedgerSystem.Running?.View is not { } view) yield break;

        foreach (var (at, label, person) in view.Dots)
            yield return new UiMapMarker(at, person ? "Character" : "Camp", new Vector4(1, .8f, .2f, 1), label, "Quests & Contracts");
    }

    /// <summary>Pickpocket on a standing, conscious man who is not ours (the world checks that the first selected character sneaks).</summary>
    IEnumerable<UiMenuVerb> Rows(UiMenuContext menu)
    {
        if (menu.Selected.Count == 0 || menu.Target is not { IsPlayer: false, Imprisoned: false, Body: { Dead: false, Unconscious: false } } mark)
            yield break;

        var thief = menu.Selected[0];
        if (thief == mark.Id) yield break;

        yield return new UiMenuVerb(context.Tr("Pickpocket"), () => menu.Enqueue(new PickpocketOrder(thief, mark.Id) { Tick = menu.Tick }));
    }
}
