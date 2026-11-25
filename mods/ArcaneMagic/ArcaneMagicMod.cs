using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;

namespace Meitou.ArcaneMagic;

/// <summary>
/// Port of Arcane Magic - Wizards of Kenshi (Workshop 3784221846, its plugin <c>plugin/ArcaneMagicNative.dll</c> v0.75.1): the Arcane skill,
/// mana, the five spells cast with the Arcane Focus, the Magi's spells unlocked by their defeat (README). The original's data mod (the Focus,
/// the Magi, factions, towns, the Summonathan) is the required item and loads as data.
/// </summary>
public sealed class ArcaneMagicMod : IMod, IModDataPhase
{
    /// <summary>
    /// The Focus needs no ammunition (the original fakes its ammunition for every check): its <c>ammo</c> reference is removed, so Meitou's crossbow
    /// never runs dry, and one release is one cast (<c>num shots</c> 1; the original sets its magazine to the volley, README "Port choices").
    /// </summary>
    public void EditData(ModDataContext context)
    {
        if (context.Data.Find(ArcaneRules.Focus) is not { } focus)
        {
            context.Log($"{ArcaneRules.Focus} (Arcane Focus Orb) not loaded: enable the original Arcane Magic data mod");
            return;
        }

        var edit = context.Edit(ArcaneRules.Focus);
        var ammo = focus.GetReferences("ammo");
        if (ammo.Count > 0)
            edit.References["ammo"] = [.. ammo.Select(a => new FcsReference(a.TargetStringId, int.MaxValue, int.MaxValue, int.MaxValue))];

        edit.Ints["num shots"] = 1;
    }

    public void Initialize(ModContext context)
    {
        var skill = context.AddSkill(new ModSkill("arcanemagic.arcane", "Arcane", SkillColumn.Weapons)
        {
            Description = "Skill at attuning to and directing an Arcane Focus. Greater mastery allows increasingly efficient control over its "
                + "mysterious energies.",
            Affects = ["Mana capacity", "Mana regeneration", "Magic Missile damage", "Projectiles per volley", "Magic Missile mana cost"],
            Training = ["Using an Arcane Focus"],
            Start = 0,
        });

        if (!FocusPatches.Install(context)) return;

        context.AddSystem("arcane", build => new ArcaneSystem(context, skill, build.Find<CombatSystem>(), build.Find<PopulationSystem>()),
            SystemAnchor.End);
        FocusAnimation.Patch(context);
        SpellModels.Patch(context);
        FocusGrade.Patch(context);
        SaveKeys.Patch(context);
        ArcaneUi.Install(context);
        context.Log("Arcane Magic initialized");
    }
}
