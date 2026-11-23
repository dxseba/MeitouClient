using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QueenBirthing;

/// <summary>
/// Port of "Queen Start with Birthing" (workshop 3777203526): a player Hive Queen pays hunger to give birth to a worker drone, a soldier drone or
/// a hive prince, who joins her squad knocked out beside her, and the character editor opens on the newborn.
/// </summary>
public sealed class QueenBirthingMod : IMod
{
    /// <summary>
    /// The player's settings.
    /// </summary>
    internal static readonly BirthSettings Settings = new();

    /// <summary>
    /// The CHARACTER template the original's data mod gives the worker drone (QueenBirth.mod, a required item).
    /// </summary>
    const string WorkerTemplate = "12-QueenBirth.mod";

    /// <summary>
    /// The soldier drone's and the hive prince's templates: named by the original DLL, absent from the QueenBirth.mod it ships.
    /// </summary>
    const string SoldierTemplate = "10-QueenBirth.mod", PrinceTemplate = "11-QueenBirth.mod";

    /// <summary>
    /// The worker's record name looked up when QueenBirth.mod is not loaded (this port's fallback).
    /// </summary>
    const string FallbackDrone = "Hive Worker Drone";

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var kinds = Kinds(context);
        context.AddSystem("birthing", _ => new BirthSystem(Settings, kinds), SystemAnchor.End);
        var button = new BirthButton(kinds);
        context.AddUi(button);
        context.AddFrame(button.Frame);
        EditorOpener.Install(context);
        context.Log("Queen Birthing initialized");
    }

    /// <summary>
    /// The births the loaded data can make, in the original menu's order; a kind whose template is missing is left out (logged).
    /// </summary>
    static IReadOnlyList<BirthKind> Kinds(ModContext context)
    {
        string? worker = Template(context.Data, WorkerTemplate) ?? FallbackRecord(context.Data);
        var kinds = new List<BirthKind>();
        Add(context, kinds, new BirthKind(0, "Worker Drone", worker, 1.30f));
        Add(context, kinds, new BirthKind(1, "Soldier Drone", Template(context.Data, SoldierTemplate), 2.30f));
        Add(context, kinds, new BirthKind(2, "Hive Prince", Template(context.Data, PrinceTemplate), 2.30f));
        return kinds;
    }

    /// <summary>
    /// Adds <paramref name="kind"/> when its template was found, logging either way.
    /// </summary>
    static void Add(ModContext context, List<BirthKind> kinds, BirthKind kind)
    {
        if (kind.Template is null)
        {
            context.Log($"Queen Birthing: no template for {kind.Label}; it is not offered");
            return;
        }

        context.Log($"Queen Birthing: {kind.Label} from {kind.Template}");
        kinds.Add(kind);
    }

    /// <summary>
    /// <paramref name="id"/> when it is a CHARACTER record of the merged data, else null.
    /// </summary>
    static string? Template(GameDatabase data, string id) => data.Find(id) is { Type: FcsRecordType.Character } ? id : null;

    /// <summary>
    /// A CHARACTER record named "Hive Worker Drone", else the first (by id) whose race is a RACE of that name.
    /// </summary>
    static string? FallbackRecord(GameDatabase data)
    {
        var characters = data.OfType(FcsRecordType.Character).OrderBy(r => r.StringId, StringComparer.Ordinal).ToList();
        if (characters.FirstOrDefault(r => r.Name == FallbackDrone) is { } named) return named.StringId;

        var races = data.OfType(FcsRecordType.Race)
            .Where(r => r.Name == FallbackDrone)
            .Select(r => r.StringId)
            .ToHashSet(StringComparer.Ordinal);
        return characters.FirstOrDefault(r => r.GetReferences("race").Any(x => races.Contains(x.TargetStringId)))?.StringId;
    }

    /// <summary>
    /// A queen, as the original tells one (case-sensitive): her race's string id contains "Queen" or <c>57335-Newwworld.mod</c> (Newwworld.mod's
    /// Hive Queen), or her race's name contains "Queen".
    /// </summary>
    internal static bool IsQueen(RaceData? race)
    {
        if (race is null) return false;

        return race.StringId.Contains("Queen", StringComparison.Ordinal)
            || race.StringId.Contains("57335-Newwworld.mod", StringComparison.Ordinal)
            || race.Name.Contains("Queen", StringComparison.Ordinal);
    }
}

/// <summary>
/// One birth the menu offers: its index in the original menu, label, CHARACTER template and hunger cost (raw 0-3 units; the original's buttons
/// show it x100).
/// </summary>
sealed record BirthKind(int Index, string Label, string? Template, float Cost)
{
    /// <summary>
    /// The cost as the original's button shows it (130, 230).
    /// </summary>
    public int ShownCost => (int)MathF.Round(Cost * 100);
}

/// <summary>
/// The mod's settings.
/// </summary>
public sealed class BirthSettings
{
    /// <summary>
    /// Whether the GIVE BIRTH button shows at all (this port's switch).
    /// </summary>
    [Setting("Enable Give Birth", Hint = "Shows the GIVE BIRTH button in your Hive Queens' inventory windows.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The original's <c>ShowEditorOnBirth</c> (default on): the character editor opens on the newborn.
    /// </summary>
    [Setting("Open editor on birth", Hint = "Opens the character editor on each newborn.")]
    public bool OpenEditorOnBirth { get; set; } = true;
}

/// <summary>
/// <paramref name="Queen"/> gives birth to the kind at <paramref name="Kind"/> in the original menu's order (0 worker, 1 soldier, 2 prince).
/// </summary>
public sealed record GiveBirthOrder(CharacterId Queen, int Kind) : SimCommand;
