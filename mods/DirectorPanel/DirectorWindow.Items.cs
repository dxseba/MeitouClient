using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.DirectorPanel;

/// <summary>The Items and Stats tabs.</summary>
internal sealed partial class DirectorWindow
{
    /// <summary>The original's six grades (armour, crossbows, prosthetics) and the quality each gives (port choice: the original's are not known).</summary>
    static readonly string[] GradeNames = ["Prototype grade", "Shoddy grade", "Standard grade", "High grade", "Specialist grade", "Masterwork"];

    static readonly int[] GradeQualities = [0, 20, 40, 60, 80, 100];

    /// <summary>The stats window's groups as the original's Stats tab groups them.</summary>
    static readonly (string Group, StatsEnumerated[] Stats)[] StatGroups =
    [
        ("Attributes", [StatsEnumerated.Strength, StatsEnumerated.Dexterity, StatsEnumerated.Toughness, StatsEnumerated.Perception]),
        ("Combat", [StatsEnumerated.MeleeAttack, StatsEnumerated.MeleeDefence, StatsEnumerated.Dodge, StatsEnumerated.MartialArts,
            StatsEnumerated.Katanas, StatsEnumerated.Sabres, StatsEnumerated.Hackers, StatsEnumerated.HeavyWeapons, StatsEnumerated.Blunt,
            StatsEnumerated.Polearms]),
        ("Ranged", [StatsEnumerated.Turrets, StatsEnumerated.Crossbows, StatsEnumerated.FriendlyFire]),
        ("Thievery", [StatsEnumerated.Stealth, StatsEnumerated.Lockpicking, StatsEnumerated.Thieving, StatsEnumerated.Assassination]),
        ("Athletics", [StatsEnumerated.Athletics, StatsEnumerated.Swimming]),
        ("Science", [StatsEnumerated.Science, StatsEnumerated.Engineering, StatsEnumerated.Robotics, StatsEnumerated.Medic]),
        ("Crafting", [StatsEnumerated.SmithingWeapon, StatsEnumerated.SmithingArmour, StatsEnumerated.SmithingBow, StatsEnumerated.Labouring,
            StatsEnumerated.Farming, StatsEnumerated.Cooking]),
    ];

    int _kind, _gradeIndex = 2, _makerGrade;
    Entry? _item, _maker, _bountyFaction;
    readonly HashSet<StatsEnumerated> _ticked = [];
    bool _everyone;

    IEnumerable<UiNode> ItemsTab()
    {
        yield return Heading("it-h", "Giving items:");
        yield return Choices("it-kind", Catalog.Kinds, _kind, k => _kind = k);
        string kind = Catalog.Kinds[_kind];
        foreach (var n in PickList("it", catalog.Items.Where(e => _kind == 0 || e.Group == kind), _item?.Id, e => _item = e)) yield return n;

        yield return Label("it-picked", _item is { } it ? texts.Format("Selected: %s   [%s]", it.Name, it.Id) : texts["Selected:"]);
        foreach (var n in GradeRows()) yield return n;

        var give = Row("it-give");
        give.Children.Add(Label("it-qty-label", texts["Quantity:"], width: 120));
        give.Children.Add(Field("it-qty", 80, "1"));
        give.Children.Add(Button("it-go", texts["Give"], GiveItem, 120, _item is not null && !_target.IsNone));
        yield return give;

        foreach (var n in InventoryRows()) yield return n;
        foreach (var n in ColourRows()) yield return n;
        foreach (var n in MoneyRows()) yield return n;
        yield return Button("it-heal", texts["Heal all squads"], () => Send(new HealSquads() { Tick = context.Tick }), 220);
    }

    IEnumerable<UiNode> GradeRows()
    {
        if (_item is not { } item) yield break;

        if (item.Group == "Weapons")
        {
            yield return Label("it-maker-h", texts["Manufacturer:"] + " " + (_maker?.Name ?? ""));
            foreach (var n in PickList("mk", catalog.Makers, _maker?.Id, m => (_maker, _makerGrade) = (m, 0), rows: 5)) yield return n;

            var grades = _maker is { } m ? catalog.Grades[m.Id] : [];
            if (_maker is not null && grades.Count == 0) yield return Hint("it-nogrades", "this manufacturer has no grades of its own");
            if (grades.Count > 0) yield return Choices("it-mgrade", [.. grades.Select(g => g.Name)], _makerGrade, g => _makerGrade = g);
        }
        else if (item.Group is "Armour" or "Crossbows" or "Limbs")
        {
            yield return Label("it-grade-h", texts["Grade:"]);
            yield return Choices("it-grade", GradeNames, _gradeIndex, g => _gradeIndex = g);
        }
    }

    void GiveItem()
    {
        if (_item is not { } item || _target.IsNone) return;

        int quantity = Math.Clamp((int)Number("it-qty", 1), 1, 10_000);
        var give = new DebugGiveItem(_target, item.Id, quantity, GradeQualities[_gradeIndex]) { Exact = WeaponGrade(item), Tick = context.Tick };
        context.Enqueue(give);
        _note = "";
    }

    /// <summary>A weapon's maker and its chosen grade (model and level); null leaves the game's default grade.</summary>
    DebugWeaponGrade? WeaponGrade(Entry item)
    {
        if (item.Group != "Weapons" || _maker is not { } maker) return null;

        var grades = catalog.Grades[maker.Id];
        if (grades.Count == 0) return new DebugWeaponGrade(maker.Id, null, null);

        var grade = grades[Math.Clamp(_makerGrade, 0, grades.Count - 1)];
        return new DebugWeaponGrade(maker.Id, grade.Model, grade.Level);
    }

