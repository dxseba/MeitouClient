using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The body perks, every <see cref="Cadence"/> ticks for every living character: stat and carry modifiers under the mod's keys, the regeneration
/// perks, the chef's feeding, and the per-body table the Harmony patches read (<see cref="DesoPatches"/>).
/// </summary>
sealed partial class DesoSystem : IPartMaxFilter, INeedRateFilter
{
    static readonly StatsEnumerated[] Core = [Strength, Dexterity, Toughness, Perception];

    void Body(World world)
    {
        float seconds = Cadence * world.TickSeconds;
        bool feed = world.Tick % (Cadence * 30) == 0;
        var auras = Auras(world);
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Stats: { } stats, Medical: { Dead: false } medical } cold) continue;

            var id = table.IdOf(slot);
            Modifiers(id, cold, stats, medical, auras);
            Carry(cold, stats);
            DesoPatches.SetGuard(medical, GuardOf(cold, stats));
            Refresh(world, cold, stats);
            if (cold.Race is { } race) Regen(world, id, cold, stats, medical, race, seconds);
            if (feed) Feed(world, cold, stats);
            Recruit(world, cold);
            Fletcher(world, cold, stats);
        }
    }

    /// <summary>Field Nutrition auras by squad: the best nutritionist's bonus, +1 per 10 medic above 60, at most 4.</summary>
    Dictionary<int, Aura> Auras(World world)
    {
        var auras = new Dictionary<int, Aura>();
        if (!_cfg.P202) return auras;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Stats: { } s, Medical.Dead: false, SquadId: >= 0 } cold || !Perks.Nutritionist(s)) continue;
            if (!Gates.InScope(cold, _cfg.ScopeIndex)) continue;

            float bonus = Math.Min(4, MathF.Floor((Gates.L(s, Medic) - 60) / 10));
            if (bonus > auras.GetValueOrDefault(cold.SquadId).Bonus) auras[cold.SquadId] = new Aura(table.IdOf(slot), bonus);
        }

        return auras;
    }

    void Modifiers(CharacterId id, CharacterCold cold, CharacterStats s, MedicalState medical, Dictionary<int, Aura> auras)
    {
        var cfg = _cfg;
        bool scoped = Gates.InScope(cold, cfg.ScopeIndex);
        var mods = s.Modifiers;
        float perception = scoped && cfg.P17 && Gates.L(s, Perception) >= 20 ? Math.Min(10, MathF.Floor(Gates.L(s, Perception) / 10)) : 0;
        Set(mods, "perception", MeleeAttack, perception);
        Set(mods, "perception", MeleeDefence, perception);
        Set(mods, "evasion", Dodge, scoped && cfg.P205 && Gates.L(s, Dodge) > 60 ? Mastery.Value(Gates.L(s, Dodge), 0, 20) : 0);
        float swim = scoped && cfg.P211 && Gates.L(s, Swimming) > 60 ? 0.2f * (Gates.L(s, Swimming) - 60) : 0;
        float aura = scoped && auras.TryGetValue(cold.SquadId, out var a) && a.Source != id ? a.Bonus : 0;
        foreach (var stat in Core)
        {
            Set(mods, "aquatic", stat, swim);
            Set(mods, "nutrition", stat, aura);
        }

        Set(mods, "saber", MeleeDefence, Saber(cold, s, cfg, scoped));
        Realms(cold, s, mods, cfg);
        LastLight(s, medical, mods, scoped && cfg.P18 && Gates.L(s, Toughness) >= 80);
    }

    /// <summary>Saber Guard's parry: +1 / 2.75 / 8 melee defence with a sabre in hand above the threshold.</summary>
    static float Saber(CharacterCold cold, CharacterStats s, DesoSettings cfg, bool scoped)
    {
        float level = Gates.L(s, Sabres);
        bool held = cold.Fighter is { Unarmed: false, WeaponSkill: Sabres };
        return scoped && cfg.P8 && held && level > cfg.MasteryThreshold ? Mastery.Value(level, 1, 8) : 0;
    }

    void Realms(CharacterCold cold, CharacterStats s, StatModifiers mods, DesoSettings cfg)
    {
        bool universal = cfg.AscMartBlock && Gates.Reached(cold, cfg, Gates.Realm.Universal);
        Set(mods, "universal", MeleeDefence, universal ? 0.5f * Gates.L(s, MeleeDefence) : 0);

        bool pack = cfg.AscWldBuff && Gates.IsAnimal(cold) && _view.WildSquads.Contains(cold.SquadId);
        foreach (var stat in Core) Set(mods, "pack", stat, pack ? 0.2f * Gates.L(s, stat) : 0);
    }

    /// <summary>Last Light: every stat +50 % while a head or torso part is below 25 % of its maximum.</summary>
    static void LastLight(CharacterStats s, MedicalState medical, StatModifiers mods, bool qualifies)
    {
        bool low = qualifies && medical.Parts.Any(p => p.Template.Type is BodyPartType.Head or BodyPartType.Torso && p.Fraction < 0.25f);
        foreach (var stat in StatsInfo.RecordStats) Set(mods, "lastlight", stat, low ? 0.5f * Gates.L(s, stat) : 0);
    }

    static void Set(StatModifiers mods, string perk, StatsEnumerated stat, float amount)
    {
        string key = Key + "." + perk;
        if (amount != 0 && float.IsFinite(amount)) mods.Set(key, stat, amount);
        else mods.Remove(key, stat);
    }

    /// <summary>Master Porter: pack and equipment weight halved, taken as the carrying capacity doubled.</summary>
    void Carry(CharacterCold cold, CharacterStats s)
    {
        bool porter = _cfg.P27 && Gates.InScope(cold, _cfg.ScopeIndex) && Perks.Porter(s, _cfg);
        if (porter) s.CarryModifiers.Set(Key + ".porter", CarryTerm.Scale, 1);
        else if (s.CarryModifiers.All.Count > 0) s.CarryModifiers.Remove(Key + ".porter", CarryTerm.Scale);
    }

    /// <summary>Iron Guard's flat reductions and the injury penalty reduction for the patches.</summary>
    DesoPatches.Guard GuardOf(CharacterCold cold, CharacterStats s)
    {
        var cfg = _cfg;
        bool scoped = Gates.InScope(cold, cfg.ScopeIndex);
        var (limb, vital) = scoped && cfg.P16 ? Perks.Guard(Gates.L(s, MeleeDefence)) : (0, 0);
        float injury = scoped && cfg.P206 ? Perks.InjuryReduction(Gates.L(s, Toughness)) : 0;
        bool physical = Gates.Reached(cold, cfg, Gates.Realm.Physical);
        if (cfg.AscPhysInjury && physical) injury = 1;
        bool tuned = cfg.P201 && TunedSquads.Contains(cold.SquadId);
        bool noDark = cfg.AscArtifice && cold.IsPlayer && cfg.Ascension && Artifice(s) > 0;
        return new DesoPatches.Guard(limb, vital, injury, physical && cfg.AscPhysLimb, physical && cfg.AscPhysKo, tuned, noDark);
    }

    public float Max(World world, in PartMaxAsk ask)
    {
        var cfg = _cfg;
        if (!cfg.ToughHp || !world.Characters.TryResolveNext(ask.Character, out int slot) || world.Characters.Cold(slot) is not { Stats: { } s } cold)
            return ask.Max;
        if (!Gates.InScope(cold, cfg.ScopeIndex) || Gates.L(s, Toughness) is var tough && tough <= 60) return ask.Max;

        float rate = ask.Max >= 1000 ? 0.005f : ask.Max >= 500 ? 0.01f : 0.02f;
        return ask.Max * (1 + rate * (Math.Min(tough, 100) - 60));
    }

    public float Rate(World world, in NeedRateAsk ask)
    {
        if (ask.Kind != NeedKind.Hunger || !world.Characters.TryResolveNext(ask.Character, out int slot) || world.Characters.Cold(slot) is not { } cold)
            return ask.Rate;
        if (cold.SquadId < 0) return ask.Rate;

        var view = _view;
        if (_cfg.AscWldSync && Gates.IsAnimal(cold) && view.WildSquads.Contains(cold.SquadId)) return 0;

        return view.ChefSquads.Contains(cold.SquadId) ? ask.Rate * 0.7f : ask.Rate;
    }
}
