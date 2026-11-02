using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class RobotCutContactTests
{
    [Fact]
    public void Cached_contacts_sample_a_bounded_primary_cut_rim_without_backing_or_brain_geometry()
    {
        var rim = Ring(100, 1);
        Vertex[] cap =
        [
            .. rim, .. rim,
            .. Ring(4, 0).Select(v => v with { Position = v.Position + new Vector3(1000, 0, 0), Uv = new(3, 0) }),
            .. Ring(4, 0).Select(v => v with { Position = v.Position + new Vector3(-1000, 0, 0), Uv = new(4, 0) }),
            .. Ring(4, 0).Select(v => v with { Position = v.Position + new Vector3(0, 1000, 0), Uv = new(0, 1) }),
        ];
        var samples = CutContactGeometry.Sample(cap);
        Assert.InRange(samples.Length, 3, CutContactGeometry.SampleLimit);
        Assert.Equal(samples.Length, samples.Select(v => v.Position).Distinct().Count());
        Assert.All(samples, vertex =>
        {
            Assert.InRange(vertex.Position.X, -1, 1);
            Assert.Equal(12, vertex.Position.Y);
            Assert.InRange(vertex.Position.Z, -1, 1);
            Assert.Equal(1u, vertex.Bones);
        });
        var kept = samples.Select(v => v.Position).ToArray();
        Array.Fill(cap, new Vertex { Position = new(999, 999, 999) });
        Assert.Equal(kept, samples.Select(v => v.Position).ToArray());
    }

    [Fact]
    public void The_same_cut_rim_tracks_independent_robot_halves_or_heads_through_their_own_bones_and_world_transforms()
    {
        var rim = CutContactGeometry.Sample(Ring(8, 1));
        Matrix4x4[] left = [Matrix4x4.CreateTranslation(0, 10000, 0),
            Matrix4x4.CreateRotationZ(.4f) * Matrix4x4.CreateTranslation(-50, 1, 20)];
        Matrix4x4[] right = [Matrix4x4.CreateTranslation(0, -10000, 0),
            Matrix4x4.CreateRotationZ(-.3f) * Matrix4x4.CreateTranslation(70, 2, -30)];
        var world = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(4, 500, -2);
        var a = Assert.IsType<CharacterCutContact>(CutContactGeometry.Pose(rim, left, world));
        var b = Assert.IsType<CharacterCutContact>(CutContactGeometry.Pose(rim, right, world));
        CheckContact(a, new(0, 12, 0), left[1], world);
        CheckContact(b, new(0, 12, 0), right[1], world);
        Assert.True(Vector3.Distance(a.Position, b.Position) > 100);

        left[1].M42 += 2;
        var moved = Assert.IsType<CharacterCutContact>(CutContactGeometry.Pose(rim, left, world));
        CheckContact(moved, new(0, 12, 0), left[1], world);
        Assert.InRange(Vector3.Distance(moved.Position - a.Position, Vector3.TransformNormal(new(0, 2, 0), world)), 0, 1e-4f);
        Assert.Equal(b, CutContactGeometry.Pose(rim, right, world));
    }

    [Fact]
    public void Empty_or_invalid_contacts_cannot_emit_nan_anchors_and_missing_bones_use_the_finite_world_fallback()
    {
        Assert.Null(CutContactGeometry.Pose([], [], Matrix4x4.Identity));
        var invalidWorld = Matrix4x4.Identity;
        invalidWorld.M41 = float.NaN;
        Assert.Null(CutContactGeometry.Pose(Ring(8, 1), [Matrix4x4.Identity, Matrix4x4.Identity], invalidWorld));
        var zeroNormals = Ring(8, 1).Select(v => v with { Normal = Vector3.Zero }).ToArray();
        Assert.Null(CutContactGeometry.Pose(zeroNormals, [Matrix4x4.Identity, Matrix4x4.Identity], Matrix4x4.Identity));
        var missing = Ring(8, 255);
        var world = Matrix4x4.CreateRotationY(.5f) * Matrix4x4.CreateTranslation(3, 7, 11);
        var fallback = Assert.IsType<CharacterCutContact>(CutContactGeometry.Pose(missing, [Matrix4x4.Identity], world));
        CheckContact(fallback, new(0, 12, 0), Matrix4x4.Identity, world);
    }

    static void CheckContact(CharacterCutContact contact, Vector3 centre, Matrix4x4 bone, Matrix4x4 world)
    {
        var expected = Vector3.Transform(Vector3.Transform(centre, bone), world);
        var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.TransformNormal(Vector3.UnitY, bone), world));
        Assert.True(float.IsFinite(contact.Position.LengthSquared()));
        Assert.InRange(Vector3.Distance(expected, contact.Position), 0, 1e-4f);
        Assert.InRange(Vector3.Distance(normal, contact.Normal), 0, 1e-5f);
    }

    static Vertex[] Ring(int count, uint bone) => Enumerable.Range(0, count).Select(index =>
    {
        float angle = index * MathF.Tau / count;
        var point = new Vector3(MathF.Cos(angle), 12, MathF.Sin(angle));
        return new Vertex { Position = point, Normal = Vector3.UnitY, Uv = Vector2.Zero,
            Tangent = new(point, TorsoCutSurface.Layer), Bones = bone, Weights = Vector4.UnitX };
    }).ToArray();
}
