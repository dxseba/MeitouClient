using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Physics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

internal static class GoreRandom
{
    public static readonly RngPurpose TorsoVisualPurpose =
        (RngPurpose)(uint)(Rng.StableHash("meitou.brutality.torso-cut") | 0x80000000UL);
    public static readonly RngPurpose HeadCutVisualPurpose =
        (RngPurpose)(uint)(Rng.StableHash("meitou.brutality.head-cut") | 0x80000000UL);
}

/// <summary>Publishes short-lived presentation requests after the native combat has accepted a decapitation.</summary>
internal static class StockHeadRules
{
    internal readonly record struct CutRequest(long Tick, ulong Seed, HeadCutState Shape);
    const int RequestLifetime = 240;

    sealed class Requests
    {
        public readonly ConcurrentDictionary<CharacterId, CutRequest> Hits = new();
        public bool Previewed;
    }

    static readonly ConditionalWeakTable<World, Requests> worlds = new();
    static readonly string? preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        ? Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_HEAD_PREVIEW")?.ToLowerInvariant() : null;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    internal static bool Enabled { get; set; }

    internal static void Observe(World world, in CombatHit hit)
    {
        if (!hit.VictimWasAlive || StockHeadGate.Result(world, hit) is not { } request) return;
        StockHeadSplitSave.Remember(world, hit.Victim, request.Shape, request.Seed);
        LimitLaunch(world, hit.Victim);
        if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock head cut target={hit.Victim} tick={hit.Tick} direction={hit.CutDirection} mode={request.Shape.Mode} plane={request.Shape.Plane}"));
        if (request.Shape.Mode is HeadCutMode.Burst or HeadCutMode.DetachedSplit)
            worlds.GetOrCreateValue(world).Hits[hit.Victim] = request;
        else StockWoundRules.RecordHeadCut(world, hit.Victim, request.Shape);
    }

    static void LimitLaunch(World world, CharacterId victim)
    {
        if (!world.Characters.TryResolveNext(victim, out int slot)
            || world.Characters.Cold(slot) is not { Blow: { } raw } cold) return;

        // The corpse gets its own motion and a small shove. The loose head uses a separate visual velocity.
        float response = RobotRagdollWeight.Response(cold.Race?.IsRobot == true);
        var motion = StockTorsoLaunch.Clamp(raw.Velocity - raw.Push, 8);
        var push = StockTorsoLaunch.Clamp(new(raw.Push.X, 0, raw.Push.Z), 8 * response);
        var speed = motion + push;
        cold.Blow = new(speed, push, null);
        cold.Knockdown = StockTorsoLaunch.Clamp(push, 3 * response);
        if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock head launch target={victim} nativePush={raw.Push.Length():F1}->{push.Length():F1} bodySpeed={speed.Length():F1} headSpeed={cold.Knockdown.Length():F1}"));
    }

    internal static bool TryGet(World world, CharacterId id, long shownTick, out CutRequest request)
    {
        request = default;
        if (!worlds.TryGetValue(world, out var requests) || !requests.Hits.TryGetValue(id, out var hit)
            || shownTick < hit.Tick || shownTick - hit.Tick > RequestLifetime) return false;
        request = hit;
        return true;
    }

    internal static void Complete(World world, CharacterId id, CutRequest request)
    {
        if (worlds.TryGetValue(world, out var requests)) requests.Hits.TryRemove(new(id, request));
    }

    internal static bool QueuePreview(World world, CharacterId id, int slot, CharacterCold actor, string style)
    {
        if (!Enabled || actor.Race?.IsRobot == true && style == "burst"
            || actor.Medical?.Parts.FirstOrDefault(part => part.Template.Type == BodyPartType.Head && part.Template.Vital) is not { } head)
            return false;
        var yaw = world.Characters.Next[slot].Yaw;
        var travel = -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        ulong seed = Rng.Mix(Rng.Key(id) ^ world.Seed);
        if (HeadCutPolicy.Preview(style, visualSeed: seed, travel: travel, victimYaw: yaw) is not { } shape) return false;
        long tick = world.Tick + 1;
        StockHeadSplitSave.Remember(world, id, shape, seed);
        if (shape.Mode is HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit)
            StockWoundRules.RecordHeadCut(world, id, shape);
        else worlds.GetOrCreateValue(world).Hits[id] = new(tick, seed, shape);
        world.Commands.Enqueue(new DebugDamage([id], 5000, head.Template.StringId) { Tick = tick });
        return true;
    }

    internal static void Step(World world)
    {
        StockHeadSplitSave.Step(world);
        if (worlds.TryGetValue(world, out var requests))
            foreach (var pair in requests.Hits)
                if (world.Tick - pair.Value.Tick > RequestLifetime) requests.Hits.TryRemove(pair.Key, out _);
        if (preview is null || world.Tick < 90 || requests is { Previewed: true }) return;

        // Opt-in sandbox probe. The native debug command exercises the same decapitation and snapshot refresh.
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot] is not { Alive: true }
                || table.Cold(slot) is not { IsPlayer: false, Race: { } race, Fighter.IsAnimal: false, Medical: { Dead: false } medical }
                || preview == "burst" && race.IsRobot
                || medical.Parts.FirstOrDefault(part => part.Template.Type == BodyPartType.Head) is not { } head) continue;
            var id = table.IdOf(slot);
            requests ??= worlds.GetOrCreateValue(world);
            requests.Previewed = true;
            ulong seed = Rng.Mix(Rng.Key(id) ^ world.Seed);
            if (HeadCutPolicy.Preview(preview, visualSeed: seed) is not { } shape) return;
            StockHeadSplitSave.Remember(world, id, shape, seed);
            if (shape.Mode is HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit) StockWoundRules.RecordHeadCut(world, id, shape);
            else requests.Hits[id] = new(world.Tick, seed, shape);
            world.Commands.Enqueue(new DebugDamage([id], 5000, head.Template.StringId) { Tick = world.Tick + 1 });
            Console.WriteLine(FormattableString.Invariant($"Brutality stock head preview mode={shape.Mode} target={id} tick={world.Tick} plane={shape.Plane}"));
            break;
        }
    }
}
