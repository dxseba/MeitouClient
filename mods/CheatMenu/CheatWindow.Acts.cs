using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>
/// The one-shot buttons, by their English text. Most are the game's own debug commands (heal, money, wake, hunger, bounties, skills, teleport,
/// knock-out, kill, research, recruit), the rest the cheat system's commands; each says what it did on the status line.
/// </summary>
internal sealed partial class CheatWindow
{
    /// <summary>The original's +10,000 cats, Fill hunger's value (the HUD's full meter) and Knock out's seconds (the port's choice).</summary>
    const long Cats = 10_000;
    const float KnockOutSeconds = 60;

    /// <summary>The original's skip hours (<b>Verified</b>, its button texts).</summary>
    const double Morning = 7, Night = 21;

    void Act(string english)
    {
        if (Acts.TryGetValue(english, out var act)) act(this);
        else if (!BaseAct(english) && !WorldAct(english)) Say(english);
    }

    static readonly Dictionary<string, Action<CheatWindow>> Acts = new()
    {
        ["Heal squad"] = w => w.Debug(new DebugHeal(w.Squad) { Tick = w.T }, "Heal squad"),
        ["+10,000 cats"] = w => w.Debug(new DebugMoney(Cats) { Tick = w.T }, "+10,000 cats"),
        ["Set cats to the amount above"] = w => w.SetCats(),
        ["Menu key"] = w => w._rebinding = true,
        ["Restore lost limbs"] = w => w.Debug(new DebugHeal(w.Squad, Limbs: true) { Tick = w.T }, "Restore lost limbs"),
        ["Wake up squad"] = w => w.Debug(new DebugWake(w.Squad) { Tick = w.T }, "Wake up squad"),
        ["Fill hunger (squad)"] = w => w.Queue(CheatMenu.Deed.FillHunger, w.Squad, "Fill hunger (squad)"),
        ["Free slaves, remove shackles"] = w => w.Queue(CheatMenu.Deed.FreeSlaves, [], "Free slaves, remove shackles"),
        ["Clear bounties"] = w => w.Debug(new DebugClearBounties(w.Squad) { Tick = w.T }, "Clear bounties"),
        ["Max skills (selected)"] = w => w.OnTarget(t => new DebugMaxSkills(t) { Tick = w.T }, "Max skills (selected)"),
        ["Edit skills (selected)..."] = w => w.Open(Page.Skills),
        ["Edit appearance and race"] = w => w.EditLook(),
        ["Rename character"] = w => w.Queue(CheatMenu.Deed.RenameCharacter, w.Selected, "Rename character", text: w._name.Trim()),
        ["Rename squad"] = w => w.Queue(CheatMenu.Deed.RenameSquad, w.Selected, "Rename squad", text: w._name.Trim()),
        ["No hunger: toggle selected"] = w => w.Queue(CheatMenu.Deed.ToggleHungerOf, w.Selected, "No hunger: toggle selected"),
        ["Teleport selected to camera"] = w => w.Debug(new DebugTeleport(w.Selected, w._camera) { Tick = w.T }, "Teleport selected to camera"),
        ["Clone selected character"] = w => w.Clone(),
        ["Animal: next age stage"] = w => w.Queue(CheatMenu.Deed.AnimalNextAge, w.Target, "Animal: next age stage"),
        ["Recruit selected NPC"] = w => w.Recruit(w.SelectedNpcs, "Recruit selected NPC"),
        ["Recruit selected, new squad"] = w => w.Recruit(w.SelectedNpcs, "Recruit selected, new squad"),
        ["Knock out selected"] = w => w.OnTarget(t => new DebugKnockOut(t, KnockOutSeconds) { Tick = w.T }, "Knock out selected"),
        ["Remove limbs (selected)"] = w => w.Queue(CheatMenu.Deed.RemoveLimbs, w.Target, "Remove limbs (selected)"),
        ["Kill selected NPC"] = w => w.Debug(new DebugKill(w.SelectedNpcs) { Tick = w.T }, "Kill selected NPC"),
        ["Despawn selected NPC"] = w => w.Queue(CheatMenu.Deed.Despawn, w.SelectedNpcs, "Despawn selected NPC"),
        ["Learn all blueprints"] = w => w.Debug(new DebugUnlockResearch() { Tick = w.T }, "Learn all blueprints"),
        ["Finish buildings near camera"] = w => w.Queue(CheatMenu.Deed.FinishBuildings, [], "Finish buildings near camera", vector: true),
        ["Reveal all towns"] = w => w.Queue(CheatMenu.Deed.RevealTowns, [], "Reveal all towns"),
        ["Game speed"] = w => w.NextSpeed(),
        ["Skip to morning (7:00)"] = w => w.Skip(Morning, "Skip to morning (7:00)"),
        ["Skip to night (21:00)"] = w => w.Skip(Night, "Skip to night (21:00)"),
        ["Random skills for every NPC"] = w => w.Queue(CheatMenu.Deed.RandomSkills, [], "Random skills for every NPC"),
        ["All factions at war"] = w => w.Queue(CheatMenu.Deed.AllAtWar, [], "All factions at war"),
        ["Everyone hates you"] = w => w.Queue(CheatMenu.Deed.EveryoneHates, [], "Everyone hates you"),
        ["Heal everyone near camera"] = w => w.Debug(new DebugHeal(w.Near(own: true)) { Tick = w.T }, "Heal everyone near camera"),
        ["Recruit everyone near camera"] = w => w.Recruit(w.Near(own: false), "Recruit everyone near camera"),
        ["Knock out everyone near camera"] = w =>
            w.Debug(new DebugKnockOut(w.Near(own: false), KnockOutSeconds) { Tick = w.T }, "Knock out everyone near camera"),
        ["Kill everyone near camera"] = w => w.Debug(new DebugKill(w.Near(own: false)) { Tick = w.T }, "Kill everyone near camera"),
        ["Remove limbs near camera"] = w => w.Queue(CheatMenu.Deed.RemoveLimbs, w.Near(own: false), "Remove limbs near camera"),
        ["Ally with all factions"] = w =>
            w.Debug(new DebugSetRelation(w.Population.PlayerFaction, null, Allied) { Tick = w.T }, "Ally with all factions"),
        ["Set time to the hour above"] = w => w.SetHour(),
        ["Remove every NPC's limbs"] = w => w.Queue(CheatMenu.Deed.RemoveLimbs, w.AllNpcs, "Remove every NPC's limbs"),
    };

