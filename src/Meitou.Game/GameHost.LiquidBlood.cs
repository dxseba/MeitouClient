using System.Numerics;

using Meitou.Data.World;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class GameHost
{
    readonly LiquidBloodSimulation liquidBlood = new(new Random());
    bool LiquidBloodEnabled => GoreEnabled && o.MeitouBlood && (!o.NoParticles || IsFlat && g.FlatCombatParticles);

    void UpdatePosedCutEffects()
    {
        if (!GoreEnabled) return;
        var world = session.World;
        double clock = (session.ShownTick + session.SimulationAlpha) * world.TickSeconds;
        UpdateRobotDischarges(world);
        UpdateRobotBreakExplosions(world);
        UpdateRobotCables(world, (float)clock);
        UpdateLiquidBlood(world, clock);
        UpdatePosedGuts();
        DrawGuts();
    }

    void BeginLiquidBlood(in CombatEvent hit, World world)
    {
        if (!LiquidBloodEnabled || world.Characters.Cold(hit.Victim.Slot)?.Race is not { } race
            || !race.IsRobot && race.BleedRate <= 0) return;
        liquidBlood.Begin(hit.Victim, hit.Tick * world.TickSeconds, hit.Direction, race.BloodColour);
    }

    void UpdateLiquidBlood(World world, double clock)
    {
        if (gpu.Particles is not { } particles) return;
        particles.LiquidBlood.Clear();
        particles.LiquidStreams.Clear();
        if (!LiquidBloodEnabled) { liquidBlood.Clear(); return; }
        liquidBlood.Advance(clock, (owner, half) => world.Characters.IsAlive(owner)
            ? gpu.Characters?.CutContact(TorsoDrawKey(owner, half)) : null, LiquidGround);
        particles.LiquidStreams.AddRange(liquidBlood.Streams);
        foreach (var drop in liquidBlood.Drops)
        {
            var tail = drop.At - drop.Velocity * MathF.Min(0.045f, 1.5f / MathF.Max(drop.Velocity.Length(), 0.001f));
            particles.LiquidBlood.Add(new LiquidBloodDrop(drop.At, tail, drop.Width,
                new Vector4(drop.Colour, 0.9f * Math.Clamp((2.5f - drop.Age) * 3, 0, 1))));
        }
        foreach (var impact in liquidBlood.Impacts) LiquidBloodImpact(impact);

        float LiquidGround(Vector3 at)
        {
            float floor = world.Walkability.GroundHeight(at.X, at.Z, at.Y);
            return float.IsFinite(floor) ? floor : gpu.Terrain.HeightAt(at.X, at.Z);
        }
    }

    void LiquidBloodImpact(in FlyingBlood drop)
    {
        uint rgb = (uint)(drop.Colour.X * 255) << 16 | (uint)(drop.Colour.Y * 255) << 8 | (uint)(drop.Colour.Z * 255);
        uint opaque = 0xDF000000 | Abgr(rgb);
        var along = new Vector2(drop.Velocity.X, drop.Velocity.Z);
        if (drop.At.Y <= gpu.Terrain.HeightAt(drop.At.X, drop.At.Z) + BloodAboveGround)
            decalStore?.Add(TerrainDecalType.Splash, drop.At, along, Math.Clamp(drop.Width * 0.38f, 0.025f, 0.09f),
                opaque, TerrainDecalStore.NewLife, bloodRandom);
        if (gpu.Decals is { Surfaces: true } surfaces)
            surfaces.AddSurface(TerrainDecalType.Splash, drop.At, along, 0.04f, opaque, bloodRandom);
    }

    void ClearLiquidBlood()
    {
        liquidBlood.Clear();
        gpu?.Particles?.LiquidBlood.Clear();
        gpu?.Particles?.LiquidStreams.Clear();
    }
}