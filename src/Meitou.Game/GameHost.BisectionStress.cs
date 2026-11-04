using System.Diagnostics;
using System.Globalization;
using System.Numerics;

using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class GameHost
{
    double bisectionStressDrawTotal, bisectionStressFillTotal;
    long bisectionStressAllocatedTotal;
    int bisectionStressSamples;

    Vector3 StageBisectionStress()
    {
        if (!IsSandbox || !g.SandboxBisect || g.Sandbox < 2)
            throw new ArgumentException("--scene bisect-stress needs --sandbox <n>=2 --sandbox-bisect");
        var characters = session.CurrentSnapshot.Characters;
        if (g.SandboxCorpsePile)
        {
            int index = 0;
            foreach (var character in characters)
            {
                if (character.Id == sandboxHero) continue;
                var at = new Vector3((index % 6 - 2.5f) * 2, character.Position.Y, (index / 6 - 2.5f) * 2);
                session.World.Commands.Enqueue(new DebugTeleport([character.Id], at) { Tick = session.World.Tick });
                index++;
            }
            AdvanceScene(1);
            characters = session.CurrentSnapshot.Characters;
        }
        session.World.Profile.Enabled = true;
        session.World.Profile.Clear();
        Console.WriteLine($"bisect stress: {characters.Count} initial characters, all except the hero cut at tick 120; render timing excludes simulation and PNG output");
        bisectionStressDrawTotal = bisectionStressFillTotal = 0;
        bisectionStressAllocatedTotal = bisectionStressSamples = 0;
        return characters.Aggregate(Vector3.Zero, (sum, character) => sum + character.Position) / Math.Max(1, characters.Count)
            + Vector3.UnitY * 6;
    }

    void QueueBisectionStress()
    {
        var world = session.World;
        if (world.Tick != 120) return;
        int queued = 0;
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            var id = world.Characters.IdOf(slot);
            if (id == sandboxHero || !world.Characters.IsAlive(id)
                || world.Characters.Cold(slot)?.Medical is not { Dead: false }) continue;
            world.Commands.Enqueue(new DebugBisect(id)
            {
                Tick = world.Tick,
                Attacker = sandboxHero,
                VisualSeed = g.SandboxBisectVariant,
                CutDirection = g.SandboxCutDirection ?? 2,
                Style = g.SandboxBisectStyle,
                TargetBodyPart = string.Equals(g.SandboxPart, "stomach", StringComparison.OrdinalIgnoreCase)
                    ? BodyPartTemplate.StomachPartId : ArmourData.ChestPartId,
            });
            queued++;
        }
        Console.WriteLine($"bisect stress: queued {queued} simultaneous cuts at tick {world.Tick}");
    }

    void DrawBisectionStressFrame(int width, int height, int frame)
    {
        double beforeFill = fillTotal;
        long beforeAllocated = GC.GetTotalAllocatedBytes(precise: false);
        long started = Stopwatch.GetTimestamp();
        DrawWorld(width, height);
        double draw = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - beforeAllocated;
        double fill = fillTotal - beforeFill;
        if (frame >= 2)
        {
            bisectionStressDrawTotal += draw;
            bisectionStressFillTotal += fill;
            bisectionStressAllocatedTotal += allocated;
            bisectionStressSamples++;
        }
        ReportBisectionStress(frame, draw, fill, allocated);
    }

    void ReportBisectionStress(int frame, double draw, double fill, long allocated)
    {
        var previous = session.PreviousSnapshot.Characters.ToDictionary(character => character.Id);
        int bisected = 0, robots = 0, settled = 0;
        foreach (var character in session.CurrentSnapshot.Characters)
        {
            if (character.TorsoBisection is null) continue;
            bisected++;
            if (session.World.Characters.Cold(character.Id.Slot)?.Race?.IsRobot == true) robots++;
            if (character.TorsoRagdoll is { } now && previous.TryGetValue(character.Id, out var before)
                && ReferenceEquals(now.Positive, before.TorsoRagdoll?.Positive)
                && ReferenceEquals(now.Negative, before.TorsoRagdoll?.Negative)) settled++;
        }
        string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        var renderer = gpu.Characters;
        string average = bisectionStressSamples == 0 ? "warming" :
            $"warm mean draw {F(bisectionStressDrawTotal / bisectionStressSamples)} ms, fill {F(bisectionStressFillTotal / bisectionStressSamples)} ms, "
            + $"allocated {bisectionStressAllocatedTotal / bisectionStressSamples} bytes ({bisectionStressSamples} samples)";
        var profile = session.World.Profile;
        double sim = profile.Ticks > 0 ? profile.PhaseMs.Sum() / profile.Ticks : 0;
        double rag = profile.Ticks > 0 ? profile.Parts.FirstOrDefault(p => p.Name == "rag step").Ms / profile.Ticks : 0;
        Console.WriteLine($"bisect stress simulation {frame}: tick {F(sim)} ms, rag step {F(rag)} ms, {profile.Ticks} ticks");
        profile.Clear();
        Console.WriteLine($"bisect stress frame {frame}: bisected {bisected}, robots {robots}, settled {settled}; "
            + $"draw {F(draw)} ms, fill {F(fill)} ms, character update {F(renderer?.LastUpdateMs ?? 0)} ms, "
            + $"pose {F(renderer?.LastPoseMs ?? 0)} ms, allocated {allocated} bytes; "
            + $"discharges {robotDischarges.Count}, anchors {gpu.Particles?.RobotDischarges.Count ?? 0}, "
            + $"electrical quads {gpu.Particles?.DrawnRobotDischargeQuads ?? 0}, smoke puffs {gpu.Particles?.DrawnRobotSmokePuffs ?? 0}, "
            + $"explosion bursts {robotExplosionBursts}, active explosions {robotExplosions.Count}, explosion quads {gpu.Particles?.DrawnRobotExplosionQuads ?? 0}, "
            + $"scrap bursts {robotScrapBursts}, scrap pieces {robotScrapPieces}, liquid drops {liquidBlood.Drops.Count}, liquid streams {liquidBlood.Streams.Count}, "
            + $"liquid drawn {gpu.Particles?.DrawnLiquidBlood ?? 0}, liquid sources {liquidBlood.SourceCount}; {average}");
    }
}