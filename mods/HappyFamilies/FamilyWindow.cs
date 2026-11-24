using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.HappyFamilies;

/// <summary>
/// The original's family window (600 x 150 at the screen's top left, caption "Family Tree of &lt;name&gt;"): the father on the left half, the
/// mother on the right, the children below. It follows the selected player character while open; its key (F by default) or its close button
/// shuts it.
/// </summary>
sealed class FamilyWindow(ModContext context) : IUiExtension
{
    const float Width = 600, Height = 150, Caption = 26;

    string _panel = "", _title = "", _text = "", _button = "", _close = "";
    volatile bool _open;
    CharacterId _shown = CharacterId.None;

    public string Id => "dxseba.happyfamilies.ui";

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        _panel = registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = ink });
        _title = registration.RegisterStyle("title", new() { Background = new(.16f, .12f, .09f, 1), Text = ink });
        _text = registration.RegisterStyle("text", new() { Text = ink });
        _button = registration.RegisterStyle("button", new() { Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1) });
        _close = registration.RegisterAction("close", _ => _open = false);
        registration.Contribute(UiSlots.Overlay, Build, order: 57);
    }

    /// <summary>
    /// The key: closes an open window, else opens it on the selected player character (not an animal).
    /// </summary>
    public void Toggle(IReadOnlyList<CharacterId> selected)
    {
        if (_open)
        {
            _open = false;
            return;
        }

        if (selected.Count == 0 || FamilySystem.Views?.ContainsKey(selected[0]) != true) return;

        _shown = selected[0];
        _open = true;
    }

    /// <summary>
    /// Once a frame: a newly selected player character takes the window, as the original's selection handler does.
    /// </summary>
    public void Follow(ModFrame frame)
    {
        if (_open && frame.Shown is { } lead && FamilySystem.Views?.ContainsKey(lead) == true) _shown = lead;
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!_open || FamilySystem.Views?.GetValueOrDefault(_shown) is not { } view) return new UiNode("family-hidden") { Visible = false };

        var window = new UiNode("family-window") { Placement = new(Width, Height, Left: 0, Top: 0), Style = _panel };
        window.Children.Add(TitleBar(view));
        window.Children.Add(Label("family-father", context.Tr("Father: ") + Parent(view.Father, view.FatherGone), 0, 30, Width / 2, 60));
        window.Children.Add(Label("family-mother", context.Tr("Mother: ") + Parent(view.Mother, view.MotherGone), Width / 2, 30, Width / 2, 60));
        window.Children.Add(Label("family-children", Children(view), 0, 95, Width, 40));

        var root = new UiNode("family") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(window);
        return root;
    }

    UiNode TitleBar(FamilyView view)
    {
        var bar = new UiNode("family-caption", UiNodeKind.Label)
        {
            Placement = new(Width, Caption, Left: 0, Top: 0), Text = context.Tr("Family Tree of ") + view.Name, Style = _title,
        };
        bar.Children.Add(new UiNode("family-close", UiNodeKind.Button)
        {
            Placement = new(Caption, Caption, Left: Width - Caption, Top: 0), Text = "X", Style = _button, Action = _close,
        });
        return bar;
    }

    string Parent(string? name, bool gone) => name is null ? context.Tr("Unknown") : gone ? name + context.Tr(" (Dead)") : name;

    string Children(FamilyView view) =>
        view.Children.Count == 0 ? context.Tr("No Children.") : context.Tr("Children: ") + string.Join(", ", view.Children);

    UiNode Label(string id, string text, float left, float top, float width, float height) =>
        new(id, UiNodeKind.Label) { Placement = new(width, height, Left: left, Top: top), Text = text, Style = _text };
}
