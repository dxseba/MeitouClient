using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Game;

sealed partial class GameHost
{
    internal sealed class GutFall
    {
        public Vector3 Position, Velocity;
        public Vector3 HalfExtent = new(0.35f);
        public Quaternion Rotation = Quaternion.Identity;
        public float Floor, Age;
        public uint BloodColour = DefaultBloodColour;
        public long RigKey;
        public bool Physical, Resting;
        public bool Section, Positioned, AttachmentBroken, KeepAttached;
        public float InsideDepth;
        public Vector3 AttachmentLocal;
    }

    readonly Dictionary<CharacterId, GutFall> gutFalls = [];
    readonly HashSet<CharacterId> gutsSpilled = [];
    BowelRagdollScene? gutPhysics;
    IRagdollEnvironment? gutEnvironment;
    Meitou.Navigation.RagdollGeometry? ownedGutGeometry;
    long nextGutKey;

    /// <summary>Loose tissue uses its own display-thread Bepu scene; world simulation is never mutated from rendering.</summary>
    void UpdateGuts(World world, float dt)
    {
        if (gpu?.Debris is not { } debris) return;
        debris.AnatomyFile ??= Meitou.Content.GameInstall.Locate() is { } game
            ? Path.Combine(game.ModsDirectory, "Gauntlet of Crowns", "Ecorche.anatomy") : null;
        if (debris.Extent(DebrisRenderer.Bowel) is null) return;
        SpawnWoundGuts(world);
        foreach (var (who, gone) in gutFalls.ToArray())
            if (gone.Age > GutLifeSeconds || !world.Characters.IsAlive(who)) RemoveGutFall(who);
        dt = float.IsFinite(dt) ? Math.Clamp(dt, 0, 0.5f) : 0;
        UpdateGutPhysics(world, config.AdvancedRagdolls ? debris.BowelRig() : null, dt);
    }

    void DrawGuts()
    {
        foreach (var fall in gutFalls.Values)
        {
            if (fall.Section && !fall.Positioned) continue;
            var pose = fall.Physical ? gutPhysics?.PoseOf(fall.RigKey) : null;
            gpu.DebrisPieces.Add(new DebrisPiece(DebrisRenderer.Bowel, 1, pose is null ? fall.Rotation : Quaternion.Identity,
                pose?.Centre ?? fall.Position, BloodColour: fall.BloodColour, Bowel: pose));
        }
        gpu.Debris?.PrepareBowels(CollectionsMarshal.AsSpan(gpu.DebrisPieces));
    }

    void UpdateGutPhysics(World world, BowelRigGeometry? geometry, float dt)
    {
        foreach (var fall in gutFalls.Values)
        {
            if (fall.Section && !fall.Positioned) { fall.Age += dt; continue; }
            if (fall.Section && !GutEmergence.AttachedAt(fall.KeepAttached, fall.Age))
            {
                gutPhysics?.ReleaseAttachment(fall.RigKey);
                fall.AttachmentBroken = true;
            }
            if (geometry is not null && !fall.Physical)
            {
                gutPhysics ??= new BowelRagdollScene();
                bool resting = fall.Resting || !fall.Section && fall.Age >= 1;
                var rotation = resting ? RestingGutRotation(geometry) : fall.Rotation;
                var position = resting ? fall.Position with { Y = fall.Floor + geometry.Radius } : fall.Position;
                fall.Physical = gutPhysics.Add(fall.RigKey, geometry, position, rotation,
                    resting ? Vector3.Zero : fall.Velocity, resting,
                    pinNode: fall.Section && !fall.AttachmentBroken && GutEmergence.AttachedAt(fall.KeepAttached, fall.Age) ? 0 : -1, preserveAttachment: fall.KeepAttached);
            }
            else if (geometry is null && fall.Physical)
            {
                if (gutPhysics?.PoseOf(fall.RigKey) is { } last) fall.Position = last.Centre;
                gutPhysics?.Remove(fall.RigKey);
                fall.Physical = false;
                if (!fall.Section || fall.AttachmentBroken) fall.Rotation = Quaternion.Identity;
                fall.Velocity = Vector3.Zero;
            }
            if (fall.Physical || fall.Section && !fall.AttachmentBroken && GutEmergence.AttachedAt(fall.KeepAttached, fall.Age)) fall.Age += dt;
            else StepGut(fall, dt);
        }
        if (geometry is null && gutPhysics is not null)
        {
            gutPhysics.Dispose();
            gutPhysics = null;
            ownedGutGeometry?.Dispose();
            ownedGutGeometry = null;
            gutEnvironment = null;
        }
        if (gutPhysics is { } physics)
        {
            physics.Step(dt / MathF.Max(1, world.PhysicsSpeed), GutEnvironment(world), world.Tick);
        }
    }

