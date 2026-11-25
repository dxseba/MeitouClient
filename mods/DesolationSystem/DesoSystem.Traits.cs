using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Population;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The patched perks' per-character table (<see cref="Traits"/>, the fighter's reach), carry-to-recruit (Wilderness and Cybernetic realms) and
/// Grand Fletcher's quiver, every <see cref="Cadence"/> ticks.
/// </summary>
sealed partial class DesoSystem
{
    /// <summary>Squads with a Prosthetic Tuning character in them (serial step).</summary>
    HashSet<int> TunedSquads { get; set; } = [];

    static readonly MethodInfo? JoinPlayer = AccessTools.Method(typeof(DialogueSystem), "JoinPlayer");

    void Refresh(World world, CharacterCold cold, CharacterStats s)
    {
        var cfg = _cfg;
        bool scoped = Gates.InScope(cold, cfg.ScopeIndex);
        bool shadow = Gates.Reached(cold, cfg, Gates.Realm.Shadow);
        var traits = new Traits(
            DodgeRate: scoped && cfg.P19 ? ShadowStep(s) : 1,
            Phantom: scoped && cfg.P208 && Gates.L(s, Dexterity) >= cfg.CounterThreshold,
            MasterKey: scoped && cfg.P26 && All(s, cfg.MasterKeyLevel, Lockpicking, Engineering, Science),
            SureKill: shadow && cfg.AscShdKill,
            BlueEye: shadow && cfg.AscShdEye,
            NoCrime: shadow && cfg.AscShdCrime,
            SilentTouch: scoped && cfg.P25 && All(s, cfg.SilentLevel, Stealth, Assassination),
            Verdict: VerdictOf(cold, s, cfg),
            PorterSpeed: scoped && cfg.P27 && Perks.Porter(s, cfg) ? 1.25f : 1,
            Artifice: cfg.AscArtifice && cold.IsPlayer && cfg.Ascension ? Artifice(s) : 0);
        TraitTable.Set(cold, traits);
        if (cold.Fighter is { } fighter) TraitTable.SetReach(fighter, Reach(cold, s, cfg, scoped));
    }

    static bool All(CharacterStats s, float level, params Data.Gameplay.Bodies.StatsEnumerated[] stats) => stats.All(x => Gates.L(s, x) >= level);

    /// <summary>Shadow Step: the lower of dexterity and dodge, 60+: recovery cut 30 / 37.5 / 60 %, then 12 % per 10, at most 80 %; as a play rate.</summary>
    static float ShadowStep(CharacterStats s)
    {
        float level = Math.Min(Gates.L(s, Dexterity), Gates.L(s, Dodge));
        if (level < 60) return 1;

        float cut = Math.Min(0.8f, Mastery.Value(level, 0.30f, 0.60f));
        return 1 / (1 - cut);
    }

    static Verdict VerdictOf(CharacterCold cold, CharacterStats s, DesoSettings cfg)
    {
        if (!cfg.Assassinate || !cold.IsPlayer || Gates.L(s, Assassination) <= 60) return Verdict.None;

        return cfg.AssassinateMode switch
        {
            DesoSettings.VerdictSneak => Verdict.Sneak,
            DesoSettings.VerdictHarvest => Verdict.Harvest,
            _ => Verdict.None,
        };
    }

    /// <summary>Heavenly Artifice: the base sum of the three smithing skills, strength, science, engineering and perception, from 300 to 600.</summary>
    static float Artifice(CharacterStats s)
    {
        float sum = Gates.L(s, SmithingWeapon) + Gates.L(s, SmithingArmour) + Gates.L(s, SmithingBow) + Gates.L(s, Strength) + Gates.L(s, Science)
            + Gates.L(s, Engineering) + Gates.L(s, Perception);
        return Math.Clamp((sum - 300) / 300, 0, 1);
    }

    /// <summary>The reach factor: Heavy Onslaught or Polearm Supremacy +5 / 8.75 / 20 % with that weapon above the threshold; Universal x1.5.</summary>
    static float Reach(CharacterCold cold, CharacterStats s, DesoSettings cfg, bool scoped)
    {
        float reach = 1;
        if (scoped && cold.Fighter is { Unarmed: false } f)
        {
            var skill = f.WeaponSkill;
            bool mastered = skill == HeavyWeapons && cfg.P11 || skill == Polearms && cfg.P12;
            float level = Gates.L(s, skill);
            if (mastered && level > cfg.MasteryThreshold) reach += Mastery.Value(level, 0.05f, 0.20f);
        }

        if (cfg.AscMartReach && Gates.Reached(cold, cfg, Gates.Realm.Universal)) reach *= 1.5f;
        return reach;
    }

    /// <summary>
    /// Carry to recruit: a Wilderness Sovereign carrying a beast, or a Cybernetic Overlord carrying a robot of a robot race, makes it join the
    /// player (the dialogue's own join).
    /// </summary>
    void Recruit(World world, CharacterCold cold)
    {
        var cfg = _cfg;
        if (cold.Carrying.IsNone || !world.Characters.TryResolveNext(cold.Carrying, out int slot)) return;
        if (world.Characters.Cold(slot) is not { IsPlayer: false, Medical.Dead: false } carried || JoinPlayer is null) return;

        bool beast = Gates.IsAnimal(carried) && cfg.AscWldRecruit && Gates.Reached(cold, cfg, Gates.Realm.Wilderness);
        bool machine = carried.Race?.IsRobot == true && cfg.AscCybRecruit && Gates.Reached(cold, cfg, Gates.Realm.Cyber);
        if (!beast && !machine) return;

        if (JoinPlayer.Invoke(null, [world, population, slot]) is true) world.Post($"{carried.Name} joined your squad.");
    }

    /// <summary>
    /// Grand Fletcher (crossbow smithing 60+): the held crossbow's bolt bundles hold 2 + 0.05 per level above 60 times their base charges, refill to
    /// it every 60 s and gain a full bundle every 240 s, both faster by half the level-60 speed per 20 levels (at least every 1 s).
    /// </summary>
    void Fletcher(World world, CharacterCold cold, CharacterStats s)
    {
        float level = Gates.L(s, SmithingBow);
        if (!_cfg.P212 || !Gates.InScope(cold, _cfg.ScopeIndex) || level < 60 || cold.Fighter?.Crossbow is not { } bow) return;
        if (cold.Inventory is not { } inventory || population.Items.Info(bow.Data.AmmoId) is not { } info) return;

        float speed = 1 + 0.5f * (level - 60) / 20;
        long refill = Math.Max(Cadence, Ticks(world, Math.Max(1, 60 / speed)) / Cadence * Cadence);
        long copy = Math.Max(Cadence, Ticks(world, Math.Max(1, 240 / speed)) / Cadence * Cadence);
        float capacity = MathF.Max(1, info.Charges) * (2 + 0.05f * (level - 60));
        foreach (var bundle in inventory.All().Where(i => i.Record == bow.Data.AmmoId))
        {
            bundle.MaxCharges = capacity;
            if (world.Tick % refill == 0) bundle.Charges = capacity;
        }

        if (world.Tick % copy == 0 && population.Items.Create(bow.Data.AmmoId) is { } extra)
        {
            extra.MaxCharges = capacity;
            extra.Charges = capacity;
            inventory.Add(extra);
        }
    }
}
