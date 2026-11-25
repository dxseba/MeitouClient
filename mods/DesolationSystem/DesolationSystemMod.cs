using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;

namespace Meitou.DesolationSystem;

/// <summary>
/// Port of Desolation System - Skills &amp; Combat Overhaul (Workshop 3796024680, <c>DesolationSystem.dll</c> and <c>DivineGraceUI.dll</c>): the
/// attack speed cap tiers, the base-stat perks, the eight weapon masteries, the Ryu styles, the profession and synergy perks the engine's hooks
/// reach, and four of the five Supreme Realms in part, each switchable as in the original's ini, with the control cards and the active-perk
/// monitor. The rules follow the original's DLL and its codex (README "Facts").
/// </summary>
public sealed class DesolationSystemMod : IMod
{
    internal static readonly DesoSettings Settings = new();

    /// <summary>The running world's system, for the cards; null before a world starts.</summary>
    internal static volatile DesoSystem? Running;

    /// <summary>The mod's random purpose, for the patches' rolls.</summary>
    internal static RngPurpose Purpose { get; private set; }

    public void Initialize(ModContext context)
    {
        if (context.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) Settings.Language = DesoSettings.Chinese;
        Purpose = context.RngPurpose;
        var group = context.AddSettings(Settings);
        PerkCodex.Load(context);
        var xp = new XpService(context.Population.Bodies.Constants, context.Population.BodyOptions);
        context.AddSystem("desolation", build => Running = new DesoSystem(Settings, context.Population, build.Find<CombatSystem>(), context.RngPurpose)
        {
            Xp = xp,
            Crimes = build.Find<CrimeSystem>(),
        }, SystemAnchor.End);

        var cards = new DesoCards(context, group);
        context.AddFrame(frame => cards.Inspected = Inspected(frame));
        context.AddKeyAction("panel", "Show or hide the Desolation cards and monitor", _ => cards.ToggleHidden(), Binding.Of(Key.F8));
        context.AddKeyAction("scope", "Switch the Desolation scope", _ => cards.CycleScope(), Binding.Of(Key.F8).With(KeyModifiers.Ctrl));
        context.AddKeyAction("realms", "Switch the Supreme Realms on or off", _ => cards.ToggleRealms());
        context.AddKeyAction("pickpocket", "Phantom Pickpocket", key => key.Enqueue(new Pickpocket([.. key.Selected]) { Tick = key.Tick }),
            Binding.Of(Key.V));
        context.AddUi(cards);
        DesoPatches.Install(context);
        context.Log("Desolation System initialized");
    }

    static CharacterIdBox? Inspected(ModFrame frame)
    {
        var id = frame.Shown ?? (frame.Selected.Count > 0 ? frame.Selected[0] : (CharacterId?)null);
        return id is { } c ? new CharacterIdBox(c) : null;
    }
}
