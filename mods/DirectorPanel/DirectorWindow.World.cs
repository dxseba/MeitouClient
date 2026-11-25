using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.DirectorPanel;

/// <summary>The NPC Spawn, Relations, Research, Behaviour and Barbershop tabs.</summary>
internal sealed partial class DirectorWindow
{
    int _spawnList, _attitude = 1;
    bool _joinSquad, _hideKnown, _linkBoth = true;
    Entry? _spawnRecord, _spawnFaction, _relationFaction, _pairA, _pairB, _research, _package;

    int PairA => FactionIndex(_pairA);

    int PairB => FactionIndex(_pairB);

    static int FactionIndex(Entry? faction) => faction is not null && int.TryParse(faction.Id, out int i) ? i : -1;

    IEnumerable<UiNode> SpawnTab()
    {
        yield return Heading("sp-h", "NPC Spawn");
        yield return Choices("sp-list", ["Characters", "Animals and machines"], _spawnList, k => _spawnList = k);
        var list = _spawnList == 0 ? catalog.People : catalog.Animals;
        foreach (var n in PickList("sp", list, _spawnRecord?.Id, e => _spawnRecord = e)) yield return n;

        yield return Label("sp-picked", texts["Selected:"] + " " + (_spawnRecord?.Name ?? ""));
        yield return Heading("sp-set", "Spawn settings:");
        yield return LabelledField("sp-count", "Quantity:", "1");
        yield return LabelledField("sp-dist", "Distance, metres:", "5");
        yield return Tick("sp-join", "Join player squad", _joinSquad, v => _joinSquad = v);
        if (!_joinSquad)
        {
            yield return Hint("sp-fhint", "A character carries no faction of its own: the squad gives it one. So pick a faction yourself.");
            yield return Label("sp-faction", texts["Faction:"] + " " + (_spawnFaction?.Name ?? texts["No faction selected"]));
            foreach (var n in PickList("spf", catalog.Factions, _spawnFaction?.Id, f => _spawnFaction = f, rows: 5)) yield return n;
            yield return Label("sp-att", texts["Attitude to the player:"]);
            yield return Choices("sp-attitude", ["Hostile", "Neutral"], _attitude, a => _attitude = a);
        }

        yield return Button("sp-go", texts["Spawn"], SpawnPicked, 160, _spawnRecord is not null && !_target.IsNone);
    }

    void SpawnPicked()
    {
        if (_spawnRecord is not { } record) return;

        int count = Math.Clamp((int)Number("sp-count", 1), 1, 100);
        float distance = Math.Clamp(Number("sp-dist", 5), 0, 500);
        Send(new Spawn(record.Id, count, distance, _target, FactionIndex(_spawnFaction), _joinSquad, _attitude == 0) { Tick = context.Tick });
    }

    IEnumerable<UiNode> RelationsTab()
    {
        if (View is not { } view) yield break;

        yield return Heading("re-h", "Faction's attitude to the player:");
        yield return Hint("re-hint", "The relation is set both ways at once. Minus means enemies, zero neutral, plus allies.");
        foreach (var n in PickList("re", catalog.Factions, _relationFaction?.Id, f => _relationFaction = f, rows: 8,
            f => $"{f.Name}  ({F0(Relation(view.ToPlayer, f))})"))
            yield return n;

        var set = Row("re-set");
        set.Children.Add(Label("re-val-label", texts["Relation:"], width: 100));
        set.Children.Add(Field("re-val", 80, "0"));
        set.Children.Add(Button("re-go", texts["Apply new relation"], () => Relate(FactionIndex(_relationFaction), view.PlayerFaction, true), 220,
            _relationFaction is not null));
        yield return set;

        foreach (var n in PairRows(view)) yield return n;
    }

    static float Relation(IReadOnlyList<float> row, Entry faction) => FactionIndex(faction) is var i and >= 0 && i < row.Count ? row[i] : 0;

