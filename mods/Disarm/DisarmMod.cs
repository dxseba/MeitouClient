using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>
/// Port of dafitime's Disarm: a "Disarm NN%" row on the right-click menu of a downed, sleeping or (while sneaking) unaware character; Kick for loose
/// items on the ground; a chance-based weapon drop on knockout; optional AI rearming; the original's settings under Mod settings.
/// </summary>
public sealed class DisarmMod : IMod
{
    internal static readonly DisarmSettings Settings = new();

    /// <summary>The running world's system, for the menu's chances; null before a world starts.</summary>
    internal static volatile DisarmSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        context.AddSystem("disarm", _ => Running = new DisarmSystem(Settings, context.Population, context.RngPurpose, context.Enqueue),
            SystemAnchor.End);
        context.AddUi(new DisarmMenu());
        context.Log("Disarm initialized");
    }
}

/// <summary>
/// The menu rows: "Disarm NN%" on a character (the first selected character with a chance walks over and tries) and Kick on a loose ground item,
/// after the game's Pick Up (whose quick click still picks up).
/// </summary>
sealed class DisarmMenu : IUiExtension
{
    public string Id => "dxseba.disarm.ui";

    public void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(Verbs);
        registration.AddMenuVerbs(GroundVerbs);
    }

    /// <summary>
    /// Kick on a ground item: the first selected character walks to it and kicks it away.
    /// </summary>
    static IEnumerable<UiMenuVerb> GroundVerbs(UiMenuTargetContext menu)
    {
        if (!DisarmMod.Settings.Enabled || !DisarmMod.Settings.Kick || menu.Target.GroundItem is not { } item) return [];

        var actor = menu.Selected[0];
        return [new UiMenuVerb("Kick", () =>
        {
            menu.Enqueue(new MoveOrder([actor], item.Position) { Tick = menu.Tick });
            menu.Enqueue(new KickOrder(actor, item.Id) { Tick = menu.Tick });
        })];
    }

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        if (!DisarmMod.Settings.Enabled || DisarmMod.Running is not { } system || menu.Target.CarriedBy != CharacterId.None) return [];

        var chances = system.Chances;
        foreach (var actor in menu.Selected)
        {
            if (chances.TryGetValue((actor, menu.Target.Id), out int chance)) return [Verb(menu, actor, chance)];
        }

        return [];
    }

    /// <summary>The row: the caption carries the chance after a tab, as the game's own chance rows do.</summary>
    static UiMenuVerb Verb(UiMenuContext menu, CharacterId actor, int chance)
    {
        var target = menu.Target;
        string caption = "Disarm\t" + chance.ToString(CultureInfo.InvariantCulture);
        return new UiMenuVerb(caption, () =>
        {
            menu.Enqueue(new MoveOrder([actor], target.Position) { Tick = menu.Tick });
            menu.Enqueue(new DisarmOrder(actor, target.Id) { Tick = menu.Tick });
        });
    }
}

/// <summary><paramref name="Actor"/> walks to <paramref name="Target"/> and tries to disarm it.</summary>
public sealed record DisarmOrder(CharacterId Actor, CharacterId Target) : SimCommand;

/// <summary><paramref name="Actor"/> walks to the ground item <paramref name="Item"/> and kicks it away.</summary>
public sealed record KickOrder(CharacterId Actor, int Item) : SimCommand;