    /// <summary>The selection, else the character the player is looking at (an NPC's panel).</summary>
    IReadOnlyList<CharacterId> Target => _selected.Count > 0 ? _selected
        : _snapshot is { } s ? [.. s.Characters.Where(c => c.Inspected).Select(c => c.Id)] : [];

    IReadOnlyList<CharacterId> AllNpcs => _snapshot is { } s ? [.. s.Characters.Where(c => !c.IsPlayer).Select(c => c.Id)] : [];

    void Debug(SimCommand command, string english)
    {
        Send(command);
        Say(Tr(english));
    }

    void OnTarget(Func<IReadOnlyList<CharacterId>, SimCommand> make, string english)
    {
        var targets = Target;
        if (targets.Count == 0)
        {
            Say(Tr("Skills: select a character"));
            return;
        }

        Debug(make(targets), english);
    }

    void Queue(Deed which, IReadOnlyList<CharacterId> targets, string english, string text = "", bool vector = false)
    {
        if (which is CheatMenu.Deed.RenameCharacter or CheatMenu.Deed.RenameSquad && text.Length == 0)
        {
            Say(Tr("not changed"));
            return;
        }

        if (vector) Send(new CameraAt(_camera) { Tick = T });
        Send(new DoDeed(which, targets, Text: text) { Tick = T });
        Say(Tr(english));
    }

    void SetCats()
    {
        if (!long.TryParse(_cats.Trim().Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long cats) || cats < 0)
        {
            Say(Tr("not changed"));
            return;
        }

        Send(new DoDeed(CheatMenu.Deed.SetMoney, [], cats) { Tick = T });
        Say($"{Tr("Set cats to the amount above")}: {cats}");
    }

    void SetHour()
    {
        bool ok = double.TryParse(_hour.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double hour) && hour is >= 0 and < 24;
        if (!ok)
        {
            Say(Tr("CheatMenu: set time: type an hour from 0 to 23.99"));
            return;
        }

        Skip(hour, "Set time to the hour above");
    }

    void Skip(double hour, string english) => Say(Clock.SkipTo(hour) ? Tr(english) : Tr("not changed"));

    void EditLook()
    {
        var targets = Target.Where(id => _snapshot?.Characters.FirstOrDefault(c => c.Id == id) is { IsPlayer: true }).ToList();
        bool opened = targets.Count > 0 && context.Interface.OpenScreen(ModScreen.CharacterEditor, new ScreenTarget { Characters = targets });
        Say(opened ? Tr("Edit appearance and race") : Tr("Skills: select a character"));
    }

    void Clone()
    {
        Send(new CameraAt(_camera) { Tick = T });
        Queue(CheatMenu.Deed.Clone, Selected, "Clone selected character");
    }

    void Recruit(IReadOnlyList<CharacterId> who, string english) =>
        Queue(english.EndsWith("new squad", StringComparison.Ordinal) ? CheatMenu.Deed.RecruitNewSquad : CheatMenu.Deed.Recruit, who, english);

    /// <summary>Game speed: the original's tiers 1, 5, 10 and 20, set through the speed buttons' own path.</summary>
    void NextSpeed()
    {
        float[] speeds = [1, 5, 10, 20];
        int now = Array.IndexOf(speeds, (float)context.Interface.Speed);
        context.Interface.SetSpeed(speeds[(now + 1) % speeds.Length]);
    }

    string SpeedText()
    {
        float speed = (float)context.Interface.Speed;
        return $"{Tr("Game speed")}: x{speed.ToString("0.##", CultureInfo.InvariantCulture)}";
    }
}
