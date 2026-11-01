using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Physics;

public class RagdollInstalledRaceTests
{
    [Fact]
    [Slow]
    public void Installed_worker_vertical_fragments_have_finite_poses_bounds_and_positive_inertia_before_the_physics_step()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var db = InstallData.FullLoadOrder!;
        const string worker = "42194-rebirth.mod";
        Assert.SkipWhen(db.Find(worker) is null, "Installed Worker Drone character not found");
        var look = CharacterAppearance.Build(db, install.Root, worker, new() { Female = false, Naked = true });
        var assets = new AssetLocator(install);
        var rigs = new CharacterRigs(db, path => assets.Configured.Find(Path.GetFileName(path.Replace('\\', '/'))) ?? assets.Find(path), path => assets.Find(path));
        var setup = rigs.For(look);
        Assert.SkipWhen(setup is null, "Installed Worker Drone skeleton not found");
        string file = look.Race.GetPath("male ragdoll");
        var ragdollPath = assets.Find(file);
        Assert.SkipWhen(ragdollPath is null, "Installed Worker Drone ragdoll not found");
        Vector3[]? Hull(string name) => assets.Find(name) is { } path ? CookedMeshReader.ReadConvex(File.ReadAllBytes(path)).Vertices : null;
        var definition = Assert.IsType<RagdollDefinition>(RagdollDefinition.Build(PhysicsFile.ReadFile(ragdollPath), setup.Rig, 1, Hull));
        var scratch = setup.Rig.Rent();
        try
        {
            scratch.PoseDerived(setup.Shape, setup.Fixed.Select(layer => new ActiveLayer(layer.Def, layer.Time, 1)).ToArray());
            foreach (int degrees in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
            {
                float radians = degrees * (MathF.PI / 180);
                var travel = new Vector2(MathF.Cos(radians), -MathF.Sin(radians));
                var cut = TorsoBisectionPolicy.StateForDirection(1, 1, travel, -MathF.PI / 2, TorsoBisectionStyle.Vertical);
                foreach (int side in new[] { -1, 1 })
                {
                var fragment = Assert.IsType<RagdollDefinition>(RagdollCut.Build(definition, cut.Plane, side, true));
                Assert.NotEmpty(fragment.Bodies);
                Assert.InRange(fragment.Root, 0, fragment.Bodies.Length - 1);
                foreach (var body in fragment.Bodies)
                {
                    string context = $"Worker approach{degrees} side{side} {body.Name}";
                    AssertFinite(body.Bind, context + " bind");
                    AssertFinite(body.BoneInBody, context + " bone frame");
                    Assert.True(float.IsFinite(body.Mass) && body.Mass > 0, context + " mass");
                    var bone = new RigidTransform(scratch.DerivedPosition[body.Bone], scratch.DerivedRotation[body.Bone]);
                    var pose = body.BoneInBody.Inverse().Then(bone);
                    AssertFinite(pose, context + " initial pose");
                    Assert.InRange(Vector3.Distance(body.BoneInBody.Then(pose).Position, bone.Position), 0, 0.0001f);
                    AssertInertia(body, context);
                }
                foreach (var joint in fragment.Joints)
                {
                    Assert.InRange(joint.A, 0, fragment.Bodies.Length - 1);
                    Assert.InRange(joint.B, 0, fragment.Bodies.Length - 1);
                    AssertFinite(new(joint.OffsetA, joint.BasisA), "Worker joint A");
                    AssertFinite(new(joint.OffsetB, joint.BasisB), "Worker joint B");
                }
            }
            }
        }
        finally { setup.Rig.Return(scratch); }
    }

    static void AssertInertia(RagdollBody body, string context)
    {
        BodyInertia inertia;
        Vector3 rebuiltCentre = Vector3.Zero;
        float centreTolerance = float.PositiveInfinity;
        switch (body.Type)
        {
            case PhysicsShapeType.Sphere:
                Assert.True(float.IsFinite(body.Size.X) && body.Size.X > 0, context + " sphere size");
                inertia = new Sphere(body.Size.X).ComputeInertia(body.Mass);
                break;
            case PhysicsShapeType.Capsule:
                Assert.True(float.IsFinite(body.Size.X) && body.Size.X > 0 && float.IsFinite(body.Size.Y) && body.Size.Y >= 0, context + " capsule size");
                inertia = new Capsule(body.Size.X, body.Size.Y).ComputeInertia(body.Mass);
                break;
            case PhysicsShapeType.Mesh:
                Assert.NotNull(body.Hull);
                Assert.True(body.Hull.Length >= 4, context + " hull point count");
                Assert.All(body.Hull, point => Assert.True(Finite(point), context + " hull coordinates"));
                var min = body.Hull.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
                var max = body.Hull.Aggregate(new Vector3(float.MinValue), Vector3.Max);
                Assert.True(Finite(min) && Finite(max) && Vector3.DistanceSquared(min, max) > 0, context + " bounds");
                var pool = new BufferPool();
                try
                {
                    var hull = new BepuPhysics.Collidables.ConvexHull(body.Hull, pool, out var centre);
                    try
                    {
                        Assert.True(Finite(centre), context + " hull centre");
                        inertia = hull.ComputeInertia(body.Mass);
                        rebuiltCentre = centre;
                        centreTolerance = MathF.Max(0.0001f, Vector3.Distance(min, max) * 0.001f);
                    }
                    finally { hull.Dispose(pool); }
                }
                finally { pool.Clear(); }
                break;
            default:
                Assert.True(Finite(body.Size) && body.Size.X > 0 && body.Size.Y > 0 && body.Size.Z > 0, context + " box size");
                inertia = new Box(body.Size.X, body.Size.Y, body.Size.Z).ComputeInertia(body.Mass);
                break;
        }
        Assert.True(float.IsFinite(inertia.InverseMass) && inertia.InverseMass > 0, context + " inverse mass");
        var tensor = inertia.InverseInertiaTensor;
        float[] coefficients = [tensor.XX, tensor.YX, tensor.YY, tensor.ZX, tensor.ZY, tensor.ZZ];
        Assert.All(coefficients, coefficient => Assert.True(float.IsFinite(coefficient), context + " inertia coefficient"));
        double xx = tensor.XX, yx = tensor.YX, yy = tensor.YY, zx = tensor.ZX, zy = tensor.ZY, zz = tensor.ZZ;
        double minor = xx * yy - yx * yx;
        double determinant = xx * (yy * zz - zy * zy) - yx * (yx * zz - zx * zy) + zx * (yx * zy - zx * yy);
        Assert.True(xx > 0 && minor > 0 && determinant > 0, context + $" inertia is not positive definite (XX={xx}, minor={minor}, determinant={determinant})");
        Assert.True(rebuiltCentre.Length() <= centreTolerance, context + $" stored hull changes centre by {rebuiltCentre} when rebuilt (limit {centreTolerance})");
    }

    static void AssertFinite(RigidTransform pose, string context) => Assert.True(Finite(pose.Position)
        && float.IsFinite(pose.Rotation.X) && float.IsFinite(pose.Rotation.Y) && float.IsFinite(pose.Rotation.Z) && float.IsFinite(pose.Rotation.W)
        && pose.Rotation.LengthSquared() is > 0.99f and < 1.01f, context + " contains an invalid transform");
    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
