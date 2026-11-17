using System.Globalization;
using Meitou.Simulation;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;
using static Meitou.WorkForCats.WorkForCatsMod;

namespace Meitou.WorkForCats;

/// <summary>
/// What the employment window shows for the inspected workplace and the first selected player character: the building, its employer, why it
/// offers no job (null: it does), the worker, the skill and level, the hourly rate, and whether the worker is employed here.
/// </summary>
internal sealed record EmploymentView(string Site, string Building, string Employer, string? Refusal, CharacterId Worker, string? WorkerName,
    string Skill, int Level, float Rate, bool EmployedHere, string Status);

internal sealed partial class WorkSystem
{
    volatile EmploymentView? _view;

    /// <summary>
    /// The window's view, rebuilt every <see cref="Cadence"/> ticks; safe to read from the interface thread. Null: no workplace is inspected.
    /// </summary>
    public EmploymentView? View => _view;

    void Publish(World world)
    {
        var lookup = _finder.Find(world, world.Player.InspectedBuilding, _rules);
        _view = lookup.Place is { } place ? ViewOf(world, place, lookup.Refusal) : null;
    }

    EmploymentView ViewOf(World world, Workplace place, string? refusal)
    {
        string employer = place.Own ? "You" : _data.Relations.Contains(place.Owner) ? _data.Relations.Factions[place.Owner].Name : "Nobody";
        string skill = SkillName(place.Stat.ToString());
        if (Worker(world) is not { } worker)
            return new EmploymentView(place.Key, place.Name, employer, refusal, CharacterId.None, null, skill, 0, 0, false, "Select a character.");

        var (id, slot, cold) = worker;
        bool here = JobSite(cold) == place.Key;
        int level = (int)(cold.Stats?[place.Stat] ?? 0);
        string status = here ? StatusOf(world, slot, cold) : JobSite(cold) is null ? "Not employed." : "Employed elsewhere.";
        return new EmploymentView(place.Key, place.Name, employer, refusal, id, cold.Name, skill, level, RateOf(cold, place), here, status);
    }

    /// <summary>
    /// The first selected living player character.
    /// </summary>
    static (CharacterId, int, CharacterCold)? Worker(World world)
    {
        var table = world.Characters;
        foreach (var id in world.Player.Selection)
        {
            if (table.TryResolveNext(id, out int slot) && table.Cold(slot) is { IsPlayer: true, Medical.Dead: false } cold) return (id, slot, cold);
        }

        return null;
    }

    string StatusOf(World world, int slot, CharacterCold cold)
    {
        var e = _employees.GetValueOrDefault(world.Characters.IdOf(slot));
        string pay = e is null ? "" : $" Next pay in {Minutes(e.Hours)} min.";
        if ((cold.Orders & CharacterOrders.Jobs) == 0) return "Paused: Jobs are off." + pay;
        if (e is not null && Working(world, slot, cold, e) is not null) return "Working." + pay;
        if (cold.Running is { Row: JobType, Task: CharacterTask.GoTo }) return "On the way." + pay;

        return "Paused." + pay;
    }

    static string Minutes(double hours) => ((int)Math.Ceiling((1 - hours) * 60)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// "SmithingWeapon" as "Smithing weapon".
    /// </summary>
    static string SkillName(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && text.Length > 0) text.Append(' ').Append(char.ToLowerInvariant(c));
            else text.Append(c);
        }

        return text.ToString();
    }
}
