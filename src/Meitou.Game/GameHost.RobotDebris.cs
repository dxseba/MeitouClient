using System.Numerics;
using Meitou.Content;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class GameHost
{
    // Installed ITEM records: small armour plates, motors and hinges, scaled to broken metal fragments.
    static readonly string[] RobotScrapItems =
        ["42159-gamedata.base", "43397-changes_otto.mod", "43396-changes_otto.mod"];
    const int RobotScrapHistory = 64;
    sealed record RobotScrapMesh(string Path, Vector3 Extent);
    readonly record struct RobotScrapCut(CharacterId Victim, int Part, int Kind);
    readonly HashSet<RobotScrapCut> robotScrapSeen = [];
    readonly Queue<RobotScrapCut> robotScrapOrder = new(RobotScrapHistory);
    RobotScrapMesh[]? robotScrapMeshes;
    long robotScrapEpoch = -1;
    int robotScrapBursts, robotScrapPieces;

    /// <summary>A severe mechanical break throws a few small installed metal parts through the existing bounded debris pool.</summary>
    void EmitRobotCutDebris(in CombatEvent hit, CharacterCold? victim)
    {
        bool particlesEnabled = !o.NoParticles || IsFlat && g.FlatCombatParticles;
        if (!particlesEnabled || victim is not { Race.IsRobot: true, Fighter.IsAnimal: false, Medical: { } medical }
            || (uint)hit.Part >= (uint)medical.Parts.Count || gpu?.Debris is not { } debris) return;
        var part = medical.Parts[hit.Part].Template;
        int kind = RobotScrapKind(hit, victim, part);
        if (kind < 0) return;
        // Luckyren already throws a dying race's configured mechanical part meshes.
        if (config.LuckyrenMode && medical.Dead && LimbMeshes(victim.Race!.StringId).Length > 0) return;
        if (robotScrapEpoch != worldEpoch) ClearRobotCutDebris();
        var marker = new RobotScrapCut(hit.Victim, hit.Part, kind);
        if (robotScrapSeen.Contains(marker)) return;
        var choices = robotScrapMeshes ??= LoadRobotScrapMeshes(debris);
        if (choices.Length == 0) return;
        var at = gpu.Characters?.BonePosition(DrawKey(hit.Victim), RobotCutEffects.ImpactBone(part, hit.LowStrike))
            ?? hit.Position + new Vector3(0, RobotCutEffects.FallbackHeight(part), 0);
        if (!float.IsFinite(at.LengthSquared())) return;

        float length = hit.Direction.LengthSquared();
        var travel = float.IsFinite(length) && length > 1e-6f ? hit.Direction / MathF.Sqrt(length) : Vector2.UnitX;
        float floor = session.World.Walkability.GroundHeight(at.X, at.Z, hit.Position.Y);
        if (!float.IsFinite(floor)) floor = gpu.Terrain.HeightAt(at.X, at.Z);
        ulong random = RobotScrapSeed(hit, kind, session.World.Seed);
        int count = 3 + (int)(RobotScrapMix(random) % 5);
        for (int i = 0; i < count; i++) AddRobotScrap(choices, at, travel, floor, ref random);
        if (pieces.Count > MaxPieces) pieces.RemoveRange(0, pieces.Count - MaxPieces);
        if (robotScrapOrder.Count >= RobotScrapHistory) robotScrapSeen.Remove(robotScrapOrder.Dequeue());
        robotScrapSeen.Add(marker);
        robotScrapOrder.Enqueue(marker);
        robotScrapBursts++;
        robotScrapPieces += count;
    }

    static int RobotScrapKind(in CombatEvent hit, CharacterCold victim, BodyPartTemplate part)
    {
        if (hit.Kind == CombatEventKind.BodyBisected) return 2;
        // Head Hit and a possible later PartSevered notification share one marker.
        if (part.Type == BodyPartType.Head)
            return hit.Kind is CombatEventKind.Hit or CombatEventKind.PartSevered
                && victim.Medical is { } medical
                && (medical.Decapitated || victim.HeadCut is { IsValid: true }) ? 1 : -1;
        return hit.Kind == CombatEventKind.PartSevered && part.LimbSlot >= 0 ? 0 : -1;
    }

    RobotScrapMesh[] LoadRobotScrapMeshes(DebrisRenderer debris)
    {
        if (scene.Database is not { } db) return [];
        var result = new List<RobotScrapMesh>(RobotScrapItems.Length);
        foreach (string id in RobotScrapItems)
        {
            if (db.Find(id) is not { } item) continue;
            string mesh = item.GetPath("ground mesh") is { Length: > 0 } ground ? ground : item.GetPath("mesh");
            if (mesh.Length == 0 || assets.Find(mesh) is not { } found) continue;
            string path = GameFiles.Resolve(found);
            if (debris.Extent(path) is not { } extent || !float.IsFinite(extent.LengthSquared())
                || MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) <= 1e-5f) continue;
            result.Add(new(path, extent));
        }
        return [.. result];
    }

    void AddRobotScrap(RobotScrapMesh[] choices, Vector3 origin, Vector2 travel, float floor, ref ulong random)
    {
        var mesh = choices[Math.Min((int)(NextRobotScrap(ref random) * choices.Length), choices.Length - 1)];
        float radius = 0.18f + NextRobotScrap(ref random) * 0.28f;
        float scale = radius / MathF.Max(mesh.Extent.X, MathF.Max(mesh.Extent.Y, mesh.Extent.Z));
        float angle = NextRobotScrap(ref random) * MathF.Tau;
        var aside = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var flat = aside * (4 + NextRobotScrap(ref random) * 9) + travel * (4 + NextRobotScrap(ref random) * 5);
        var axis = new Vector3(NextRobotScrap(ref random) - 0.5f, NextRobotScrap(ref random) - 0.5f, NextRobotScrap(ref random) - 0.5f);
        axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitY;
        pieces.Add(new Piece
        {
            Mesh = mesh.Path, LandingSound = "Robotic_Component", Scale = scale, Extent = mesh.Extent * scale, Floor = floor, Born = session.World.Tick,
            Position = origin + new Vector3(aside.X * 0.25f, NextRobotScrap(ref random) * 0.3f, aside.Y * 0.25f),
            Velocity = new Vector3(flat.X, 8 + NextRobotScrap(ref random) * 11, flat.Y),
            Rotation = Quaternion.CreateFromYawPitchRoll(NextRobotScrap(ref random) * MathF.Tau,
                NextRobotScrap(ref random) * MathF.Tau, NextRobotScrap(ref random) * MathF.Tau),
            Spin = axis * (3 + NextRobotScrap(ref random) * 6),
        });
    }

    void ClearRobotCutDebris()
    {
        robotScrapSeen.Clear();
        robotScrapOrder.Clear();
        robotScrapMeshes = null;
        robotScrapEpoch = worldEpoch;
        robotScrapBursts = robotScrapPieces = 0;
    }

    static ulong RobotScrapSeed(in CombatEvent hit, int kind, ulong worldSeed) => unchecked(RobotScrapMix(worldSeed
        ^ ((ulong)(uint)hit.Victim.Slot << 32 | (uint)hit.Victim.Generation) ^ RobotScrapMix((ulong)hit.Tick)
        ^ RobotScrapMix((uint)hit.Part + 0x53435241504D4554UL) ^ (uint)kind));

    static float NextRobotScrap(ref ulong state)
    {
        state = RobotScrapMix(unchecked(state + 0x9E3779B97F4A7C15UL));
        return (state >> 40) / 16777216f;
    }

    static ulong RobotScrapMix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }
    }
}