    IEnumerable<UiNode> MoneyRows()
    {
        yield return Heading("mo-h", "Give money:");
        var line = Row("mo");
        line.Children.Add(Label("mo-label", texts["Amount:"], width: 120));
        line.Children.Add(Field("mo-amount", 120, "1000"));
        line.Children.Add(Button("mo-go", texts["Give"], GiveMoney, 120));
        yield return line;
    }

    void GiveMoney()
    {
        long amount = (long)Number("mo-amount", 0);
        if (amount > 0) Send(new AddMoney(amount) { Tick = context.Tick });
    }

    IEnumerable<UiNode> StatsTab()
    {
        if (Target is not { } target)
        {
            yield return Hint("st-none", "Nobody selected.");
            yield break;
        }

        var all = Row("st-all");
        all.Children.Add(Button("st-tick", texts["Tick all"], () => _ticked.UnionWith(StatGroups.SelectMany(g => g.Stats)), 150));
        all.Children.Add(Button("st-untick", texts["Untick all"], _ticked.Clear, 150));
        all.Children.Add(Tick("st-everyone", "Apply to everyone selected", _everyone, v => _everyone = v));
        yield return all;

        foreach (var (name, stats) in StatGroups)
        {
            foreach (var n in StatGroup(name, stats, target)) yield return n;
        }

        var apply = Row("st-apply");
        apply.Children.Add(Label("st-value-label", texts["Value:"], width: 80));
        apply.Children.Add(Field("st-value", 80, "100"));
        apply.Children.Add(Button("st-go", texts["Apply value"], ApplyStats, 180));
        yield return apply;

        foreach (var n in BountyRows(target)) yield return n;
        if (target.Animal) yield return Choices("st-age", ["Young", "Adult", "Old"], -1, AnimalAge);
    }

    IEnumerable<UiNode> StatGroup(string name, StatsEnumerated[] stats, TargetView target)
    {
        bool allOn = stats.All(_ticked.Contains);
        var head = Row("st-g-" + name);
        head.Children.Add(Label("st-gl-" + name, texts[name], _styles.Title, 200));
        head.Children.Add(Button("st-gt-" + name, texts[allOn ? "untick group" : "tick group"], () => TickGroup(stats, !allOn), 160));
        yield return head;

        foreach (var stat in stats)
        {
            var s = stat;
            string caption = $"{StatName(s)}: {F0(target.Stats[(int)s])}";
            yield return Tick("st-" + s, caption, _ticked.Contains(s), on => { if (on) _ticked.Add(s); else _ticked.Remove(s); });
        }
    }

    void TickGroup(StatsEnumerated[] stats, bool on)
    {
        if (on) _ticked.UnionWith(stats);
        else _ticked.ExceptWith(stats);
    }

    void ApplyStats()
    {
        if (_ticked.Count == 0)
        {
            _note = texts["No stat is ticked"];
            return;
        }

        IReadOnlyList<CharacterId> who = _everyone && _selected.Count > 0 ? _selected : [_target];
        Send(new SetStats(who, [.. _ticked.Order()], Math.Clamp(Number("st-value", 100), 0, 1000)) { Tick = context.Tick });
    }

    IEnumerable<UiNode> BountyRows(TargetView target)
    {
        yield return Heading("bo-h", "Bounty:");
        yield return Label("bo-now", texts.Format("Current bounty: %d", target.Bounty));
        foreach (var n in PickList("bo", catalog.Factions, _bountyFaction?.Id, f => _bountyFaction = f, rows: 5)) yield return n;

        var line = Row("bo-set");
        line.Children.Add(Field("bo-amount", 120, "1000"));
        line.Children.Add(Button("bo-go", texts["Assign"], AssignBounty, 140, _bountyFaction is not null));
        line.Children.Add(Button("bo-clear", texts["Clear all bounties"], () => Send(new ClearBounties(_target) { Tick = context.Tick }), 220));
        yield return line;
    }

    void AssignBounty()
    {
        if (_bountyFaction is { } f && int.TryParse(f.Id, out int faction))
            Send(new SetBounty(_target, faction, Math.Max(1, (int)Number("bo-amount", 1000))) { Tick = context.Tick });
    }

    /// <summary>The original's Young, Adult and Old ages as the game's age fraction (port choice: pup, adult and elder words of the game).</summary>
    void AnimalAge(int choice) => Send(new SetAnimalAge(_target, choice switch { 0 => 0.3f, 1 => 1f, _ => 1.3f }) { Tick = context.Tick });

    static string StatName(StatsEnumerated stat) => stat switch
    {
        StatsEnumerated.MeleeAttack => "Melee Attack",
        StatsEnumerated.MeleeDefence => "Melee Defence",
        StatsEnumerated.MartialArts => "Martial Arts",
        StatsEnumerated.HeavyWeapons => "Heavy Weapons",
        StatsEnumerated.FriendlyFire => "Precision Shooting",
        StatsEnumerated.SmithingWeapon => "Weapon Smith",
        StatsEnumerated.SmithingArmour => "Armour Smith",
        StatsEnumerated.SmithingBow => "Bow Smith",
        StatsEnumerated.Medic => "Field Medic",
        _ => stat.ToString(),
    };
}
