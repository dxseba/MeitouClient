using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>
    /// One blow of <c>Task_KillPrisoner</c> (goals 134, 135, docs/game/ai-tasks.md "Prisoner fate", <b>Observed</b>): the attacker's weapon packet (or its fists') lands on the target
    /// with no reach, block or dodge test (the target is caged), through its armour and toughness as a landed melee hit; the floater, sound, blood, KO, death and severed-limb
    /// effects follow as in <see cref="Apply"/>. The killing of a captive is no crime (engine choice: the original's call passes no crime). <paramref name="blow"/> numbers the
    /// blows for the armour cover roll. True when the target died of it. Serial phases only.
    /// </summary>
    public bool Execute(World world, int a, int d, int blow)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var ac = table.Cold(a);
        var dc = table.Cold(d);
        if (ac is not { Fighter: { } fighter, Stats: not null, Medical: not null, Race: not null } || dc is not { Medical: { Dead: false } medical, Race: { } race, Stats: not null }) return false;
        float toughness = Eff(dc, StatsEnumerated.Toughness);
        TargetKind kind = race.IsRobot ? TargetKind.Robot : dc.Fighter is { IsAnimal: true } ? TargetKind.Animal : TargetKind.Human;
        float skill = Eff(ac, fighter.WeaponSkill);
        float str = Eff(ac, StatsEnumerated.Strength);
        var strikingItem = !fighter.IsAnimal && !fighter.Unarmed ? fighter.Weapon?.SourceItem : null;
        var packet = fighter.Unarmed
            ? DamageFormulas.UnarmedPacket(UnarmedRating(world, a, ac, fighter), Eff(ac, StatsEnumerated.Toughness), str, fighter.FistsItem, kind, race.StringId, fighter.DamageOutput, combat)
            : DamageFormulas.Packet(skill, Eff(ac, StatsEnumerated.Dexterity) * fighter.DexterityMult, str, fighter.Weapon!, kind, race.StringId, fighter.DamageOutput, combat);
        var mechanism = CombatWoundMechanisms.Classify(fighter, packet);
        float brutality = ac.Brutality.Level;
        var travel = new Vector2(prev[d].Position.X - prev[a].Position.X, prev[d].Position.Z - prev[a].Position.Z);
        ulong headSeed = Wounds.HeadCutPolicy.VisualSeedFor(world.Seed, table.IdOf(d), table.IdOf(a), (ulong)(uint)blow);
        var headCut = dc.Fighter is not { IsAnimal: true }
            ? Wounds.HeadCutPolicy.Candidate(brutality, mechanism, -1, packet.Cut, headSeed, travel, prev[d].Yaw) : null;
        var ctx = MakeContext(world, d, dc) with
        {
            AllowDecapitation = dc.Fighter is not { IsAnimal: true } && BrutalityPolicy.CanDecapitate(brutality, mechanism)
                && headCut is not { Mode: Wounds.HeadCutMode.AttachedSplit },
            HeadCutCandidate = headCut,
        };
        var events = hitEvents;
        events.Clear();
        bool aliveBeforeHit = !medical.Dead;
        bool wasIncapacitated = medical.Incapacitated;
        int partCount = CanDepositWeaponBlood(strikingItem, race, aliveBeforeHit) ? medical.Parts.Count : 0;
        Span<bool> organicBeforeHit = partCount <= 64 ? stackalloc bool[partCount] : new bool[partCount];
        for (int i = 0; i < partCount; i++) organicBeforeHit[i] = !medical.Parts[i].IsRobotic(race);
        ReadOnlySpan<ArmourPiece> armour = dc.Fighter is { } df ? df.Armour : default;
        var cover = new CoverRoll(world.Seed, Rng.Key(table.IdOf(d)), a, blow, 0);
        var (part, damage, absorbed) = HitResolver.LandDetailed(medical, ctx, packet, armour, cover, toughness, false, false, combat, events);
        if ((uint)part < (uint)organicBeforeHit.Length)
            DepositWeaponBlood(strikingItem, race, damage, aliveBeforeHit, organicBeforeHit[part], mechanism);
        FloatHit(world, d, dc, ac.IsPlayer, part, damage, absorbed);
        ref var D = ref write[d];
        D.Taken++;
        D.HitTick = (int)world.Tick;
        D.HitPart = part;
        bool death = false;
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case MedicalEventKind.Died: death = true; break;
                case MedicalEventKind.HeadCut: dc.HeadCut = ctx.HeadCutCandidate; break;
                case MedicalEventKind.Decapitated: table.LimbsChanged.Enqueue((d, -1)); break;   // the look loses its head (nothing drops)
                case MedicalEventKind.PartSevered:
                    xp.ToughnessFromLimbLoss(dc.Stats!, race);
                    table.LimbsChanged.Enqueue((d, medical.Parts[ev.Part].Template.LimbSlot));
                    world.Defer(new Dialogue.DialogueEvent(table.IdOf(d), CharacterId.None, medical.Parts[ev.Part].Template.LimbSlot is 2 or 3 ? 70 : 71) { Tick = world.Tick + 1 });
                    break;
            }
        }
        PublishHit(world, CombatHitSource.Execution, table.IdOf(a), d, medical, part, damage, new Vector2(prev[d].Position.X - prev[a].Position.X, prev[d].Position.Z - prev[a].Position.Z), events, mechanism: mechanism, attackerBrutality: brutality);
        if (medical.Incapacitated)
        {
            D.Down = true;
            D.AttackTech = -1;
            D.NextBlow = 0;
            D.ReactTech = -1;
        }
        var toAttacker = new Vector2(prev[a].Position.X - prev[d].Position.X, prev[a].Position.Z - prev[d].Position.Z);
        Sound(world, SoundKind.Hit, d, a, prev[d].Position, unarmed: fighter.Unarmed, robot: ac.Race!.IsRobot,
            armoured: absorbed.Cut + absorbed.Blunt + absorbed.Pierce > 0);
        Splash(world, d, dc, -toAttacker, damage.Cut + damage.Blunt + damage.Pierce);
        HitRagdoll(world, d, dc, part, -toAttacker, damage.Cut + damage.Blunt + damage.Pierce, wasIncapacitated, death, prev[a].Position);
        if (medical.Incapacitated) Sound(world, SoundKind.Down, d, a, prev[d].Position, fatal: death);
        if (death && world.Uniques.RecordOf(table.IdOf(d)) is { } unique) world.Uniques.Died(unique, ac.IsPlayer, dc.Faction);
        return death;
    }
}
