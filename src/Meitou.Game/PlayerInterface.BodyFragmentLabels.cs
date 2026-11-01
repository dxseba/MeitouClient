using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Engine.Input;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    public Func<IReadOnlyList<BodyFragmentLabel>>? BodyFragmentLabels { get; set; }
    readonly List<(BodyFragmentLabel Label, UiRect Rect)> bodyFragmentLabels = [];
    BodyFragmentLabel? bodyFragmentLabelPress;

    internal IReadOnlyList<(BodyFragmentLabel Label, UiRect Rect)> BodyFragmentLabelsDrawn => bodyFragmentLabels;

    /// <summary>The still-scene harness can show the ordinary Alt labels without synthesizing keyboard input.</summary>
    internal void SceneShowBodyPartLabels(Vector3 focus)
    {
        HighlightFocus = focus;
        AltHeld = true;
    }

    BodyFragmentLabel? BodyFragmentLabelAt(Vector2 pixel)
    {
        if (HighlightFocus is null) return null;
        foreach (var (label, rect) in bodyFragmentLabels)
            if (rect.Contains(pixel.X, pixel.Y)) return label;
        return null;
    }

    /// <summary>Shared layout path for the live painter and event tests; hidden pieces never enter the hit rectangles.</summary>
    internal void LayoutBodyFragmentLabels(Func<string, float> measure)
    {
        bodyFragmentLabels.Clear();
        if (HighlightFocus is not { } focus || BodyFragmentLabels is null || EditorOpen) return;
        float scale = Height / 1080f, height = Height * 0.0259259f;
        var near = new List<(BodyFragmentLabel Label, float Distance, Vector2 At)>();
        foreach (var label in BodyFragmentLabels())
        {
            if (label.Name.Length == 0 || !float.IsFinite(label.Position.LengthSquared())
                || HiddenInWorld?.Invoke(label.Owner) == true || AboveViewedFloor?.Invoke(label.Position) == true
                || Vector3.DistanceSquared(focus, label.Position) > HighlightRadius * HighlightRadius) continue;
            var middle = label.Position + new Vector3(0, 0.5f, 0);
            if (Project(middle) is not { } at || at.X < 0 || at.X > Width || at.Y < 0 || at.Y > Height || Ray(at) is not { } ray) continue;
            float distance = Vector3.Distance(ray.Origin, middle);
            if (Hidden(ray, distance) || GroundHit(ray.Origin, ray.Direction, MathF.Max(0, distance - 0.25f)) is not null) continue;
            near.Add((label, distance, at));
        }
        near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var occupied = itemLabels.Select(p => p.Rect).ToList();
        foreach (var (label, _, at) in near)
        {
            float width = measure(label.Name);
            var rect = new UiRect(Math.Clamp(at.X - width / 2, 0, MathF.Max(0, Width - width)), at.Y - 6 * scale - height, width, height);
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach (var other in occupied)
                    if (rect.Intersect(other) is { Width: > 0, Height: > 0 }) { rect = rect with { Y = other.Y - height - 1 }; moved = true; }
            }
            if (rect.Y + rect.Height < 0) continue;
            occupied.Add(rect);
            bodyFragmentLabels.Add((label, rect));
        }
    }

    void AddBodyFragmentLabels(List<UiControl> controls)
    {
        float scale = Height / 1080f, em = 20 * 50f / 72f * scale;
        LayoutBodyFragmentLabels(name => (uiPainter?.MeasureStandard(name, em) ?? name.Length * em * 0.55f) + em);
        var over = BodyFragmentLabelAt(mouse);
        for (int i = bodyFragmentLabels.Count - 1; i >= 0; i--)
        {
            var (label, rect) = bodyFragmentLabels[i];
            string colour = over is { } selected && label.SamePiece(selected) ? "0.4 1 0.4" : TipHex("e0cccc");
            controls.Add(new($"bodypartlabel-{i}", "Widget", rect, "", "", "Kenshi_GenericTextBoxFlatSmallSkin", true,
                new Dictionary<string, string> { ["Colour"] = colour }, Vector4.Zero, Pickable: false));
            controls.Add(new($"bodypartlabel-text-{i}", "TextBox", rect, label.Name, "", "", true,
                new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "Center", ["TextColour"] = colour }, Vector4.Zero, Pickable: false, TextScale: 1));
        }
    }

    void InspectBodyLabel(BodyFragmentLabel label)
    {
        if (!label.Owner.IsNone && session.CurrentSnapshot.Characters.Any(c => c.Id == label.Owner))
            Say(new InspectCommand(label.Owner) { Tick = session.World.Tick });
    }

    void PickUpBodyLabel(BodyFragmentLabel label)
    {
        if (!label.Interactive || SelectedIds().Count == 0) { InspectBodyLabel(label); return; }
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == label.Owner) is not { } target) return;
        int side = label.Kind == BodyFragmentLabelKind.TorsoHalf ? label.TorsoSide : 0;
        var carriers = Selected().Where(c => CarriesFragment(c, target.Id, side)).Select(c => c.Id).ToArray();
        if (carriers.Length > 0) Say(new PutDownOrder(carriers) { Tick = session.World.Tick });
        else Say(new PickUpOrder([], target.Id) { Tick = session.World.Tick, TorsoSide = side });
    }

    bool BodyFragmentLabelMouseDown(MouseButton button, Vector2 pixel, bool ctrl, bool alt)
    {
        if (BodyFragmentLabelAt(pixel) is not { } label) return false;
        if (button == MouseButton.Left) { bodyFragmentLabelPress = label; return true; }
        if (button != MouseButton.Right) return false;
        if (!label.Interactive) { InspectBodyLabel(label); return true; }
        if (ctrl) { PickUpBodyLabel(label); return true; }
        Action quick = alt ? () => Say(new LootOrder([], label.Owner) { Tick = session.World.Tick, TorsoSide = label.TorsoSide }) : () => PickUpBodyLabel(label);
        if (!ContextPressOn(pixel, at => session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == label.Owner) is { } target
            ? Menu(label.Name, at, Verbs(target, label.TorsoSide)) : null, quick)) quick();
        return true;
    }

    bool BodyFragmentLabelMouseUp(MouseButton button, Vector2 pixel)
    {
        if (button != MouseButton.Left || bodyFragmentLabelPress is not { } pressed) return false;
        bodyFragmentLabelPress = null;
        if (BodyFragmentLabelAt(pixel) is { } released && pressed.SamePiece(released)) PickUpBodyLabel(released);
        return true;
    }
}
