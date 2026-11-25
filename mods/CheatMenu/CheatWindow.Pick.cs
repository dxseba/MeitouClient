using System.Globalization;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>The list pages' clicks: an argument <c>kind:value</c> (a tab, a record, a faction step, a stat, a town, a squad template).</summary>
internal sealed partial class CheatWindow
{
    void Pick(string argument)
    {
        int colon = argument.IndexOf(':');
        string kind = colon < 0 ? argument : argument[..colon], value = colon < 0 ? "" : argument[(colon + 1)..];
        switch (kind)
        {
            case "tab": SetTab(int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "item": _picked = value; break;
            case "grade": _grade = (_grade + 1) % Grades.Length; break;
            case "maker": _maker = (_maker + 1) % (Makers.Count + 1); break;
            case "give": Give(int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "relation": StepRelation(value); break;
            case "skill": SetSkill((StatsEnumerated)int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "town": Travel(int.Parse(value, CultureInfo.InvariantCulture)); break;
            case "squad": SpawnSquad(value); break;
            case "skillxp": Send(new CycleSkillXp((StatsEnumerated)int.Parse(value, CultureInfo.InvariantCulture)) { Tick = T }); break;
        }
    }

    void SetTab(int tab)
    {
        _tab = tab;
        _picked = null;
        _search = "";
    }

    /// <summary>Give 1 / Give 10: the picked record to the first selected character, at the grade (weapons: the manufacturer and level).</summary>
    void Give(int count)
    {
        if (_picked is not { } record || Selected.Count == 0)
        {
            Say(Tr("Skills: select a character"));
            return;
        }

        var give = new DebugGiveItem(Selected[0], record, count, _grade * 20) { Tick = T, Exact = ChosenGrade() };
        Debug(give, $"{Tr(count == 1 ? "Give 1" : "Give 10")}: {record}");
    }

    /// <summary>A weapon's manufacturer and level (1 to 99) from the Weapons tab; null on the other tabs or with neither chosen.</summary>
    DebugWeaponGrade? ChosenGrade()
    {
        if (_tab != 1) return null;

        var makers = Makers;
        string? maker = _maker > 0 && _maker <= makers.Count ? makers[_maker - 1].Id : null;
        int? level = int.TryParse(_value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int l) ? Math.Clamp(l, 1, 99) : null;
        return maker is null && level is null ? null : new DebugWeaponGrade(maker, null, level);
    }

    /// <summary>A faction's +25 or -25: the cheat system moves the relation both ways, held to -100..100.</summary>
    void StepRelation(string value)
    {
        var parts = value.Split(':');
        int faction = int.Parse(parts[0], CultureInfo.InvariantCulture), sign = int.Parse(parts[1], CultureInfo.InvariantCulture);
        Send(new DoDeed(CheatMenu.Deed.StepRelation, [], faction, sign > 0 ? "+" : "-") { Tick = T });
        Say(ShownName(faction));
    }

    string ShownName(int faction) =>
        faction >= 0 && faction < context.Population.Factions.Count ? context.Population.Factions[faction].Name : "";

    void SetSkill(StatsEnumerated stat)
    {
        if (Target.Count == 0 || !float.TryParse(_value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float level))
        {
            Say(Tr("not changed"));
            return;
        }

        Debug(new DebugSetStats(Target, [stat], Math.Clamp(level, 0, 1000)) { Tick = T }, $"{StatName(stat)}: {level}");
    }

    /// <summary>Travel to town: the squad (else the selection) to the town's centre, as the original's teleport.</summary>
    void Travel(int site)
    {
        if (site < 0 || site >= context.Population.Sites.Count) return;

        var town = context.Population.Sites[site];
        var who = Selected.Count > 0 ? Selected : Squad;
        Debug(new DebugTeleport(who, town.Position) { Tick = T }, Tr("CheatMenu: travel to ") + town.Town.ShownName());
    }

    void SpawnSquad(string template)
    {
        Send(new CameraAt(_camera) { Tick = T });
        Send(new DoDeed(CheatMenu.Deed.SpawnSquad, [], Text: template) { Tick = T });
        Say(Tr("Spawn squad at camera"));
    }
}
