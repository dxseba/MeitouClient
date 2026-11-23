using Meitou.Game.Ui;
using Meitou.Localisation;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Sparring;

/// <summary>
/// Port of Sparring 切磋 (workshop 3776308916): a "Sparring" row on the right-click menu of a squad mate makes the first selected character and
/// that mate fight one on one at half damage until one of them is down; nobody else joins in.
/// </summary>
public sealed class SparringMod : IMod
{
    internal static readonly SparringSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("sparring", _ => new SparSystem(Settings, context.Population), SystemAnchor.End);
        context.AddUi(new SparMenu());
        context.Log("Sparring initialized");
    }
}

/// <summary>
/// The mod's switches (the original has none; both are this port's).
/// </summary>
public sealed class SparringSettings
{
    /// <summary>
    /// Whether the Sparring row shows.
    /// </summary>
    [Setting("Enable Sparring", Hint = "Right-click a squad mate: Sparring, a one-on-one fight until one is down.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether a sparring blow's cut is dealt as blunt (off: the original's halved blow as it is).
    /// </summary>
    [Setting("Blunt blows while sparring", Hint = "A sparring blow's cut damage becomes blunt, so a spar knocks out instead of bleeding out.")]
    public bool CutToBlunt { get; set; }
}

/// <summary>
/// The "Sparring" row (the original's button caption; 切磋 when the game runs in Chinese): on a conscious player character that is not the first
/// selected one.
/// </summary>
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
        return [new UiMenuVerb(Caption, () => menu.Enqueue(new SparOrder(challenger, partner) { Tick = menu.Tick }))];
    }

    /// <summary>
    /// The original's caption: 切磋 when the game's language is Chinese (its settings.cfg language names chinese, china or zh), else Sparring.
    /// </summary>
    static string Caption => Loc.Language.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "切磋" : "Sparring";

    /// <summary>A player character, awake and alive, not carried.</summary>
    static bool Offered(CharacterSnapshot target) =>
        target.IsPlayer && target.CarriedBy == CharacterId.None && target.Body is not { Unconscious: true } and not { Dead: true };
}

/// <summary><paramref name="Challenger"/> and <paramref name="Partner"/>, both the player's, spar until one is down.</summary>
public sealed record SparOrder(CharacterId Challenger, CharacterId Partner) : SimCommand;
