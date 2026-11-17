using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Sparring;

/// <summary>
/// Port of Sparring 切磋 (workshop 3776308916): a "Spar" row on the right-click menu of a squad mate makes the first selected character and that
/// mate fight one on one until one of them is down; other characters of their faction stay out, and blows on a downed partner are cancelled.
/// </summary>
public sealed class SparringMod : IMod
{
    internal static readonly SparringSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("sparring", _ => new SparSystem(Settings), SystemAnchor.End);
        context.AddUi(new SparMenu());
        SparHooks.Install(context.Harmony);
        context.Log("Sparring initialized");
    }
}

/// <summary>The original's options, under Mod settings (it had none; the cut-to-blunt switch is this port's).</summary>
public sealed class SparringSettings
{
    [Setting("Enable Sparring", Hint = "Right-click a squad mate: Spar, a one-on-one fight until one is down.")]
    public bool Enabled { get; set; } = true;

    [Setting("Blunt blows while sparring", Hint = "A sparring blow's cut damage becomes blunt, so a spar knocks out instead of bleeding out.")]
    public bool CutToBlunt { get; set; } = true;
}

/// <summary>The "Spar" row: on a conscious squad mate of the player that is not the first selected character.</summary>
sealed class SparMenu : IUiExtension
{
    public string Id => "dxseba.sparring.ui";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        if (!SparringMod.Settings.Enabled || !Offered(menu.Target) || menu.Selected.Count == 0) return [];

        var challenger = menu.Selected[0];
        if (challenger == menu.Target.Id) return [];

        var partner = menu.Target.Id;
        return [new UiMenuVerb("Spar", () => menu.Enqueue(new SparOrder(challenger, partner) { Tick = menu.Tick }))];
    }

    /// <summary>A player character, awake and alive, not carried.</summary>
    static bool Offered(CharacterSnapshot target) =>
        target.IsPlayer && target.CarriedBy == CharacterId.None && target.Body is not { Unconscious: true } and not { Dead: true };
}

/// <summary><paramref name="Challenger"/> and <paramref name="Partner"/>, both the player's, spar until one is down.</summary>
public sealed record SparOrder(CharacterId Challenger, CharacterId Partner) : SimCommand;