    IEnumerable<UiNode> PairRows(DirectorView view)
    {
        yield return Heading("pa-h", "Set relations between two factions:");
        yield return Label("pa-a", texts["First:"] + " " + (_pairA?.Name ?? ""));
        foreach (var n in PickList("paa", catalog.Factions, _pairA?.Id, f => _pairA = f, rows: 5)) yield return n;
        yield return Label("pa-b", texts["Second:"] + " " + (_pairB?.Name ?? ""));
        foreach (var n in PickList("pab", catalog.Factions, _pairB?.Id, f => _pairB = f, rows: 5)) yield return n;

        if (_pairA is not null && _pairB is not null)
            yield return Label("pa-now", texts["Current relation"] + $": {F0(view.PairAB)} / {F0(view.PairBA)}");

        var set = Row("pa-set");
        set.Children.Add(Field("pa-val", 80, "0"));
        set.Children.Add(Tick("pa-both", "Link both directions", _linkBoth, v => _linkBoth = v, 220));
        set.Children.Add(Button("pa-go", texts["Apply new relation"], () => Relate(PairA, PairB, _linkBoth, "pa-val"), 220));
        yield return set;
    }

    void Relate(int a, int b, bool both, string field = "re-val") =>
        Send(new SetRelation(a, b, Math.Clamp(Number(field, 0), -100, 100), both) { Tick = context.Tick });

    IEnumerable<UiNode> ResearchTab()
    {
        var done = View?.Researched ?? new HashSet<string>();
        yield return Hint("rs-hint", "Granted directly, with no blueprints or bench. Already known ones are marked.");
        yield return Tick("rs-hide", "Hide known", _hideKnown, v => _hideKnown = v);
        var shown = catalog.Research.Where(r => !_hideKnown || !done.Contains(r.Id));
        foreach (var n in PickList("rs", shown, _research?.Id, r => _research = r, rows: 18, r => (done.Contains(r.Id) ? "[x] " : "") + r.Name))
            yield return n;

        yield return Button("rs-go", texts["Learn"], () => Send(new Learn(_research!.Id) { Tick = context.Tick }), 160, _research is not null);
    }

    IEnumerable<UiNode> BehaviourTab()
    {
        yield return Heading("be-h", "Behaviour");
        yield return Hint("be-hint", "Behaviour is defined by a package attached to a squad, not to a single character. Leave the separate option on so only this character gets it.");
        if (Target is { } target) yield return Label("be-now", texts["Now:"] + " " + string.Join(", ", target.Packages));

        foreach (var n in PickList("be", catalog.Packages, _package?.Id, p => _package = p, rows: 16)) yield return n;

        var line = Row("be-go");
        line.Children.Add(Button("be-apply", texts["Apply behaviour"], () => Send(new SetBehaviour(_target, _package!.Id, _separate) { Tick = context.Tick }), 220,
            _package is not null && !_target.IsNone));
        line.Children.Add(Button("be-restore", texts["Restore normal behaviour"], () => Send(new SetBehaviour(_target, null) { Tick = context.Tick }), 260,
            Target?.PanelChangedBehaviour == true));
        yield return line;
        yield return Tick("be-sep", "Separate into own squad", _separate, v => _separate = v);
        foreach (var n in StanceRows()) yield return n;
        foreach (var n in PoseRows()) yield return n;
    }

    IEnumerable<UiNode> BarbershopTab()
    {
        yield return Heading("ba-h", "Appearance:");
        yield return Hint("ba-hint", "This opens the same editor used at character creation. Everything changed there applies to the selected character, including the idle pose.");
        yield return Button("ba-go", texts["Open appearance editor"], OpenEditor, 260, !_target.IsNone);
    }

    void OpenEditor()
    {
        _note = texts[context.Interface.OpenScreen(ModScreen.CharacterEditor, ScreenTarget.Of(_target)) ? "Editor opened" : "Editor unavailable"];
    }
}