    static Quaternion RestingGutRotation(BowelRigGeometry geometry)
    {
        var normal = Vector3.Normalize(Vector3.Cross(geometry.RestNodes[1] - geometry.RestNodes[0],
            geometry.RestNodes[3] - geometry.RestNodes[0]));
        float dot = Vector3.Dot(normal, Vector3.UnitY);
        if (dot > 0.9999f) return Quaternion.Identity;
        if (dot < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(normal, Vector3.UnitY), 1 + dot));
    }

    void SpawnWoundGuts(World world)
    {
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            var hot = world.Characters.Previous[slot];
            var id = world.Characters.IdOf(slot);
            if (!hot.Alive || wounds.Of(id) is not { DeepAbdomen: true } || gutFalls.ContainsKey(id) || gutsSpilled.Contains(id)
                || world.Characters.Cold(slot)?.Race?.Name is not ("Greenlander" or "Scorchlander")) continue;
            var forward = new Vector3(MathF.Sin(hot.Yaw), 0, MathF.Cos(hot.Yaw));
            KeepGutFall(id, new GutFall
            {
                Position = hot.Position + new Vector3(0, 11.5f, 0) + forward * 1.2f,
                Velocity = forward * 1.5f, Floor = hot.Position.Y,
                BloodColour = world.Characters.Cold(slot)?.Race?.BloodColour ?? DefaultBloodColour,
            });
        }
    }

    void KeepGutFall(CharacterId id, GutFall fall)
    {
        if (gutFalls.Count >= BowelRagdollScene.Capacity && !gutFalls.ContainsKey(id))
            RemoveGutFall(gutFalls.MaxBy(pair => pair.Value.Age).Key);
        fall.RigKey = ++nextGutKey;
        fall.HalfExtent = gpu?.Debris?.Extent(DebrisRenderer.Bowel) ?? fall.HalfExtent;
        // Older analytic spills may have been advanced before the renderer knew their exact bounds.
        if (fall.Resting) fall.Position.Y = fall.Floor + GutSupportHeight(fall);
        gutFalls[id] = fall;
    }

    void RemoveGutFall(CharacterId id)
    {
        if (!gutFalls.Remove(id, out var fall)) return;
        gutPhysics?.Remove(fall.RigKey);
        gutsSpilled.Add(id);
    }

    IRagdollEnvironment GutEnvironment(World world)
    {
        if (gutEnvironment is not null) return gutEnvironment;
        // RagdollGeometry keeps a thread-owned height cache: never share the simulation's instance with display physics.
        if (saveLevels is not null && scene.Database is { } db)
            return gutEnvironment = ownedGutGeometry = new Meitou.Navigation.RagdollGeometry(install, db, saveLevels,
                (x, z) => world.Walkability.GroundHeight(x, z, 0));
        return gutEnvironment = new WalkabilityEnvironment(world.Walkability);
    }

    void DisposeGuts()
    {
        gutPhysics?.Dispose();
        ownedGutGeometry?.Dispose();
        ownedGutGeometry = null;
        gutPhysics = null;
        gutEnvironment = null;
        gutFalls.Clear();
    }

    static float GutSupportHeight(GutFall fall)
    {
        var rotation = Matrix4x4.CreateFromQuaternion(fall.Rotation);
        return MathF.Abs(rotation.M12) * fall.HalfExtent.X
            + MathF.Abs(rotation.M22) * fall.HalfExtent.Y
            + MathF.Abs(rotation.M32) * fall.HalfExtent.Z;
    }

    internal static void StepGut(GutFall fall, float dt)
    {
        if (!float.IsFinite(dt) || dt < 0) return;
        fall.Age += dt;
        if (fall.Resting) return;
        if (LandGut(fall) || dt == 0) return;
        fall.Velocity.Y -= (fall.Age < 0.8f ? 8 : 40) * dt;
        fall.Position += fall.Velocity * dt;
        fall.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3 * dt) * fall.Rotation);
        LandGut(fall);
    }

    static bool LandGut(GutFall fall)
    {
        float level = fall.Floor + GutSupportHeight(fall);
        if (fall.Position.Y > level) return false;
        fall.Position.Y = level;
        fall.Velocity = Vector3.Zero;
        fall.Resting = true;
        return true;
    }
}
