using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Physics;

public class RagdollFogPrinceTests
{
    // Candidate preserved in Clip's stack in MeitouClient.exe.41532.dmp; the minidump omitted the source point array.
    static readonly Vector4 DumpPlaneCandidate = new(-0.2391731888f, 0.9625774026f, 0.1274404079f, -12.64869976f);

    [Fact]
    [Slow]
    public void Installed_fog_prince_accepts_both_sides_of_the_crash_dump_plane_candidate()
    {
        var source = InstalledDefinition();
        foreach (bool fullBody in new[] { false, true })
            foreach (int side in new[] { -1, 1 })
                AssertFragment(source, DumpPlaneCandidate, side, fullBody, $"dump candidate, full body {fullBody}, side {side}");
    }

    [Fact]
    [Slow]
    public void Installed_fog_prince_cut_colliders_remain_valid_across_styles_approaches_and_offsets()
    {
        var source = InstalledDefinition();
        (TorsoBisectionStyle Style, int Direction, ulong Seed, int Degrees)[] cases =
        [
            (TorsoBisectionStyle.Transverse, 2, 0, 0), (TorsoBisectionStyle.Transverse, 3, 1, 45),
            (TorsoBisectionStyle.Oblique, 2, 3, 90), (TorsoBisectionStyle.Oblique, 3, 7, 225),
            (TorsoBisectionStyle.Vertical, 1, 1, 0), (TorsoBisectionStyle.Vertical, 1, 3, 135),
            (TorsoBisectionStyle.Diagonal, 1, 0, 270), (TorsoBisectionStyle.Diagonal, 1, 7, 315),
        ];
        foreach (var sample in cases)
        {
            float radians = sample.Degrees * MathF.PI / 180;
            var travel = new Vector2(MathF.Cos(radians), -MathF.Sin(radians));
            var cut = TorsoBisectionPolicy.StateForDirection(sample.Direction, sample.Seed, travel, -MathF.PI / 2, sample.Style);
            Assert.True(cut.IsValid);
            foreach (int side in new[] { -1, 1 })
                AssertFragment(source, cut.Plane, side, cut.FullBody, $"{sample.Style}, seed {sample.Seed}, approach {sample.Degrees}, side {side}");
        }
    }

    static RagdollDefinition InstalledDefinition()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var db = InstallData.FullLoadOrder!;
        const string fogPrince = "42028-rebirth.mod";
        Assert.SkipWhen(db.Find(fogPrince) is null, "Installed Fog Prince character not found");
        var look = CharacterAppearance.Build(db, install.Root, fogPrince, new() { Female = false, Naked = true });
        var assets = new AssetLocator(install);
        var rigs = new CharacterRigs(db,
            path => assets.Configured.Find(Path.GetFileName(path.Replace('\\', '/'))) ?? assets.Find(path), path => assets.Find(path));
        var setup = rigs.For(look);
        Assert.SkipWhen(setup is null, "Installed Fog Prince skeleton not found");
        var ragdollPath = assets.Find(look.Race.GetPath("male ragdoll"));
        Assert.SkipWhen(ragdollPath is null, "Installed Fog Prince ragdoll not found");
        Vector3[]? Hull(string name) => assets.Find(name) is { } path ? CookedMeshReader.ReadConvex(File.ReadAllBytes(path)).Vertices : null;
        return Assert.IsType<RagdollDefinition>(RagdollDefinition.Build(PhysicsFile.ReadFile(ragdollPath), setup.Rig, 1, Hull));
    }

    static void AssertFragment(RagdollDefinition source, Vector4 plane, int side, bool fullBody, string context)
    {
        var fragment = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, side, fullBody));
        Assert.NotEmpty(fragment.Bodies);
        Assert.InRange(fragment.Root, 0, fragment.Bodies.Length - 1);
        foreach (var body in fragment.Bodies)
        {
            string actor = context + ": " + body.Name;
            Assert.True(Finite(body.Bind.Position) && Finite(body.BoneInBody.Position), actor);
            Assert.InRange(body.Bind.Rotation.LengthSquared(), 0.999f, 1.001f);
            Assert.InRange(body.BoneInBody.Rotation.LengthSquared(), 0.999f, 1.001f);
            var original = source.Bodies[source.BodyOfBone[body.Bone]];
            var oldBone = original.BoneInBody.Then(original.Bind);
            var newBone = body.BoneInBody.Then(body.Bind);
            Assert.InRange(Vector3.Distance(oldBone.Position, newBone.Position), 0, 0.0001f);
            Assert.InRange(MathF.Abs(1 - MathF.Abs(Quaternion.Dot(oldBone.Rotation, newBone.Rotation))), 0, 0.0001f);
            RagdollInstalledRaceTests.AssertInertia(body, actor);
            if (fullBody && body.Hull is { } hull)
                Assert.All(hull, point => Assert.True(side * Distance(plane, body.Bind.Apply(point)) >= -0.001f, actor));
        }
        foreach (var joint in fragment.Joints)
        {
            Assert.InRange(joint.A, 0, fragment.Bodies.Length - 1);
            Assert.InRange(joint.B, 0, fragment.Bodies.Length - 1);
            Assert.True(Finite(joint.OffsetA) && Finite(joint.OffsetB), context + ": joint offsets");
        }
    }

    static float Distance(Vector4 plane, Vector3 point) => Vector3.Dot(new(plane.X, plane.Y, plane.Z), point) + plane.W;
    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
