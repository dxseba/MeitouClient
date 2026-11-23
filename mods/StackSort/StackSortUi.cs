using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.StackSort;

/// <summary>
/// The original's button strip <c>&lt;  StackSort [n]  H/W  &gt;</c> over a player character's inventory or bag window's ARRANGE
/// (<c>UiRegistration.AddScreenControls(kind, "ArrangeButton", Above, ...)</c>, its root moved down by the button's height): the original's
/// widths and gaps, centred on ARRANGE, the character's own window shifted by <c>body_x_offset</c>. The strip covers ARRANGE, as the original
/// hides it; with StackSort off nothing is drawn and ARRANGE is the game's.
/// </summary>
sealed class StackSortUi(StripGeometry strip) : IUiExtension
{
    /// <summary>
    /// The window kinds with an ARRANGE for a character's grid: the inventory (the main grid) and the worn bag's window.
    /// </summary>
    const string Inventory = "inventory", Bag = "bag";

    /// <summary>
    /// The registered style and press action.
    /// </summary>
    string _button = "", _press = "";

    /// <summary>
    /// The command queue of the last built window (the same queue for every window).
    /// </summary>
    Action<SimCommand>? _enqueue;

    /// <summary>
    /// The tick of the last built window, for the command.
    /// </summary>
    Func<long>? _tick;

    public string Id => "dxseba.stacksort.ui";

    public void Register(UiRegistration registration)
    {
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _press = registration.RegisterAction("press", Press);
        registration.AddScreenControls(Inventory, "ArrangeButton", UiAnchorPlacement.Above, c => Build(c, bag: false));
        registration.AddScreenControls(Bag, "ArrangeButton", UiAnchorPlacement.Above, c => Build(c, bag: true));
    }

    /// <summary>
    /// The strip for the window's character; nothing for a window of no player character or with StackSort off.
    /// </summary>
    UiNode? Build(UiScreenContext context, bool bag)
    {
        if (!StackSortMod.Settings.Enabled || context.Character is not { IsPlayer: true } owner) return null;

        _enqueue = context.Enqueue;
        _tick = () => context.Tick;
        float offset = bag ? 0 : strip.BodyOffset;
        var root = new UiNode("stacksort")
        {
            Placement = new(strip.Width, UiLength.Fill, Left: (context.Anchor.Width - strip.Width) / 2 + offset, Top: new UiLength(0, 1)),
            Flow = UiFlow.Row, Gap = strip.Gap, Pickable = false,
        };
        var original = StackSortMod.Original;
        int shown = LayoutCycle.Shown.TryGetValue((owner.Id, bag), out int n) ? n : 1;
        root.Children.Add(Button("back", "<", strip.Arrow, Argument(ArrangeMode.Previous, owner.Id, bag)));
        root.Children.Add(Button("sort", original.SortCaption(shown), strip.Sort, Argument(ArrangeMode.Next, owner.Id, bag, sort: true)));
        root.Children.Add(Button("dim", original.GoalCaption(LayoutCycle.Current), strip.Dim, Argument(ArrangeMode.SwitchGoal, owner.Id, bag)));
        root.Children.Add(Button("next", ">", strip.Arrow, Argument(ArrangeMode.Next, owner.Id, bag)));
        return root;
    }

    /// <summary>
    /// One strip button.
    /// </summary>
    UiNode Button(string id, string text, float width, string argument) => new("stacksort-" + id, UiNodeKind.Button)
    {
        Placement = new(width, UiLength.Fill), Text = text, Style = _button, Action = _press, Argument = argument,
    };

    /// <summary>
    /// A button's argument: what it asks, whether it is the sort button (Ctrl+click undoes), the grid and the character.
    /// </summary>
    static string Argument(ArrangeMode mode, CharacterId character, bool bag, bool sort = false) =>
        string.Join(':', (int)mode, sort ? 1 : 0, bag ? 1 : 0, character.Slot, character.Generation);

    /// <summary>
    /// A press: the game's ARRANGE command tagged with what the button asks (Ctrl+click on the sort button: undo).
    /// </summary>
    void Press(UiActionContext action)
    {
        var parts = action.Argument.Split(':');
        if (parts.Length != 5 || _enqueue is not { } enqueue || _tick is not { } tick) return;

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var mode = numbers[1] == 1 && StackSortMod.CtrlHeld ? ArrangeMode.Undo : (ArrangeMode)numbers[0];
        enqueue(StackSortMod.Order(new CharacterId(numbers[3], numbers[4]), numbers[2] == 1, mode, tick()));
    }
}
