using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The regeneration perks over <c>seconds</c> since the last step: Master Hemopoiesis (blood), Battlefield Healing (the most injured flesh part),
/// Nano-Repair (robotic parts and their wear), Physical Sanctification (flesh 1.5 HP/s, blood never below half); and Field Rations' feeding.
/// </summary>
sealed partial class DesoSystem
{
    void Regen(World world, CharacterId id, CharacterCold cold, CharacterStats s, MedicalState medical, RaceData race, float seconds)
    {
        var cfg = _cfg;
        float capacity = medical.Capacity(Context(world, id, cold, race));
        bool scoped = Gates.InScope(cold, cfg.ScopeIndex);
        float medic = Gates.L(s, Medic);
        if (scoped && cfg.P21 && medic > 60) medical.Blood = Math.Min(capacity, medical.Blood + Mastery.Value(medic, 0.05f, 0.2f) * seconds);
        if (scoped && cfg.P204 && medic >= 10) HealWorst(medical, race, medic / 10 * 0.08f * seconds);
        if (scoped && cfg.P23 && Gates.L(s, Robotics) is var robotics && robotics >= 40) Repair(medical, race, robotics, seconds);
        if (cfg.AscCybMaint && Gates.Reached(cold, cfg, Gates.Realm.Cyber)) Repair(medical, race, 100, seconds);
        if (!Gates.Reached(cold, cfg, Gates.Realm.Physical)) return;

        if (cfg.AscPhysRegen) HealWorst(medical, race, 1.5f * seconds);
        if (cfg.AscPhysBlood && medical.Blood < 0.5f * capacity) medical.Blood = 0.5f * capacity;
    }

    /// <summary>Heals the flesh part furthest below its maximum, up to it.</summary>
    static void HealWorst(MedicalState medical, RaceData race, float amount)
    {
        HealthPart? worst = null;
        foreach (var part in medical.Parts)
        {
            if (part.IsRobotic(race) || part.Flesh >= part.MaxHp) continue;
            if (worst is null || part.MaxHp - part.Flesh > worst.MaxHp - worst.Flesh) worst = part;
        }

        if (worst is not null) worst.Flesh = Math.Min(worst.MaxHp, worst.Flesh + amount);
    }

    /// <summary>Nano-Repair: robotic parts above 25 % heal 0.05 HP/s per 10 robotics; from 80, wear repairs 0.2 + 0.5 per further 10 levels HP/s.</summary>
    static void Repair(MedicalState medical, RaceData race, float robotics, float seconds)
    {
        float heal = 0.005f * robotics * seconds;
        float wear = robotics >= 80 ? (0.2f + 0.05f * (robotics - 80)) * seconds : 0;
        foreach (var part in medical.Parts)
        {
            if (!part.IsRobotic(race)) continue;

            if (part.Fraction > 0.25f && part.Flesh < part.MaxHp) part.Flesh = Math.Min(part.MaxHp, part.Flesh + heal);
            if (wear > 0 && part.Wear > 0) part.Wear = Math.Max(0, part.Wear - wear);
        }
    }

    /// <summary>Field Rations: a chef feeds each squadmate below 90 % fullness 5 % of the meter every 10 s.</summary>
    void Feed(World world, CharacterCold chef, CharacterStats s)
    {
        var cfg = _cfg;
        if (!cfg.P22 || chef.SquadId < 0 || !Gates.InScope(chef, cfg.ScopeIndex)) return;
        if (Gates.L(s, Farming) < cfg.Chef || Gates.L(s, Cooking) < cfg.Chef) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Medical: { Dead: false } medical } mate || mate == chef || mate.SquadId != chef.SquadId) continue;

            if (medical.Hunger < 0.9f * MedicalState.MaxHunger)
                medical.Hunger = Math.Min(MedicalState.MaxHunger, medical.Hunger + 0.05f * MedicalState.MaxHunger);
        }
    }
}
