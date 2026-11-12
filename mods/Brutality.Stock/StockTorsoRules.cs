using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

internal static class GorePartIds
{
    public const string Stomach = "100-gamedata.quack";
}

/// <summary>Evaluates the original cleave rules after native combat and publishes a presentation request for each cut corpse.</summary>
internal static class StockTorsoRules
{
    internal readonly record struct CutRequest(CharacterId Victim, long Tick, ulong Seed, TorsoBisectionState Shape,
        Vector2 Travel, float Force, int Part, uint BloodColour, bool Robot, bool Restoring = false);

    sealed class Requests
    {
        public readonly ConcurrentDictionary<CharacterId, CutRequest> Cuts = new();
        public bool Previewed;
#if BRUTALITY_STOCK_SAVE_PROBE
        public long NextPreviewWave;
        public int PreviewedVictims;
#endif
        public long LastSpawn = -1;
    }

    const ulong ChanceDomain = 0x424953454354UL;
    // Keep the original mod's chance stream even though this build uses a separate manifest and save key.
    static readonly RngPurpose chancePurpose = (RngPurpose)(uint)(Rng.StableHash("dxseba.brutality") | 0x80000000UL);
    static readonly ConditionalWeakTable<World, Requests> worlds = new();
    static readonly string? preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        ? Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_TORSO_PREVIEW") : null;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    internal static bool Enabled { get; set; }

    internal static bool HasCuts(World world) => worlds.TryGetValue(world, out var requests) && !requests.Cuts.IsEmpty;

    internal static bool Restore(World world, CutRequest request) => worlds.GetOrCreateValue(world).Cuts.TryAdd(request.Victim, request);

    internal static bool TryGetSaved(World world, CharacterId owner, out CutRequest request)
    {
        request = default;
        return worlds.TryGetValue(world, out var cuts) && cuts.Cuts.TryGetValue(owner, out request);
    }

    internal static IEnumerable<CharacterId> CutOwners(World world) =>
        worlds.TryGetValue(world, out var cuts) ? cuts.Cuts.Keys.OrderBy(Rng.Key) : [];

    internal static void Observe(World world, in CombatHit hit, float level)
    {
        if (level < BrutalityPolicy.BisectionLevel || hit.Source != CombatHitSource.Melee || !hit.VictimWasAlive
            || hit.CutDirection is not (1 or 2 or 3 or 7 or 9) || hit.Damage.Cut < TorsoBisectionPolicy.MinimumCut
            || !world.Characters.TryResolveNext(hit.Attacker, out int attacker)
            || world.Characters.Cold(attacker)?.Fighter is not { IsAnimal: false, Unarmed: false, Weapon: { } weapon }
            || weapon.Data.SkillCategory == (int)WeaponCategory.Blunt
            || !world.Characters.TryResolveNext(hit.Victim, out int victim)
            || world.Characters.Cold(victim) is not { Race: { } race, Fighter.IsAnimal: false, Medical: { Dead: true } medical }
            || (uint)hit.Part >= (uint)medical.Parts.Count || !StockTrainingHook.TryGetTorsoMaximum(world, hit, out float maximum))
            return;

        // Eligible directions exclude thrusts. Positive delivered cut on an armed non-blunt blow also implies positive raw cut.
        var part = medical.Parts[hit.Part].Template;
        var input = new TorsoBisectionInput(hit.Source, CombatWoundMechanism.Slash, part, hit.CutDirection, hit.Damage.Cut, maximum,
            AliveBeforeHit: true, Robot: race.IsRobot) { Brutality = level, Lethal = true };
        ulong counter = CombatRolls.BlowCounter(hit.Attacker.Slot, hit.Sequence, hit.Blow, 0);
        float roll = Rng.Float(Rng.Hash(world.Seed, Rng.Key(hit.Victim) ^ ChanceDomain, chancePurpose, counter));
        if (!TorsoBisectionPolicy.ShouldBisect(input, roll)) return;

        ulong seed = TorsoBisectionPolicy.VisualSeedFor(world.Seed, hit.Victim, hit.Attacker, counter);
        var travel = new Vector2(hit.Travel.X, hit.Travel.Z);
        var shape = TorsoBisectionPolicy.StateForHit(part, hit.CutDirection, seed, travel, hit.VictimYaw, lowStrike: hit.LowStrike);
        if (!shape.IsValid) return;
        var request = new CutRequest(hit.Victim, hit.Tick, seed, shape, travel, hit.Damage.Cut, hit.Part, race.BloodColour, race.IsRobot);
        if (worlds.GetOrCreateValue(world).Cuts.TryAdd(hit.Victim, request) && trace)
            Console.WriteLine(FormattableString.Invariant($"Brutality stock torso request target={hit.Victim} part={part.Name} tick={hit.Tick} level={level:F1} cut={hit.Damage.Cut:F1}"));
    }

    internal static bool TryGet(World world, CharacterId victim, long shownTick, out CutRequest request)
    {
        request = default;
        if (!worlds.TryGetValue(world, out var requests) || !requests.Cuts.TryGetValue(victim, out var cut) || shownTick <= cut.Tick) return false;
        request = cut;
        return true;
    }

