using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Player;

namespace Meitou.DesolationSystem;

/// <summary>
/// Universal Mastery's absolute block (a defence filter: a blow it can block is blocked facing 95 % of the time), Wilderness Sovereign's peaceful
/// beasts and Shadow Sovereign's shaking off attackers while sneaking (engage filters: no fight but in defence). Pure reads, any thread.
/// </summary>
sealed partial class DesoSystem : ICombatDefenceFilter, ICombatEngageFilter
{
    const float AbsoluteBlock = 0.95f;

    public int Defence(World world, in DefenceChoiceAsk ask)
    {
        var cfg = _cfg;
        if (ask.Stumbling || combat is null || !cfg.AscMartBlock || !world.Characters.TryResolveNext(ask.Defender, out int slot)) return ask.Technique;
        if (world.Characters.Cold(slot) is not { Fighter: { CanBlock: true } fighter, Medical: { } medical } cold) return ask.Technique;
        if (!Gates.Reached(cold, cfg, Gates.Realm.Universal)) return ask.Technique;

        var techniques = combat.Techniques;
        if (ask.Technique >= 0 && techniques[ask.Technique] is { IsBlock: true, IsDodge: false } t && t.AttackDirection1 == ask.Direction)
            return ask.Technique;

        ulong seed = Rng.Hash(world.Seed, Rng.Key(ask.Defender), purpose, ((ulong)ask.Tick << 16) ^ ((ulong)(uint)ask.Sequence << 4) ^ (ulong)ask.Blow);
        if (Rng.Float(seed) >= AbsoluteBlock) return ask.Technique;

        int pick = TechniqueChooser.ChooseReaction(techniques, fighter.TechniqueCategory, medical.Prone, true, ask.Direction, 100, 0, 0,
            Rng.Float(Rng.Mix(seed)));
        return pick >= 0 ? pick : ask.Technique;
    }

    public bool Allows(World world, in EngageAsk ask)
    {
        if (ask.Defensive) return true;

        var table = world.Characters;
        if (!table.TryResolveNext(ask.Attacker, out int a) || !table.TryResolveNext(ask.Target, out int t)) return true;
        if (table.Cold(a) is not { } attacker || table.Cold(t) is not { } target) return true;

        var cfg = _cfg;
        bool fear = cfg.AscWldFear && !attacker.IsPlayer && Gates.IsAnimal(attacker) && target.IsPlayer && _view.WildSquads.Contains(target.SquadId);
        bool shaken = cfg.AscShdWatch && !attacker.IsPlayer && (target.Orders & CharacterOrders.Sneak) != 0
            && Gates.Reached(target, cfg, Gates.Realm.Shadow);
        return !fear && !shaken;
    }
}
