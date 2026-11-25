using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.DirectControl;

/// <summary>
/// The overlay: the centred crosshair in first person and the third-person camera, and the target's name (red) with its thin health bars.
/// Built every frame from <see cref="Pilot"/>'s state on the interface's thread.
/// </summary>
sealed class ControlOverlay(string id) : IUiExtension
{
    const float BarWidth = 56, BarHeight = 5, Line = 16;

    static readonly Vector4 Red = new(0.95f, 0.25f, 0.2f, 1), White = new(1, 1, 1, 0.9f), Amber = new(1f, 0.7f, 0.2f, 1);
    static readonly Vector4 Back = new(0, 0, 0, 0.6f), Fill = new(0.8f, 0.15f, 0.12f, 1);

    string _text = "", _back = "", _fill = "";

    public string Id => id;

    public void Register(UiRegistration registration)
    {
        _text = registration.RegisterStyle("text", new UiStyle { TextAlign = "Center", TextPadding = new(0, 0, 0, 0) });
        _back = registration.RegisterStyle("bar-back", new UiStyle { Background = Back });
        _fill = registration.RegisterStyle("bar-fill", new UiStyle { Background = Fill });
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 45);
        foreach (string panel in FocusHidden) registration.AddScreenOverrides(UiScreens.Hud, panel, _ => Focus());
    }

    /// <summary>
    /// The HUD panels the Action RPG combat focus HUD hides (Kenshi_MainPanel.layout names): the squad bar, the orders, shortcuts, floor and build
    /// panels and the jobs; the name, stats, medical, time and message panels stay.
    /// </summary>
    static readonly string[] FocusHidden = ["SquadPanel", "OrdersPanel", "ShortcutsPanel", "FloorPanel", "BuildPanel", "JobsPanel"];

    static readonly UiWidgetOverride Hidden = new() { Visible = false };

    static UiWidgetOverride? Focus() =>
        Pilot.Current is { On: true, LookActive: true, Settings: { Classic: false, CombatFocusHud: true } } ? Hidden : null;

    UiNode Build()
    {
        if (Pilot.Current is not { On: true } pilot) return new UiNode("dc-overlay") { Placement = UiPlacement.Fill, Pickable = false };

        var root = new UiNode("dc-overlay") { Placement = UiPlacement.Fill, Pickable = false, Animated = true };
        if (pilot.View != View.Normal && pilot.Screen is { X: > 0 } screen) root.Children.Add(Label("dc-cross", "+", screen / 2, 24, White));
        if (pilot.Tag is { } tag) AddTag(root, tag);
        if (pilot.Screen is { X: > 0 } middle) AddCues(root, pilot, middle / 2);
        return root;
    }

    /// <summary>
    /// The Action RPG cues round the crosshair: the drawn direction's arrow (white), the locked attack direction (amber, with an L), and the hit
    /// marker (a red X fading out).
    /// </summary>
    void AddCues(UiNode root, Pilot pilot, Vector2 centre)
    {
        if (pilot.HitMarker > 0) root.Children.Add(Label("dc-hit", "X", centre, 24, Red with { W = pilot.HitMarker }));
        if (pilot.Settings.Classic || !pilot.Settings.ShowDirectionArrows || pilot.View == View.Normal) return;

        if (pilot.ShownGesture is var shown and not Gesture.None) root.Children.Add(Arrow("dc-arrow", shown, centre, White));
        if (pilot.LockedGesture is var locked and not Gesture.None)
        {
            root.Children.Add(Arrow("dc-lock-arrow", locked, centre, Amber));
            root.Children.Add(Label("dc-lock", "L", centre + new Vector2(0, 22), 24, Amber));
        }
    }

    UiNode Arrow(string id, Gesture g, Vector2 centre, Vector4 colour)
    {
        const float r = 60;
        var (text, at) = g switch
        {
            Gesture.Up => ("v", centre - new Vector2(0, r)),
            Gesture.Down => ("^", centre + new Vector2(0, r)),
            Gesture.Left => (">", centre - new Vector2(r, 0)),
            _ => ("<", centre + new Vector2(r, 0)),
        };
        return Label(id, text, at, 24, colour);
    }

    void AddTag(UiNode root, NameTag tag)
    {
        root.Children.Add(Label("dc-tag-name", tag.Name, tag.At - new Vector2(0, Line), 200, Red));
        float left = tag.At.X - tag.Bars.Count * (BarWidth + 2) / 2;
        for (int i = 0; i < tag.Bars.Count; i++)
        {
            float x = left + i * (BarWidth + 2);
            root.Children.Add(Box($"dc-tag-back-{i}", x, tag.At.Y, BarWidth, _back));
            root.Children.Add(Box($"dc-tag-fill-{i}", x, tag.At.Y, BarWidth * tag.Bars[i].Fill, _fill));
        }
    }

    UiNode Label(string id, string text, Vector2 centre, float width, Vector4 colour) =>
        new(id, UiNodeKind.Label)
        {
            Placement = new UiPlacement(width, Line, Left: centre.X - width / 2, Top: centre.Y - Line / 2),
            Text = text.Replace("#", "##", StringComparison.Ordinal),
            Style = _text,
            TextColour = colour,
            Pickable = false,
        };

    static UiNode Box(string id, float x, float y, float width, string style) =>
        new(id) { Placement = new UiPlacement(MathF.Max(width, 0), BarHeight, Left: x, Top: y), Style = style, Pickable = false };
}