    internal static bool QueuePreview(World world, CharacterId id, int slot, CharacterCold actor, bool vertical)
    {
        if (!Enabled || actor.Medical is not { } medical || actor.Race is not { } race) return false;
        const string partId = ArmourData.ChestPartId;
        int part = medical.Parts.ToList().FindIndex(entry => entry.Template is { Vital: true, Type: BodyPartType.Torso } template
            && template.StringId == partId);
        if (part < 0) return false;
        ulong seed = Rng.Mix(Rng.Key(id) ^ world.Seed);
        var travel = Vector2.UnitX;
        var style = vertical ? TorsoBisectionStyle.Vertical : TorsoBisectionStyle.Transverse;
        var shape = TorsoBisectionPolicy.StateForHit(medical.Parts[part].Template, vertical ? 1 : 2, seed, travel,
            world.Characters.Next[slot].Yaw, style);
        long tick = world.Tick + 1;
        if (!shape.IsValid || !worlds.GetOrCreateValue(world).Cuts.TryAdd(id,
            new(id, tick, seed, shape, travel, 120, part, race.BloodColour, race.IsRobot))) return false;
        world.Commands.Enqueue(new DebugDamage([id], 5000, partId) { Tick = tick });
        return true;
    }

    internal static void Step(World world)
    {
        var requests = worlds.GetOrCreateValue(world);
        if (requests.LastSpawn != world.Characters.Spawns)
        {
            requests.LastSpawn = world.Characters.Spawns;
            for (int i = 0; i < world.Characters.HighWater; i++)
                if (world.Characters.Next[i].Alive && world.Characters.Cold(i) is { Medical.Dead: true, Save: not null } cold
                    && !requests.Cuts.ContainsKey(world.Characters.IdOf(i)))
                    StockTorsoSave.Restore(world, world.Characters.IdOf(i), cold);
        }
        foreach (var cut in requests.Cuts)
            if (!world.Characters.TryResolveNext(cut.Key, out _))
            {
                requests.Cuts.TryRemove(cut.Key, out _);
                StockTorsoSave.Remove(world, cut.Key);
            }
        if (world.Tick < 90 || requests is { Previewed: true }) return;

#if BRUTALITY_STOCK_SAVE_PROBE
        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_COMBINED") == "1" && world.Tick < 180) return;
        bool mixed = preview == "mixed";
        bool holdFoe = mixed && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_DELAY") == "1";
        bool sustained = mixed || preview == "sustained";
        if (sustained && world.Tick < requests.NextPreviewWave) return;
        int previewedCount = 0;
        bool nativeCrowd = preview == "native-crowd";
        bool crowd = nativeCrowd || preview == "crowd";
#endif
        var (style, direction, partId) = preview switch
        {
            "transverse" => (TorsoBisectionStyle.Transverse, 2, ArmourData.ChestPartId),
            "vertical" => (TorsoBisectionStyle.Vertical, 1, ArmourData.ChestPartId),
            "near-vertical" => (TorsoBisectionStyle.NearVertical, 1, ArmourData.ChestPartId),
            "diagonal" => (TorsoBisectionStyle.Diagonal, 7, ArmourData.ChestPartId),
            "oblique" => (TorsoBisectionStyle.Oblique, 3, ArmourData.ChestPartId),
#if BRUTALITY_STOCK_SAVE_PROBE
            "crowd" or "native-crowd" or "sustained" or "mixed" => (TorsoBisectionStyle.Oblique, 3, ArmourData.ChestPartId),
#endif
            "stomach" => (TorsoBisectionStyle.Transverse, 2, GorePartIds.Stomach),
            _ => (TorsoBisectionStyle.Auto, -1, ""),
        };
        if (direction < 0) return;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
#if BRUTALITY_STOCK_SAVE_PROBE
            if (holdFoe && slot == 1) continue;
#endif
            if (!table.Next[slot].Alive || table.Cold(slot) is not
                { IsPlayer: false, Race: { } race, Fighter.IsAnimal: false, Medical: { Dead: false } medical }) continue;
            int part = -1;
            for (int i = 0; i < medical.Parts.Count; i++)
                if (medical.Parts[i].Template is { Vital: true, Type: BodyPartType.Torso } template && template.StringId == partId)
                {
                    part = i;
                    break;
                }
            if (part < 0) continue;

            var id = table.IdOf(slot);
            ulong seed = Rng.Mix(Rng.Key(id) ^ world.Seed);
            var travel = Vector2.UnitX;
            var shape = TorsoBisectionPolicy.StateForHit(medical.Parts[part].Template, direction, seed, travel, table.Next[slot].Yaw, style);
            if (!shape.IsValid) continue;
            requests ??= worlds.GetOrCreateValue(world);
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained)
            {
                requests.PreviewedVictims++;
                requests.Previewed = requests.PreviewedVictims >= (mixed ? 12 : 32);
                requests.NextPreviewWave = world.Tick + 90;
            }
            else
#endif
                requests.Previewed = true;
            long tick = world.Tick + 1;
#if BRUTALITY_STOCK_SAVE_PROBE
            if (!nativeCrowd)
#endif
                requests.Cuts.TryAdd(id, new(id, tick, seed, shape, travel, 120, part, race.BloodColour, race.IsRobot));
            world.Commands.Enqueue(new DebugDamage([id], 5000, partId) { Tick = tick });
            Console.WriteLine($"Brutality stock torso preview style={style} part={medical.Parts[part].Template.Name} target={id} tick={tick}");
#if BRUTALITY_STOCK_SAVE_PROBE
            if (crowd && ++previewedCount < 20) continue;
            if (sustained && !requests.Previewed && ++previewedCount < 8) continue;
#endif
            return;
        }
    }
}
