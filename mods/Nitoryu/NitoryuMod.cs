using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Nitoryu;

/// <summary>
/// Port of Nitoryu: Dual Wielding (workshop 3812165089, GPL-3.0): a fighter carrying two blades draws both and fights with one in each hand, by
/// a Dual Wielding skill and a Path of five ranks. The original's data (its moves, settings, training dummies, NPC lists) loads as its own
/// <c>.mod</c>; its <c>Nitoryu.ini</c> and its animation files are read at runtime from the installed original (README "Facts").
/// </summary>
public sealed class NitoryuMod : IMod, IModDataPhase, INitoryuApi, IModDependents
{
    internal static readonly NitoryuSettings Settings = new();

    public void EditData(ModDataContext context) => NitoryuData.Edit(context);

    public void Initialize(ModContext context)
    {
        Diag.Context = context;
        string? ini = OriginalFiles.Find(context.Folder, "Nitoryu.ini", context.Log);
        var config = NitoryuConfig.Load(context.Data, ini, context.Log);
        OffHandLook.Grip = OffHandGrip.From(config, context.Log);
        Settings.OnlyMyStrikes = config.OnlyMyStrikes;
        Settings.Guard = config.Guard;
        Settings.Flow = config.Flow;
        context.AddSettings(Settings);

        var skill = context.AddSkill(Skill());
        CombatAccess.Bind(context.Log);
        var session = new NitoryuSession(config, new Pairs(context.Data, config), new NpcRules(context.Data, config), skill, context.RngPurpose,
            context.Population, context.Data, Settings, context.Log, context.Tr);
        context.AddSystem("nitoryu", _ => new NitoryuSystem(session), SystemAnchor.End);
        context.AddKeyAction("toggle", "Toggle dual stance", Toggle);

        AttackChoice.Patch(context);
        CombatPatches.Patch(context);
        PoseLayer.Patch(context);
        Mirror.Patch(context);
        OffHandLook.Patch(context);
        StatsPane.Patch(context, skill);
        context.AddCombatMove(SheathMoves.Source);
        Addons.Enabled = config.AddonsOn;
        var marks = new Marks(config.Marks);
        context.AddUi(marks);
        context.AddFrame(marks.Frame);
        context.AddUi(new InventoryButtons(config.IniRaw("ui.toggle_button") != "0", config.IniRaw("ui.any_weapon_button") != "0"));
        context.Log($"Nitoryu initialized ({(ini is null ? "no Nitoryu.ini: the data's settings" : "Nitoryu.ini read")})");
    }

    static DualFighter? StateOf(World world, CharacterId id) =>
        NitoryuSystem.Of(world) is { } system && system.State.IsDual(id.Slot, id) ? system.State[id.Slot] : null;

    public bool IsDual(World world, CharacterId character) => StateOf(world, character) is not null;

    public int Rank(World world, CharacterId character) => StateOf(world, character)?.Tier ?? 0;

    public float Path(World world, CharacterId character) => StateOf(world, character)?.Path ?? 0;

    public float DualWielding(World world, CharacterId character) => StateOf(world, character)?.Effective ?? 0;

    public void AddListener(string owner, INitoryuListener listener) => Addons.Add(owner, listener);

    public void AddPairRule(string owner, NitoryuPairRule rule) => Addons.Add(owner, rule);

    public void DependentFailed(string modId) => Addons.Drop(modId);

    static void Toggle(Game.Host.Input.ModKeyContext key) => key.Enqueue(new ToggleDualOrder(key.Selected) { Tick = key.Tick });

    /// <summary>The Dual Wielding line under the Weapons column (the original's <c>[Stats] group=1</c>), its pane texts the original's README.</summary>
    static ModSkill Skill() => new("nitoryu.ambidexterity", "Dual Wielding", SkillColumn.Weapons)
    {
        Description = "Fighting with a blade in each hand. With the lower of the two weapon skills, Dexterity, melee attack and melee defence "
            + "it makes the Path (60 % Dual Wielding, 10 % each), which opens the ranks Deshi (20), Bushi (40), Ronin (60, Dual Wielding 40), "
            + "Kenshi (75, 60) and Kensei (90, 80). Heavy and medium armour and overload lower it in a fight.",
        Affects = ["Second blade strikes, twin strikes and specials", "Second blade parries", "Attack and block speed with two blades"],
        Training = ["Fighting with two blades: half the weapon experience"],
        Start = -1,
    };
}
