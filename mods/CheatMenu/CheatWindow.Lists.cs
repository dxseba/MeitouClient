using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Localisation;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>
/// The pages with lists: Spawn items (its four tabs, search, grade and Give 1 / Give 10), Factions (+/-25 per faction and Ally with all), Edit
/// skills, Travel to town and Spawn squad at camera. A list shows the first <see cref="Shown"/> matches of the search, sorted by name.
/// </summary>
internal sealed partial class CheatWindow
{
    const int Shown = 60;

    /// <summary>The original's relation step and bounds (<b>Verified</b>: +/-25, held to -100..100, set both ways).</summary>
    const float RelationStep = 25, Allied = 100;

    /// <summary>The original's armour and gear grades, Prototype to Masterwork (<b>Verified</b>), as qualities 0 to 100.</summary>
    static readonly string[] Grades = ["Prototype", "Shoddy", "Standard", "High", "Specialist", "Masterwork"];

    static readonly string[] ItemTabs = ["Items", "Weapons", "Armour", "Other"];

    int _tab, _grade, _maker;
    string? _picked;

    IEnumerable<UiNode> ItemsPage()
    {
        yield return Head("Spawn items (selected)");
        var tabs = Kit.Row("cheat-tabs");
        for (int i = 0; i < ItemTabs.Length; i++)
            tabs.Children.Add(Kit.Button("cheat-tab" + i, (i == _tab ? "> " : "") + Tr(ItemTabs[i]), _styles.Button, _actions.Pick, "tab:" + i, 100));
        yield return tabs;
        yield return Search();
        foreach (var node in List(ItemRecords(), "item")) yield return node;
        if (_tab == 1) foreach (var node in WeaponGrade()) yield return node;
        else yield return Kit.Button("cheat-grade", $"{Tr("Quality")}: {Tr(Grades[_grade])}", _styles.Button, _actions.Pick, "grade");
        var give = Kit.Row("cheat-give");
        give.Children.Add(Kit.Button("cheat-give1", Tr("Give 1"), _styles.Button, _actions.Pick, "give:1", 200, _picked is not null));
        give.Children.Add(Kit.Button("cheat-give10", Tr("Give 10"), _styles.Button, _actions.Pick, "give:10", 200, _picked is not null));
        yield return give;
    }

    IEnumerable<UiNode> WeaponGrade()
    {
        var makers = Makers;
        string maker = _maker > 0 && _maker <= makers.Count ? makers[_maker - 1].Name : Tr("Standard");
        yield return Kit.Button("cheat-maker", $"{Tr("Manufacturer")}: {maker}", _styles.Button, _actions.Pick, "maker");
        var row = Kit.Row("cheat-level");
        row.Children.Add(Kit.Label("cheat-level-label", Tr("Level (1-99)"), _styles.Text, 200));
        row.Children.Add(Kit.TextBox("cheat-level-box", _value, _actions.Value, 100, 2));
        yield return row;
    }

    /// <summary>The weapon manufacturers in the order the Manufacturer button cycles them (by shown name).</summary>
    List<(string Id, string Name)> Makers => [.. Records(FcsRecordType.WeaponManufacturer)];

    /// <summary>The records of the open tab: Items (ITEM), Weapons, Armour, Other (crossbows, limbs, containers, blueprints, maps).</summary>
    IEnumerable<(string Id, string Name)> ItemRecords()
    {
        FcsRecordType[] types = _tab switch
        {
            0 => [FcsRecordType.Item],
            1 => [FcsRecordType.Weapon],
            2 => [FcsRecordType.Armour],
            _ => [.. DebugGiveItem.ItemTypes.Except([FcsRecordType.Item, FcsRecordType.Weapon, FcsRecordType.Armour])],
        };
        return types.SelectMany(Records);
    }

    IEnumerable<(string Id, string Name)> Records(FcsRecordType type) =>
        context.Data.OfType(type).Select(r => (r.StringId, ShownText.Record(r.StringId, r.Name)))
            .OrderBy(r => r.Item2, StringComparer.CurrentCulture);

    UiNode Search()
    {
        var row = Kit.Row("cheat-search-row");
        row.Children.Add(Kit.Label("cheat-search-label", Tr("Search:"), _styles.Text, 90));
        row.Children.Add(Kit.TextBox("cheat-search", _search, _actions.Search, 0, 64));
        return row;
    }

