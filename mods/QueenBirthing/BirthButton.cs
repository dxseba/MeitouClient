using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QueenBirthing;

/// <summary>
/// The original's GIVE BIRTH button in a living player queen's inventory window (TOO HUNGRY below the worker's cost), left of ARRANGE
/// (<c>UiRegistration.AddScreenControls("inventory", "ArrangeButton", Left, ...)</c>), and its popup at the pointer: WORKER DRONE (130), SOLDIER
/// DRONE (230), HIVE PRINCE (230), each enabled while her hunger covers it. The popup hides once the pointer has been off it for more than 10
/// frames (the original's rule, kept by a frame callback) or when its window closes.
/// </summary>
sealed class BirthButton(IReadOnlyList<BirthKind> kinds) : IUiExtension
{
    /// <summary>
    /// The original's popup size, its buttons' height and step, and their top margin and side margins.
    /// </summary>
    const float MenuWidth = 176, MenuHeight = 100, ButtonHeight = 24, ButtonStep = 30, ButtonTop = 8, ButtonInset = 12;

    /// <summary>
    /// The original button's width against ARRANGE's (its window-relative widths 0.22 and 0.22179).
    /// </summary>
    const float WidthShare = 0.22f / 0.22179f;

    /// <summary>
    /// Pixels between the button and ARRANGE (the original moves ARRANGE right instead; a screen control cannot move the game's own).
    /// </summary>
    const float Gap = 2;

    /// <summary>
    /// The original's popup offset from the pointer, and how many frames the pointer may be off it before it hides.
    /// </summary>
    const float PointerLeft = 12, PointerUp = 4;

    const int OutsideFrames = 10;

    /// <summary>
    /// The registered styles and actions.
    /// </summary>
    string _button = "", _panel = "", _press = "", _choose = "";

    /// <summary>
    /// The last built window's context (its queen, anchor and command queue).
    /// </summary>
    UiScreenContext? _last;

    /// <summary>
    /// The queen the popup is open for (none: closed), its top-left corner on the screen, and the frames the pointer has been off it.
    /// </summary>
    CharacterId _open = CharacterId.None;

    Vector2 _menuAt;

    int _outside;

    /// <summary>
    /// Whether a window built the popup this frame (it hides when its window closes).
    /// </summary>
    bool _shown;

    public string Id => "dxseba.queenbirthing.ui";

    public void Register(UiRegistration registration)
    {
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _press = registration.RegisterAction("birth", Press);
        _choose = registration.RegisterAction("kind", Choose);
        registration.AddScreenControls("inventory", "ArrangeButton", UiAnchorPlacement.Left, Build);
    }

    /// <summary>
    /// Once a frame: hides the popup when the pointer has been off it for more than <see cref="OutsideFrames"/> frames or its window is gone.
    /// </summary>
    public void Frame(ModFrame frame)
    {
        if (_open.IsNone) return;

        bool shown = _shown;
        _shown = false;
        var menu = new UiRect(_menuAt.X, _menuAt.Y, MenuWidth, MenuHeight);
        _outside = menu.Contains(frame.Pointer.X, frame.Pointer.Y) ? 0 : _outside + 1;
        if (!shown || _outside > OutsideFrames) _open = CharacterId.None;
    }

    /// <summary>
    /// The button (and the popup while open) in a living player queen's window; nothing in anyone else's.
    /// </summary>
    UiNode? Build(UiScreenContext context)
    {
        if (kinds.Count == 0 || !QueenBirthingMod.Settings.Enabled) return null;
        if (context.Character is not { IsPlayer: true, Body: { Dead: false } } queen) return null;
        if (BirthSystem.Queens is not { } queens || !queens.TryGetValue(queen.Id, out float hunger)) return null;

        _last = context;
        var root = new UiNode("queenbirth") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(new("queenbirth-button", UiNodeKind.Button)
        {
            Placement = new(new UiLength(0, WidthShare), UiLength.Fill, Right: Gap), Text = hunger >= kinds[0].Cost ? "GIVE BIRTH" : "TOO HUNGRY",
            Style = _button, Action = _press,
        });
        if (_open == queen.Id) root.Children.Add(Menu(context, hunger));
        return root;
    }

    /// <summary>
    /// The popup: one button per kind the data offers, enabled while her hunger covers its cost.
    /// </summary>
    UiNode Menu(UiScreenContext context, float hunger)
    {
        _shown = true;
        var origin = new Vector2(context.Anchor.X - context.Anchor.Width, context.Anchor.Y);
        var menu = new UiNode("queenbirth-menu")
        {
            Placement = new(MenuWidth, MenuHeight, Left: _menuAt.X - origin.X, Top: _menuAt.Y - origin.Y), Style = _panel,
        };
        foreach (var (kind, row) in kinds.Select((k, i) => (k, i)))
        {
            menu.Children.Add(new($"queenbirth-kind-{kind.Index}", UiNodeKind.Button)
            {
                Placement = new(MenuWidth - 2 * ButtonInset, ButtonHeight, Left: ButtonInset, Top: ButtonTop + row * ButtonStep),
                Text = $"{kind.Label.ToUpperInvariant()} ({kind.ShownCost})", Style = _button, Action = _choose,
                Argument = kind.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), Enabled = hunger >= kind.Cost,
            });
        }

        return menu;
    }

    /// <summary>
    /// GIVE BIRTH (or TOO HUNGRY): opens the popup at the pointer, kept on the screen, as the original's <c>ShowBirthMenu</c>.
    /// </summary>
    void Press(UiActionContext action)
    {
        if (_last?.Character is not { } queen) return;

        var view = action.Frame.Viewport;
        float x = Math.Clamp(action.Position.X - PointerLeft, view.X, Math.Max(view.X, view.X + view.Width - MenuWidth));
        float y = Math.Clamp(action.Position.Y - PointerUp, view.Y, Math.Max(view.Y, view.Y + view.Height - MenuHeight));
        (_open, _menuAt, _outside, _shown) = (queen.Id, new(x, y), 0, true);
    }

    /// <summary>
    /// A kind's button: the birth order for the queen the popup is open for; the popup closes.
    /// </summary>
    void Choose(UiActionContext action)
    {
        var queen = _open;
        _open = CharacterId.None;
        if (queen.IsNone || _last is not { } context || !int.TryParse(action.Argument, out int kind)) return;

        context.Enqueue(new GiveBirthOrder(queen, kind) { Tick = context.Tick });
    }
}
