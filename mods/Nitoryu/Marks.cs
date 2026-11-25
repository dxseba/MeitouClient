using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Nitoryu;

/// <summary>What one character's marks show: the flow left (0: none), a Mugen chain waiting, and the pressure stacks on it (as an enemy).</summary>
readonly record struct MarkState(float Flow, bool Chain, int Pressure, bool Full, bool Player);

/// <summary>
/// The marks over the heads (README "How to use it", <c>[Marks]</c>): a wave with a bar that runs out for flow, a loop for a Mugen chain waiting,
/// pips over an enemy for pressure stacks (red at full). <c>marks</c> 0 none, 1 the selected characters (and the enemies they pressure), 2 all
/// the player's, 3 every fighter. The serial update publishes the state (<see cref="Publish"/>); a frame callback projects it and an overlay draws
/// text marks (the original's are pictures). Display only.
/// </summary>
sealed class Marks(int mode) : IUiExtension
{
    const float Height = 24;

    static readonly Vector4 Gold = new(1, 0.82f, 0.35f, 1), Red = new(1, 0.3f, 0.25f, 1), Blue = new(0.55f, 0.8f, 1, 1);

    public static readonly ConcurrentDictionary<CharacterId, MarkState> States = new();

    IReadOnlyList<(Vector2 At, MarkState State)> _shown = [];
    string _style = "";

    public string Id => "dxseba.nitoryu.marks";

    public static void Publish(CharacterId id, MarkState state)
    {
        if (state.Flow > 0 || state.Chain || state.Pressure > 0) States[id] = state;
        else States.TryRemove(id, out _);
    }

    public void Register(UiRegistration registration)
    {
        _style = registration.RegisterStyle("mark", new UiStyle { TextAlign = "Center", FontName = "Kenshi_StandardFont_Medium" });
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 41);
    }

    public void Frame(ModFrame frame)
    {
        if (mode <= 0 || States.IsEmpty)
        {
            _shown = [];
            return;
        }

        var selected = frame.Selected.ToHashSet();
        var list = new List<(Vector2, MarkState)>();
        foreach (var c in frame.Snapshot.Characters)
        {
            if (!States.TryGetValue(c.Id, out var state) || !Shows(state, c, selected)) continue;
            if (frame.Project(c.Position + new Vector3(0, Height, 0)) is { } at) list.Add((at, state));
        }

        _shown = list;
    }

    /// <summary>By <c>marks</c>: the selected (an enemy's pressure while any is selected), the player's, or everyone.</summary>
    bool Shows(MarkState state, CharacterSnapshot c, HashSet<CharacterId> selected) => mode switch
    {
        1 => selected.Contains(c.Id) || state.Pressure > 0 && selected.Count > 0,
        2 => c.IsPlayer || state.Pressure > 0,
        _ => true,
    };

    UiNode Build()
    {
        var shown = _shown;
        var root = new UiNode("nitoryu-marks") { Placement = UiPlacement.Fill, Pickable = false, Animated = shown.Count > 0 };
        for (int i = 0; i < shown.Count; i++)
        {
            var (at, s) = shown[i];
            string text = (s.Flow > 0 ? "~" + new string('|', (int)MathF.Ceiling(s.Flow * 4)) + " " : "") + (s.Chain ? "(o) " : "")
                + new string('*', s.Pressure);
            var colour = s.Pressure > 0 ? s.Full ? Red : Gold : s.Chain ? Blue : Gold;
            root.Children.Add(new UiNode("nitoryu-mark-" + i, UiNodeKind.Label)
            {
                Placement = new UiPlacement(120, 20, Left: at.X - 60, Top: at.Y - 10),
                Text = text.Trim(),
                Style = _style,
                TextColour = colour,
                Pickable = false,
            });
        }

        return root;
    }
}
