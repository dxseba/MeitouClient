using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.CheatMenu;

/// <summary>The pages' contents, in the original's order and wording (README "Facts").</summary>
internal sealed partial class CheatWindow
{
    UiNode Build()
    {
        var root = new UiNode("cheat-root") { Placement = UiPlacement.Fill, Pickable = false };
        if (!_open) return root;

        var window = new UiNode("cheat-window")
        {
            Placement = new(Kit.Width, Kit.Height, Left: 120, Top: 80),
            Style = _styles.Window,
            Flow = UiFlow.Column,
            Padding = 12,
            Gap = Kit.Gap,
        };
        window.Children.Add(Title());
        var body = Kit.Scroll("cheat-body", Kit.Height - 4 * (Kit.Line + Kit.Gap) - 24, _styles.List);
        foreach (var node in Content()) body.Children.Add(node);
        window.Children.Add(body);
        window.Children.Add(Kit.Label("cheat-status", _status, _styles.Text));
        if (_page != Page.Main) window.Children.Add(Kit.Button("cheat-back", Tr("Back"), _styles.Button, _actions.Page, Back(_page).ToString()));
        root.Children.Add(window);
        return root;
    }

    UiNode Title()
    {
        var row = Kit.Row("cheat-title");
        row.Children.Add(Kit.Label("cheat-title-text", Tr("Cheat Menu (F8)"), _styles.Head));
        row.Children.Add(Kit.Button("cheat-close", "X", _styles.Button, _actions.Close, width: Kit.Line));
        return row;
    }

    IEnumerable<UiNode> Content() => _page switch
    {
        Page.Squad => SquadPage(),
        Page.Selected => SelectedPage(),
        Page.Base => BasePage(),
        Page.World => WorldPage(),
        Page.Items => ItemsPage(),
        Page.Rules => RulesPage(),
        Page.Factions => FactionsPage(),
        Page.Fun => FunPage(),
        Page.Experimental => ExperimentalPage(),
        Page.Skills => SkillsPage(),
        Page.Towns => TownsPage(),
        Page.Squads => SquadsPage(),
        Page.SkillXp => SkillXpPage(),
        _ => MainPage(),
    };

    IEnumerable<UiNode> MainPage()
    {
        yield return Head("Quick cheats");
        yield return Switch(CheatMenu.Toggle.GodMode, "God mode (squad)");
        yield return Switch(CheatMenu.Toggle.NoHunger, "No hunger");
        yield return Switch(CheatMenu.Toggle.NoEncumbrance, "No encumbrance");
        yield return Do("Heal squad");
        yield return Do("+10,000 cats");
        yield return Field("cheat-cats", _cats, _actions.Cats, 12);
        yield return Do("Set cats to the amount above");
        yield return Head("Categories");
        foreach (var (page, label) in Categories)
            yield return Kit.Button("cheat-cat-" + page, Tr(label), _styles.Button, _actions.Page, page.ToString());
        yield return Head("Options");
        yield return Kit.Button("cheat-key", MenuKeyText(), _styles.Button, _actions.Act, "Menu key");
    }

    static readonly (Page Page, string Label)[] Categories =
    [
        (Page.Squad, "Squad..."), (Page.Selected, "Selected character..."), (Page.Base, "Base and research..."), (Page.World, "World and travel..."),
        (Page.Items, "Spawn items..."), (Page.Rules, "Game rules..."), (Page.Factions, "Factions..."), (Page.Fun, "Fun modes..."),
        (Page.Experimental, "Experimental..."),
    ];

    string MenuKeyText()
    {
        if (_rebinding) return Tr("Press a key (Esc cancels)");

        string key = MenuKey?.Keys is { Count: > 0 } keys ? keys[0].ToString() : "-";
        return $"{Tr("Menu key")}: {key}";
    }

    IEnumerable<UiNode> SquadPage()
    {
        yield return Head("Protection");
        yield return Switch(CheatMenu.Toggle.WeatherImmunity, "Weather immunity (squad)");
        yield return Switch(CheatMenu.Toggle.PerfectStealth, "Perfect stealth (squad)");
        yield return Switch(CheatMenu.Toggle.Weightless, "Weightless items (squad)");
        yield return Switch(CheatMenu.Toggle.InfiniteConsumables, "Infinite consumables (squad)");
        yield return Head("Multipliers");
        yield return Cycle(Tier.MoveSpeed);
        yield return Cycle(Tier.Damage);
        yield return Cycle(Tier.SkillXp);
        yield return Kit.Button("cheat-skillxp", Tr("XP per skill..."), _styles.Button, _actions.Page, Page.SkillXp.ToString());
        yield return Head("Actions");
        foreach (string act in SquadActs)
            yield return Do(act);
        yield return Cycle(Tier.MaxSquadSize);
    }

    static readonly string[] SquadActs =
        ["Restore lost limbs", "Wake up squad", "Fill hunger (squad)", "Clear bounties", "Free slaves, remove shackles"];

    IEnumerable<UiNode> SelectedPage()
    {
        yield return Head("Character");
        foreach (string act in (string[])["Max skills (selected)", "Edit skills (selected)...", "Edit appearance and race"]) yield return Do(act);
        yield return Field("cheat-name", _name, _actions.Name, 40);
        foreach (string act in SelectedActs) yield return Do(act);
    }