    /// <summary>The list's lines: the matches of the search, each picking <paramref name="kind"/>:id.</summary>
    IEnumerable<UiNode> List(IEnumerable<(string Id, string Name)> all, string kind)
    {
        string q = _search.Trim();
        var shown = all.Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(Shown).ToList();
        var list = Kit.Scroll("cheat-list-" + kind, 8 * (Kit.Line + 2) + 4, _styles.List);
        for (int i = 0; i < shown.Count; i++)
        {
            string mark = _picked == shown[i].Id ? "> " : "";
            list.Children.Add(Kit.Button($"cheat-{kind}-{i}", mark + shown[i].Name, _styles.Button, _actions.Pick, $"{kind}:{shown[i].Id}"));
        }

        yield return list;
    }

    IEnumerable<UiNode> FactionsPage()
    {
        yield return Head("Factions");
        yield return Do("Ally with all factions");
        var factions = context.Population.Factions;
        for (int i = 0; i < factions.Count; i++)
        {
            if (i == context.Population.PlayerFaction) continue;

            var row = Kit.Row("cheat-faction" + i);
            row.Children.Add(Kit.Label("cheat-faction-name" + i, ShownText.Record(factions[i].Id, factions[i].Name), _styles.Text));
            row.Children.Add(Kit.Button("cheat-faction-minus" + i, "-25", _styles.Button, _actions.Pick, $"relation:{i}:-1", 60));
            row.Children.Add(Kit.Button("cheat-faction-plus" + i, "+25", _styles.Button, _actions.Pick, $"relation:{i}:1", 60));
            yield return row;
        }
    }

    IEnumerable<UiNode> SkillsPage()
    {
        string who = _snapshot?.Characters.FirstOrDefault(c => Target.Contains(c.Id))?.Name ?? "";
        yield return Head(who.Length > 0 ? Tr("Skills: ") + who : Tr("Skills: select a character"));
        var row = Kit.Row("cheat-skill-value");
        row.Children.Add(Kit.Label("cheat-skill-value-label", Tr("Level (1-99)"), _styles.Text, 200));
        row.Children.Add(Kit.TextBox("cheat-skill-value-box", _value, _actions.Value, 100, 4));
        yield return row;
        foreach (var stat in CheatSystem.AllStats)
            yield return Kit.Button("cheat-skill-" + stat, Tr(StatName(stat)), _styles.Button, _actions.Pick, "skill:" + (int)stat);
    }

    /// <summary>A stat's name as the game's stats screen spells it (the enum's words split).</summary>
    static string StatName(StatsEnumerated stat) =>
        string.Concat(stat.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

    /// <summary>XP per skill: each stat but 14 (the original skips it) cycles its own multiplier, shown as "name: xN" or "off".</summary>
    IEnumerable<UiNode> SkillXpPage()
    {
        yield return Head("XP per skill...");
        foreach (var stat in CheatSystem.AllStats.Where(s => (int)s != 14))
        {
            int index = Running?.View.SkillTier[(int)stat] ?? 0;
            string shown = index == 0 ? Tr("off") : "x" + CheatSystem.SkillValues[index].ToString("0", CultureInfo.InvariantCulture);
            yield return Kit.Button("cheat-skillxp-" + stat, $"{Tr(StatName(stat))}: {shown}", _styles.Button, _actions.Pick, "skillxp:" + (int)stat);
        }
    }

    IEnumerable<UiNode> TownsPage()
    {
        yield return Head("Travel to town");
        yield return Search();
        var towns = context.Population.Sites.Select(s => (s.Index.ToString(CultureInfo.InvariantCulture), s.Town.ShownName()))
            .OrderBy(t => t.Item2, StringComparer.CurrentCulture);
        foreach (var node in List(towns, "town")) yield return node;
    }

    IEnumerable<UiNode> SquadsPage()
    {
        yield return Head("Spawn squad at camera");
        yield return Search();
        var squads = context.Data.OfType(FcsRecordType.SquadTemplate).Where(t => context.Population.FactionOfTemplate(t.StringId) >= 0)
            .Select(t => (t.StringId, ShownText.Record(t.StringId, t.Name))).OrderBy(t => t.Item2, StringComparer.CurrentCulture);
        foreach (var node in List(squads, "squad")) yield return node;
    }
}
