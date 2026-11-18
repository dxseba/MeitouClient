using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QueenBirthing;

/// <summary>
/// Port of "Queen Start with Birthing" (workshop 3777203526), rebuilt from its description: a "Give Birth" row on a player Hive Queen pays hunger
/// and adds a knocked-out Hive drone to her squad beside her.
/// </summary>
public sealed class QueenBirthingMod : IMod
{
    internal static readonly BirthSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var drone = DroneRecord(context.Data, Settings.DroneName);
        context.Log(drone is null ? $"Queen Birthing: no CHARACTER record for '{Settings.DroneName}'" : $"Queen Birthing: drone record {drone}");
        context.AddSystem("birthing", _ => new BirthSystem(Settings, drone), SystemAnchor.End);
        context.AddUi(new BirthMenu());
        context.Log("Queen Birthing initialized");
    }

    /// <summary>
    /// The drone's CHARACTER record: one named <paramref name="name"/>, else the first (by id) whose race is a RACE of that name.
    /// </summary>
    static string? DroneRecord(GameDatabase data, string name)
    {
        var characters = data.OfType(FcsRecordType.Character).OrderBy(r => r.StringId, StringComparer.Ordinal).ToList();
        if (characters.FirstOrDefault(r => r.Name == name) is { } named) return named.StringId;

        var races = data.OfType(FcsRecordType.Race).Where(r => r.Name == name).Select(r => r.StringId).ToHashSet(StringComparer.Ordinal);
        return characters.FirstOrDefault(r => r.GetReferences("race").Any(x => races.Contains(x.TargetStringId)))?.StringId;
    }

    /// <summary>A Hive Queen: her race's name says so.</summary>
    internal static bool IsQueen(string? raceName) => raceName is not null && raceName.Contains(Settings.QueenRace, StringComparison.OrdinalIgnoreCase);
}

public sealed class BirthSettings
{
    [Setting("Enable Give Birth", Hint = "Shows the Give Birth row on your Hive Queens.")]
    public bool Enabled { get; set; } = true;

    [Setting("Hunger cost", Hint = "Hunger the queen pays per birth (the game's 0-300 scale).", Min = 10, Max = 250)]
    public int HungerCost { get; set; } = 100;

    [Setting("Knockout seconds", Hint = "How long the newborn drone stays knocked out.", Min = 5, Max = 600)]
    public int KnockoutSeconds { get; set; } = 60;

    /// <summary>The race-name text that marks a queen.</summary>
    public string QueenRace { get; set; } = "Hive Queen";

    /// <summary>The newborn's record or race name.</summary>
    public string DroneName { get; set; } = "Hive Worker Drone";
}

/// <summary>The "Give Birth" row on a living, conscious player Hive Queen.</summary>
sealed class BirthMenu : IUiExtension
{
    public string Id => "dxseba.queenbirthing.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!QueenBirthingMod.Settings.Enabled || !target.IsPlayer || target.Body is not { Dead: false, Unconscious: false }) return [];
        if (BirthSystem.Queens is not { } queens || !queens.Contains(target.Id)) return [];

        return [new UiMenuVerb("Give Birth", () => menu.Enqueue(new GiveBirthOrder(target.Id) { Tick = menu.Tick }))];
    }
}

/// <summary><paramref name="Queen"/> gives birth to a drone.</summary>
public sealed record GiveBirthOrder(CharacterId Queen) : SimCommand;