    static readonly string[] SelectedActs =
    [
        "Rename character", "Rename squad", "No hunger: toggle selected", "Teleport selected to camera", "Clone selected character",
        "Animal: next age stage", "Recruit selected NPC", "Recruit selected, new squad", "Knock out selected", "Remove limbs (selected)",
        "Kill selected NPC", "Restock selected trader", "Despawn selected NPC",
    ];

    IEnumerable<UiNode> BasePage()
    {
        yield return Head("Research");
        yield return Cycle(Tier.Construction);
        yield return Cycle(Tier.Production);
        yield return Cycle(Tier.Research);
        yield return Switch(CheatMenu.Toggle.InstantResearch, "Instant research");
        yield return Do("Learn all blueprints");
        yield return Do("Finish buildings near camera");
        foreach (var node in BaseRows()) yield return node;
    }

    IEnumerable<UiNode> WorldPage()
    {
        yield return Head("Travel");
        yield return Kit.Button("cheat-towns", Tr("Travel to town..."), _styles.Button, _actions.Page, Page.Towns.ToString());
        yield return Do("Reveal all towns");
        yield return Kit.Button("cheat-speed", SpeedText(), _styles.Button, _actions.Act, "Game speed");
        yield return Do("Skip to morning (7:00)");
        yield return Do("Skip to night (21:00)");
        yield return Kit.Button("cheat-squads", Tr("Spawn squad at camera..."), _styles.Button, _actions.Page, Page.Squads.ToString());
        foreach (var node in WorldRows()) yield return node;
    }

    IEnumerable<UiNode> RulesPage()
    {
        yield return Head("Health");
        foreach (var tier in (Tier[])[Tier.Bleeding, Tier.BloodRecovery, Tier.WoundHealing, Tier.KnockoutTime, Tier.Starving])
            yield return Cycle(tier);
        yield return Head("Trade and crime");
        foreach (var tier in (Tier[])[Tier.ItemPrices, Tier.CarryWeight, Tier.PrisonTime]) yield return Cycle(tier);
    }

    IEnumerable<UiNode> FunPage()
    {
        yield return Head("NPC modes");
        yield return Switch(CheatMenu.Toggle.Beasts, "Every NPC is a 100-skill beast");
        yield return Switch(CheatMenu.Toggle.Weaklings, "Every NPC is a weakling");
        yield return Switch(CheatMenu.Toggle.InvincibleNpcs, "Invincible NPCs");
        yield return Switch(CheatMenu.Toggle.FastNpcs, "Fast NPCs (x3 run speed)");
        yield return Switch(CheatMenu.Toggle.StrongNpcs, "Strong NPCs (x10 damage)");
        yield return Do("Random skills for every NPC");
        yield return Switch(CheatMenu.Toggle.Chaos, "Chaos: random relations");
        yield return Switch(CheatMenu.Toggle.Raids, "Raids: random squad every 3 min");
        yield return Do("All factions at war");
        yield return Do("Everyone hates you");
        yield return Head("Near the camera");
        foreach (string act in NearActs) yield return Do(act);
    }

    static readonly string[] NearActs =
    [
        "Heal everyone near camera", "Recruit everyone near camera", "Knock out everyone near camera", "Kill everyone near camera",
        "Remove limbs near camera",
    ];

    IEnumerable<UiNode> ExperimentalPage()
    {
        yield return Head("Experimental");
        yield return Kit.Label("cheat-warn1", Tr("These can break your save."), _styles.Text);
        yield return Kit.Label("cheat-warn2", Tr("Back it up first."), _styles.Text);
        yield return Field("cheat-hour", _hour, _actions.Hour, 6);
        yield return Do("Set time to the hour above");
        yield return Do("Remove every NPC's limbs");
        foreach (var node in ExperimentalRows()) yield return node;
    }

    UiNode Head(string english) => Kit.Label("cheat-head-" + english, Tr(english), _styles.Head);

    UiNode Do(string english) => Kit.Button("cheat-act-" + english, Tr(english), _styles.Button, _actions.Act, english);

    UiNode Field(string id, string text, string action, int max) => Kit.TextBox(id, text, action, 0, max);

    /// <summary>A switch: the original's tick button, drawn as a button with a box before its text.</summary>
    UiNode Switch(Toggle which, string english)
    {
        bool on = Running?.View.On[(int)which] ?? false;
        return Kit.Button("cheat-sw-" + which, (on ? "[X] " : "[  ] ") + Tr(english), _styles.Button, _actions.Toggle, which.ToString());
    }

    /// <summary>A tier: "label: xN", or "label: off" / "label: normal" at its first value (Max squad size: its number).</summary>
    UiNode Cycle(Tier which)
    {
        int index = Running?.View.Tier[(int)which] ?? 0;
        float value = Tiers.Values(which)[index];
        string shown = index == 0 ? Tr(Tiers.IsRule(which) ? "normal" : "off")
            : which == Tier.MaxSquadSize ? ((int)value).ToString(CultureInfo.InvariantCulture)
            : "x" + value.ToString("0.##", CultureInfo.InvariantCulture);
        return Kit.Button("cheat-tier-" + which, $"{Tr(Tiers.Label(which))}: {shown}", _styles.Button, _actions.Tier, which.ToString());
    }
}
