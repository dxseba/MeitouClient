using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.ForceAwakening;

/// <summary>Damage, knock-downs, throws and heals from code, and the areas they reach; the sounds and particles of a cast.</summary>
sealed partial class ForceSystem
{
    /// <summary>Distance on the ground plane.</summary>
    static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>The attacker of the damage being dealt now (its slot; -1 none), set round a cast's and a timed effect's steps.</summary>
    int _by = -1;

    /// <summary>
    /// Skill damage as the original's (<b>Verified</b>, its damage helper: scale, the victim's Dodge, then <c>MedicalSystem::addWound</c> with hit
    /// direction 6, which picks the part and applies worn armour and toughness): x <c>skill_damage</c>, x (1 - Dodge/100 x <c>dodge_max</c>), then the
    /// game's own hit landing (part by weight alone, each worn piece's coverage roll, armour, toughness, the difficulty multiplier:
    /// <c>HitResolver.LandDetailed</c>). The attacker gets the victim's aggro and, for a player's character, the Assault crime, as the original's
    /// <c>attackingYou</c> and <c>setCrime(5)</c>. A knock-out or death stops the victim as the game's blows do.
    /// </summary>
    void Hurt(World world, CharacterId id, HitDamage damage)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Medical: { Dead: false } medical, Race: { } race } cold) return;

        float dodge = Math.Clamp(cold.Stats?[StatsEnumerated.Dodge] ?? 0, 0, 100);
        float k = _config.SkillDamage * (1 - dodge / 100 * _config.DodgeMax);
        var packet = new DamagePacket(damage.Cut * k, damage.Blunt * k, damage.Pierce * k, 0, 1, 0);
        Provoke(world, slot, cold);

        var ctx = Context(world, id, cold, race);
        var events = new List<MedicalEvent>();
        var cover = new Cover(world.Seed, Rng.Key(id), purpose, (ulong)world.Tick);
        float toughness = cold.Stats?[StatsEnumerated.Toughness] ?? 0;
        var armour = cold.Fighter is { } fighter ? fighter.Armour : default;
        var (part, _, _) = HitResolver.LandDetailed(medical, ctx, packet, armour, cover, toughness, false, true, population.Combat.Constants, events);
        if (part < 0) return;

        foreach (var e in events)
        {
            if (e.Kind is MedicalEventKind.Died or MedicalEventKind.KnockedOut)
            {
                TaskStart.Stand(table, slot);
                table.Next[slot].Velocity = Vector3.Zero;
            }
            else if (e.Kind == MedicalEventKind.PartSevered) table.LimbsChanged.Enqueue((slot, medical.Parts[e.Part].Template.LimbSlot, true));
            else if (e.Kind == MedicalEventKind.Decapitated) table.LimbsChanged.Enqueue((slot, -1, false));
        }
    }

    /// <summary>The victim turns on the attacker (aggro); a player's attack on a character not its own is an Assault.</summary>
    void Provoke(World world, int victim, CharacterCold vc)
    {
        if (_by < 0 || _by == victim || world.Characters.Cold(_by) is not { } ac || Friend(ac, vc)) return;

        aggro?.Force(world, victim, _by);
        if (ac.IsPlayer && !vc.IsPlayer) crime?.Assault(world, _by, victim, vc.InCombat);
    }

    /// <summary>The armour coverage rolls: one per worn piece from the mod's purpose, the victim and the tick.</summary>
    readonly struct Cover(ulong seed, ulong victim, RngPurpose purpose, ulong tick) : ICoverRoll
    {
        public float Roll(int piece) => Rng.Float(Rng.Hash(seed, victim ^ ((ulong)piece << 48), purpose, tick));
    }

    MedicalContext Context(World world, CharacterId id, CharacterCold cold, RaceData race) =>
        new(population.Bodies.Constants, population.BodyOptions, race)
        {
            Toughness = cold.Stats?.ScaledToughness ?? 0,
            Strength = cold.Stats?.Strength ?? 50,
            HasStats = cold.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(id),
        };

    /// <summary>
    /// A Force blow: the damage, then a knock-down of <paramref name="seconds"/> (the game's knock-out) and, with a direction, a throw of
    /// <paramref name="speed"/> along it with a little lift (<see cref="CharacterCold.Launch"/>, which the ragdoll takes).
    /// </summary>
    void Blow(World world, int slot, HitDamage damage, float seconds, Vector3 away, float speed = ThrowSpeed)
    {
        var table = world.Characters;
        if (slot < 0 || slot >= table.HighWater || table.Cold(slot) is not { Medical: { Dead: false } } cold) return;

        var id = table.IdOf(slot);
        if (damage.Cut + damage.Blunt + damage.Pierce > 0) Hurt(world, id, damage);

        var medical = cold.Medical;
        if (!medical.Dead && !medical.Incapacitated && seconds > 0)
        {
            medical.KnockOut(seconds);
            TaskStart.Stand(table, slot);
            table.Next[slot].Velocity = Vector3.Zero;
        }

        var flat = new Vector3(away.X, 0, away.Z);
        if (speed > 0 && flat.LengthSquared() > 1e-4f && medical.Incapacitated)
            cold.Launch = Vector3.Normalize(flat) * speed + new Vector3(0, speed * 0.25f, 0);
    }

    /// <summary>Heals each part of the character by <paramref name="amount"/> (flesh up to its most, stun down), as the original's heal per part.</summary>
    void HealSlot(World world, int slot, float amount)
    {
        if (world.Characters.Cold(slot)?.Medical is not { Dead: false } medical) return;

        foreach (var part in medical.Parts)
        {
            if (part.MaxHp <= 0) continue;

            part.Flesh = MathF.Min(part.MaxHp, part.Flesh + amount);
            part.Stun = MathF.Max(0, part.Stun - amount);
        }

        Fx(world, "force_heal", world.Characters.IdOf(slot));
    }

    /// <summary>Whether <paramref name="other"/> is on the caster's side (its faction).</summary>
    static bool Friend(CharacterCold caster, CharacterCold other) => other.Faction == caster.Faction;

    /// <summary>The living characters of another faction within <paramref name="radius"/> of <paramref name="at"/>, with the way from it to each.</summary>
    void Around(World world, CharacterCold caster, Vector3 at, float radius, Action<int, Vector3> act)
    {
        var table = world.Characters;
        var hits = new List<(int, Vector3)>();
        for (int s = 0; s < table.HighWater; s++)
        {
            if (!table.Next[s].Alive || table.Cold(s) is not { Medical.Dead: false } c || c == caster || Friend(caster, c)) continue;

            var away = table.Next[s].Position - at;
            if (Flat(away) <= radius) hits.Add((s, away));
        }

        foreach (var (s, away) in hits) act(s, away);
    }

    /// <summary>Those of <see cref="Around"/> within <paramref name="halfAngle"/> degrees of <paramref name="aim"/>.</summary>
    void Cone(World world, CharacterCold caster, Vector3 from, Vector3 aim, float reach, float halfAngle, Action<int, Vector3> act)
    {
        var dir = new Vector2(aim.X, aim.Z);
        if (dir.LengthSquared() < 1e-4f) return;

        dir = Vector2.Normalize(dir);
        float cos = MathF.Cos(halfAngle * MathF.PI / 180);
        Around(world, caster, from, reach, (s, away) =>
        {
            var flat = new Vector2(away.X, away.Z);
            if (flat.LengthSquared() < 1 || Vector2.Dot(Vector2.Normalize(flat), dir) >= cos) act(s, away);
        });
    }

    /// <summary>The caster and the living characters of its faction within <paramref name="radius"/>.</summary>
    void Allies(World world, CharacterCold caster, Vector3 at, float radius, Action<int> act)
    {
        var table = world.Characters;
        var hits = new List<int>();
        for (int s = 0; s < table.HighWater; s++)
        {
            if (!table.Next[s].Alive || table.Cold(s) is not { Medical.Dead: false } c || !Friend(caster, c)) continue;
            if (Flat(table.Next[s].Position - at) <= radius) hits.Add(s);
        }

        foreach (int s in hits) act(s);
    }

    /// <summary>The nearest living enemy within <paramref name="reach"/> of <paramref name="at"/> not in <paramref name="skip"/>; -1 for none.</summary>
    int Nearest(World world, CharacterCold caster, Vector3 at, float reach, HashSet<int> skip)
    {
        int best = -1;
        float bestD = reach;
        Around(world, caster, at, reach, (s, away) =>
        {
            float d = Flat(away);
            if (!skip.Contains(s) && d <= bestD) (best, bestD) = (s, d);
        });
        return best;
    }

    #region Sounds and particles

    /// <summary>
    /// The original's kind aliases for its effect and sound tables (<b>Verified</b>, the DLL's kind table): a kind without lines of its own uses
    /// the RPG Classes kind it was drawn from.
    /// </summary>
    static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal)
    {
        ["force_push"] = "air_sweep", ["force_wave"] = "air_sweep", ["force_leap"] = "crack", ["saber_throw"] = "slash",
        ["deflect"] = "faith_shield", ["deflect_hit"] = "strike", ["force_heal"] = "lay_hands", ["mind_trick"] = "mark",
        ["battle_meditation"] = "devotion_aura", ["force_choke"] = "death_grip", ["force_lightning"] = "strike", ["dark_rage"] = "fury",
        ["thermal_detonator"] = "fire", ["dirty_kick"] = "dash", ["flamethrower"] = "twin",
    };

    /// <summary>
    /// The Particle Universe systems of the original's <c>fx/arcanista_fx.pu</c> its effect table plays for each kind, with their seconds
    /// (<b>Verified</b>, the DLL's effect table; the original's start offsets within a cast are left out).
    /// </summary>
    static readonly Dictionary<string, (string System, float Seconds)[]> Systems = new(StringComparer.Ordinal)
    {
        ["air_sweep"] = [("ARC_air_sweep_ring", 0.8f), ("ARC_air_sweep_whirl", 0.7f), ("ARC_air_sweep_dust", 1.2f)],
        ["crack"] = [("ARC_crack_crack", 3), ("ARC_crack_burst", 0.6f), ("ARC_crack_rocks", 1.4f)],
        ["strike"] = [("ARC_strike_bolt", 0.5f), ("ARC_strike_flash", 0.4f)],
        ["faith_shield"] = [("ARC_faith_shield_ring", 1.2f), ("ARC_faith_shield_shell", 1)],
        ["lay_hands"] = [("ARC_lay_hands_glow", 1), ("ARC_lay_hands_motes", 1.6f)],
        ["mark"] = [("ARC_mark_flash", 0.5f), ("ARC_mark_bits", 1.2f), ("ARC_mark_ring", 1)],
        ["devotion_aura"] = [("ARC_devotion_aura_ring", 2)],
        ["death_grip"] = [("ARC_death_grip_flash", 0.5f), ("ARC_death_grip_bits", 1.2f)],
        ["fury"] = [("ARC_fury_arcs", 1.6f), ("ARC_fury_cuts", 1.6f), ("ARC_fury_blood", 2)],
        ["fire"] = [("ARC_fire_ring", 2.5f), ("ARC_fire_flames", 2.5f)],
        ["dash"] = [("ARC_dash_arc", 0.6f), ("ARC_dash_cut", 0.5f), ("ARC_dash_dust", 1.2f)],
        ["twin"] = [("ARC_twin_flash", 0.5f), ("ARC_twin_bits", 1.2f)],
        ["hunt"] = [("ARC_hunt_ring", 1.4f), ("ARC_hunt_motes", 1.6f)],
    };

    /// <summary>The light each kind gives its character while it plays (the port's colours: the original's own lights are in its shaders).</summary>
    static Vector3 Light(string kind) => kind switch
    {
        "force_lightning" or "emp" => new(0.5f, 0.7f, 2),
        "force_choke" or "life_drain" or "dark_rage" or "force_wave" => new(1.6f, 0.15f, 0.1f),
        "force_heal" or "battle_meditation" => new(0.4f, 1.5f, 0.5f),
        "thermal_detonator" or "flamethrower" => new(2, 0.8f, 0.2f),
        "deflect" or "force_push" or "force_leap" or "mind_trick" or "command" => new(0.5f, 0.9f, 1.8f),
        _ => new(1, 1, 1),
    };

    /// <summary>Queues the kind's particles and light on a character (or at a point) for the host's frame (<see cref="ForceFx"/>).</summary>
    void Fx(World world, string kind, CharacterId on, Vector3 at = default)
    {
        string key = Systems.ContainsKey(kind) ? kind : Alias.GetValueOrDefault(kind, kind);
        var systems = Systems.GetValueOrDefault(key, []);
        mod.Fx.Post(new FxEvent(systems, on, at, Light(kind)));
    }

    /// <summary>
    /// One of the kind's sounds (<c>sounds.cfg</c> <c>&lt;kind&gt;.&lt;stage&gt;</c>, else its alias's) from the original's <c>sounds\</c> folder, played
    /// as a loose WAV on the character or at the point. <c>sound_who=party</c>: only the player's characters' skills are heard.
    /// </summary>
    void Sound(World world, CharacterCold caster, string kind, string stage, CharacterId on, Vector3 at = default)
    {
        if (mod.SoundsFolder is not { } folder || mod.Settings.Volume <= 0 || mod.Settings.PartyOnly && !caster.IsPlayer) return;

        if (!_config.Sounds.TryGetValue($"{kind}.{stage}", out var files)
            && !(Alias.TryGetValue(kind, out var alias) && _config.Sounds.TryGetValue($"{alias}.{stage}", out files)))
            return;
        if (files.Length == 0) return;

        int pick = (int)(Rng.Hash(world.Seed, (ulong)kind.Length, purpose, (ulong)world.Tick) % (ulong)files.Length);
        string path = Path.Combine(folder, files[pick]);
        var where = on.IsNone ? at : world.Characters.TryResolveNext(on, out int s) ? world.Characters.Next[s].Position : at;
        world.PostSound(new PostedSound(path) { Volume = Math.Clamp(mod.Settings.Volume, 0, 4) }, on, where);
    }

    #endregion
}
