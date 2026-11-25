using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.Nitoryu;

/// <summary>What a character's inventory buttons show: whether it carries a pair, its dual stance is on, and the any-weapon pair's state.</summary>
readonly record struct ButtonState(bool Pair, bool On, bool AnyShown, bool AnyOn);

/// <summary>
/// The original's two buttons on the back weapon slot of the inventory window (README "How to use it"): crossed blades (on) or one grey blade
/// (off) toggle the dual stance; the second, shown for a character carrying two blunt weapons or hackers (or one with a katana or sabre), lets it
/// fight with them as a pair. Both are saved with the character. Drawn inside the back weapon's cell (<c>UiScreens.ItemCells</c>), in its top
/// corner; a click queues the order. <c>[UI] toggle_button</c> and <c>any_weapon_button</c> hide them. Display and input only.
/// </summary>
sealed class InventoryButtons(bool toggle, bool any) : IUiExtension
{
    static readonly string[] Screens = ["inventory"];
    static readonly Vector4 Lit = new(1, 0.85f, 0.4f, 1), Grey = new(0.6f, 0.6f, 0.6f, 1);

    public static readonly ConcurrentDictionary<CharacterId, ButtonState> States = new();

    string _button = "", _press = "", _pressAny = "";
    UiScreenContext? _last;

    public string Id => "dxseba.nitoryu.buttons";

    public void Register(UiRegistration registration)
    {
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _press = registration.RegisterAction("toggle", _ => Press(any: false));
        _pressAny = registration.RegisterAction("any", _ => Press(any: true));
        foreach (string screen in Screens) registration.AddScreenControls(screen, UiScreens.ItemCells, UiAnchorPlacement.Inside, Build);
    }

    UiNode? Build(UiScreenContext context)
    {
        if (context.Item is not { Item.Section: "back" } || context.Character is not { IsPlayer: true } c) return null;
        if (!States.TryGetValue(c.Id, out var state) || !state.Pair && !state.AnyShown) return null;

        _last = context;
        float size = Math.Clamp(context.Anchor.Height / 4, 16, 24);
        var root = new UiNode("nitoryu-buttons") { Placement = UiPlacement.Fill, Pickable = false };
        if (toggle && state.Pair) root.Children.Add(Button("nitoryu-dual", _press, state.On ? "XX" : "/", state.On, size, 2));
        if (any && state.AnyShown) root.Children.Add(Button("nitoryu-any", _pressAny, "X+", state.AnyOn, size, size + 4));
        return root;
    }

    UiNode Button(string id, string action, string text, bool on, float size, float left) => new(id, UiNodeKind.Button)
    {
        Placement = new UiPlacement(size, size, Left: left, Top: 2),
        Style = _button,
        Text = text,
        TextColour = on ? Lit : Grey,
        Action = action,
        TextScale = 0.7f,
    };

    void Press(bool any)
    {
        if (_last is not { Character: { } c } context) return;

        long tick = context.Tick;
        SimCommand order = any ? new ToggleAnyWeaponOrder(c.Id) { Tick = tick } : new ToggleDualOrder([c.Id]) { Tick = tick };
        context.Enqueue(order);
    }
}

/// <summary>The character fights with its blunt weapons or hackers as a pair, or stops (the original's second button).</summary>
public sealed record ToggleAnyWeaponOrder(CharacterId Character) : SimCommand;
