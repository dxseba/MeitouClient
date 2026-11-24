using Meitou.Data.Gameplay.Ai;

namespace Meitou.JobManagement;

/// <summary>
/// The original's HUD button picture and station group pictures (<c>gui/station-&lt;group&gt;.png</c>), read from its installed folder
/// (<see cref="OriginalFiles"/>);
/// a group without one shows its name only, as the original's text fallback.
/// </summary>
public sealed class StationIcons
{
    readonly Dictionary<StationCategory, string> _paths = [];

    readonly Dictionary<string, string> _jobs = new(StringComparer.Ordinal);

    internal StationIcons(OriginalFiles files)
    {
        Hud = files.Find(Path.Combine("gui", "kjm-hud-icon.png"));
        foreach (string name in JobFiles)
        {
            if (files.Find(Path.Combine("gui", name)) is { } path) _jobs[name] = path;
        }

        foreach (var category in Enum.GetValues<StationCategory>())
        {
            if (files.Find(Path.Combine("gui", $"station-{FileName(category)}.png")) is { } path) _paths[category] = path;
        }
    }

    /// <summary>The original's HUD button picture (<c>gui/kjm-hud-icon.png</c>); null when the original is not installed.</summary>
    public string? Hud { get; }

    /// <summary>The group's picture's full path; null when the original is not installed.</summary>
    public string? Of(StationCategory category) => _paths.GetValueOrDefault(category);

    /// <summary>
    /// A job row's picture, as the original's job cards pick it by task type (decompiled DLL): robotics a limb, the rescue and medical rows the
    /// medic, engineering, the escort rows, the animal foraging rows, splinting; null for the others or when the original is not installed.
    /// </summary>
    public string? Of(TaskType type) => JobFile((int)type) is { } name ? _jobs.GetValueOrDefault(name) : null;

    static readonly string[] JobFiles =
        ["station-skeleton-limbs.png", "job-medic.png", "job-engineering.png", "job-escort-out.png", "job-foraging-animals.png", "job-splint.png"];

    static string? JobFile(int type) => type switch
    {
        57 => "station-skeleton-limbs.png",
        58 or 105 or 106 or 148 => "job-medic.png",
        125 => "job-engineering.png",
        158 or 223 or 224 or 273 or 274 or 283 => "job-escort-out.png",
        238 or 277 or 278 or 279 => "job-foraging-animals.png",
        250 => "job-splint.png",
        _ => null,
    };

    static string FileName(StationCategory category) => category.ToString().ToLowerInvariant();
}
