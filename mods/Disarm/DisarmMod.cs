using System.Globalization;
using System.Numerics;
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
        OriginalLabels.Add(context);
        context.AddSettings(Settings);

        context.AddSystem("disarm", _ => Running = new DisarmSystem(Settings, context.Population, context.RngPurpose, context.Enqueue),
            SystemAnchor.End);
        context.AddUi(new DisarmMenu(context.Tr));
        context.Log("Disarm initialized");
    }
}

/// <summary>
/// The menu rows: "Disarm NN%" on a character (the first selected character with a chance walks over and tries) and Kick on a loose ground item,
/// after the game's Pick Up (whose quick click still picks up), that Pick Up red when it is a theft, as the original's Pickup button; Escape
/// cancels a pending disarm, as the original polls it (the key still goes on to the game).
/// </summary>
sealed class DisarmMenu(Func<string, string> tr) : IUiExtension
{
    /// <summary>The original's Pickup colour for a theft (MyGUI colour 0.85, 0.15, 0.15).</summary>
    static readonly Vector4 Theft = new(0.85f, 0.15f, 0.15f, 1);

    public string Id => "dxseba.disarm.ui";

    public void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(Verbs);
        registration.AddMenuVerbs(GroundVerbs);
        registration.AddMenuRowOverrides(TheftTint);
        registration.AddKeyInput(CancelOnEscape);
    }

    /// <summary>
    /// Escape with a disarm pending: the pending actors' disarm is dropped and they stop. The key is not taken (the original only reads it).
    /// </summary>
    static bool CancelOnEscape(UiKeyInput input)
    {
        if (!input.Cancel || DisarmMod.Running is not { Pending: { Count: > 0 } pending }) return false;

        input.Enqueue(new CancelDisarmOrder([.. pending]) { Tick = input.Tick });
        return false;
    }

    /// <summary>The hold menu's Pick Up on another faction's ground item (picking it up is a theft) in the original's red.</summary>
    static UiMenuRowOverride? TheftTint(UiMenuRowContext row)
    {
        if (!DisarmMod.Settings.Enabled || !DisarmMod.Settings.Kick || row.Caption != "Pick Up") return null;

        if (row.Target is not { Kind: UiMenuTargetKind.GroundItem, GroundItem: { } item } || DisarmMod.Running is not { } system) return null;

        bool theft = item.OwnedBy >= 0 && item.OwnedBy != system.PlayerFaction;
        return theft ? new UiMenuRowOverride { TextColour = Theft } : null;
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

    IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        if (!DisarmMod.Settings.Enabled || DisarmMod.Running is not { } system || menu.Target.CarriedBy != CharacterId.None) return [];

        var chances = system.Chances;
        foreach (var actor in menu.Selected)
        {
            if (chances.TryGetValue((actor, menu.Target.Id), out int chance)) return [Verb(menu, actor, chance)];
        }

        return [];
    }

    /// <summary>The row: the caption (translated) carries the chance after a tab, as the game's own chance rows do.</summary>
    UiMenuVerb Verb(UiMenuContext menu, CharacterId actor, int chance)
    {
        var target = menu.Target;
        string caption = tr("Disarm") + "\t" + chance.ToString(CultureInfo.InvariantCulture);
        return new UiMenuVerb(caption, () =>
        {
            menu.Enqueue(new MoveOrder([actor], target.Position) { Tick = menu.Tick });
            menu.Enqueue(new DisarmOrder(actor, target.Id) { Tick = menu.Tick });
        });
    }
}

/// <summary><paramref name="Actor"/> walks to <paramref name="Target"/> and tries to disarm it.</summary>
public sealed record DisarmOrder(CharacterId Actor, CharacterId Target) : SimCommand;

/// <summary>Escape: <paramref name="Actors"/> drop their pending disarm and stop.</summary>
public sealed record CancelDisarmOrder(IReadOnlyList<CharacterId> Actors) : SimCommand;

/// <summary><paramref name="Actor"/> walks to the ground item <paramref name="Item"/> and kicks it away.</summary>
public sealed record KickOrder(CharacterId Actor, int Item) : SimCommand;
