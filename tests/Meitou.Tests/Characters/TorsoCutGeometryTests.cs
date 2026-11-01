using System.Numerics;
using Meitou.Content;
using Meitou.Data.Ogre;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Characters;

public class TorsoCutGeometryTests
{
    static Vertex[] Box(Vector3 min, Vector3 max, uint bone = 0, uint morphSlot = 0)
    {
        Vertex V(int x, int y, int z) => new() { Position = new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z), Bones = bone, Weights = new(1, 0, 0, 0), Colour = new(BitConverter.UInt32BitsToSingle(morphSlot), 0, 0, 1) };
        var result = new List<Vertex>();
        void Quad(Vertex a, Vertex b, Vertex c, Vertex d) => result.AddRange([a, b, c, b, d, c]);
        Quad(V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(1, 1, 0));
        Quad(V(0, 0, 1), V(0, 1, 1), V(1, 0, 1), V(1, 1, 1));
        Quad(V(0, 0, 0), V(0, 1, 0), V(0, 0, 1), V(0, 1, 1));
        Quad(V(1, 0, 0), V(1, 0, 1), V(1, 1, 0), V(1, 1, 1));
        Quad(V(0, 0, 0), V(0, 0, 1), V(1, 0, 0), V(1, 0, 1));
        Quad(V(0, 1, 0), V(1, 1, 0), V(0, 1, 1), V(1, 1, 1));
        return result.ToArray();
    }

    [Fact]
    public void Torso_plane_normalizes_and_keeps_whole_arms_with_upper_half_and_legs_with_lower()
    {
        Assert.True(TorsoCutSurface.TryNormalize(new(new(0, 2, 0, -25), 1), out var upper));
        Assert.Equal(new Vector4(0, 1, 0, -12.5f), upper.Plane);
        var lower = upper with { KeepSide = -1 };
        Assert.True(TorsoCutSurface.Band(new(0, 10, 0), 1, upper) > 0);
        Assert.True(TorsoCutSurface.Band(new(0, 10, 0), 2, lower) < 0);
        Assert.True(TorsoCutSurface.Band(new(0, 15, 0), 3, upper) < 0);
        Assert.True(TorsoCutSurface.Band(new(0, 15, 0), 4, lower) > 0);
        Assert.True(TorsoCutSurface.Band(new(0, 11, 0), 0, lower) > 0);
        Assert.True(TorsoCutSurface.Band(new(0, 14, 0), 0, upper) > 0);
        Assert.False(TorsoCutSurface.TryNormalize(new(default, 1), out _));
        Assert.False(TorsoCutSurface.TryNormalize(new(new(0, 1, 0, float.NaN), 1), out _));
        Assert.False(TorsoCutSurface.TryNormalize(new(new(0, 1, 0, -12.5f), 0), out _));
        Assert.False(TorsoCutSurface.TryNormalize(new(new(1, 0, 0, 0), 1), out _));
        Assert.Equal(1, TorsoCutSurface.Packed(upper).M21);
        Assert.Equal(-1, TorsoCutSurface.Packed(lower).M21);
    }

    [Fact]
    public void Both_caps_follow_the_real_torso_section_exclude_arm_islands_and_leave_organs_an_open_centre()
    {
        Vertex[] source = [.. Box(new(-1, 0, -0.6f), new(1, 2, 0.6f)), .. Box(new(3, 0, -0.3f), new(4, 2, 0.3f), 1)];
        uint[] groups = [1u << 4];
        foreach (int side in new[] { -1, 1 })
        {
            var cap = TorsoCutSurface.Build(source, groups, new(new(0, 1, 0, -1), side));
            Assert.NotNull(cap);
            Assert.InRange(cap.Length, 12, 8192);
            Assert.All(cap, v =>
            {
                Assert.InRange(v.Position.X, -1.01f, 1.01f);
                Assert.InRange(v.Position.Z, -0.61f, 0.61f);
                Assert.True(v.Normal.Y * side <= 0);
                Assert.Equal(TorsoCutSurface.Layer, v.Tangent.W);
                Assert.Equal(1, v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W, 1e-5f);
            });
            for (int i = 0; i + 2 < cap.Length; i += 3)
            {
                static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
                Vector2 a = new(cap[i].Position.X, cap[i].Position.Z), b = new(cap[i + 1].Position.X, cap[i + 1].Position.Z), c = new(cap[i + 2].Position.X, cap[i + 2].Position.Z);
                float denominator = Cross(b - a, c - a);
                if (MathF.Abs(denominator) < 1e-8f) continue;
                float u = Cross(-a, c - a) / denominator, v = Cross(b - a, -a) / denominator;
                Assert.False(u >= 0 && v >= 0 && u + v <= 1, "A flat cap triangle covers the centre that should expose organs");
            }
        }
    }

    [Fact]
    public void Torso_cap_interpolates_the_body_morph_once()
    {
        var source = Box(new(-1, 0, -0.6f), new(1, 2, 0.6f), morphSlot: 1);
        var cut = new CharacterTorsoCut(new(0, 1, 0, -1), 1);
        var original = TorsoCutSurface.Build(source, [0], cut)!;
        var moved = TorsoCutSurface.Build(source, [0], cut, [new(0.13f, 0.2f, -0.1f, 0)])!;
        Assert.Equal(original.Length, moved.Length);
        for (int i = 0; i < original.Length; i++) Assert.InRange(Vector3.Distance(new(0.13f, 0.2f, -0.1f), moved[i].Position - original[i].Position), 0, 2e-6f);
    }

    [Fact]
    public void Picking_ignores_removed_half_boxes_and_can_enter_the_kept_volume_at_the_cut_plane()
    {
        var upper = new CharacterTorsoCut(new(0, 1, 0, -1), 1);
        var lower = upper with { KeepSide = -1 };
        var min = new Vector3(-1, 0, -0.6f); var max = new Vector3(1, 2, 0.6f);
        Assert.Null(TorsoCutSurface.RayBox(new(0, 0.2f, -3), Vector3.UnitZ, min, max, 0, upper));
        Assert.NotNull(TorsoCutSurface.RayBox(new(0, 0.2f, -3), Vector3.UnitZ, min, max, 0, lower));
        Assert.Equal(2, TorsoCutSurface.RayBox(new(0, -1, 0), Vector3.UnitY, min, max, 0, upper)!.Value, 1e-5f);
        Assert.NotNull(TorsoCutSurface.RayBox(new(0, 0.2f, -3), Vector3.UnitZ, min, max, 1, upper));
        Assert.Null(TorsoCutSurface.RayBox(new(0, 0.2f, -3), Vector3.UnitZ, min, max, 1, lower));
        Assert.NotNull(TorsoCutSurface.RayBox(new(0, 1.8f, -3), Vector3.UnitZ, min, max, 3, lower));
        Assert.Null(TorsoCutSurface.RayBox(new(0, 1.8f, -3), Vector3.UnitZ, min, max, 3, upper));
    }

    [Fact]
    public void Full_body_planes_split_every_group_and_keep_disconnected_caps_separate()
    {
        Vertex[] source = [.. Box(new(-1, 0, -0.6f), new(1, 2, 0.6f)), .. Box(new(-0.4f, 4, -0.3f), new(0.4f, 5, 0.3f), 1)];
        uint[] groups = [3u << 4];
        foreach (int side in new[] { -1, 1 })
        {
            Assert.True(TorsoCutSurface.TryNormalize(new(new(2, 0, 0, 0), side, true), out var cut));
            Assert.Equal(new Vector4(1, 0, 0, 0), cut.Plane);
            Assert.Equal(1, TorsoCutSurface.Packed(cut).M23);
            for (int group = 0; group <= 6; group++)
            {
                Assert.True(TorsoCutSurface.Band(new(side, 1, 0), group, cut) > 0);
                Assert.True(TorsoCutSurface.Band(new(-side, 1, 0), group, cut) < 0);
                Assert.Null(TorsoCutSurface.RayBox(new(-side * 0.5f, 1, -3), Vector3.UnitZ, new(-1, 0, -1), new(1, 2, 1), group, cut));
            }
            var cap = TorsoCutSurface.Build(source, groups, cut)!;
            Assert.NotNull(cap);
            Assert.Equal(2, cap.Select(v => v.Uv.Y).Distinct().Count());
            Assert.All(cap, v => Assert.True(float.IsFinite(v.Position.LengthSquared()) && v.Normal.X * side <= 1e-5f));
            for (int i = 0; i < cap.Length; i += 3)
            {
                Assert.Equal(cap[i].Uv.Y, cap[i + 1].Uv.Y);
                Assert.Equal(cap[i].Uv.Y, cap[i + 2].Uv.Y);
                Assert.InRange(MathF.Max(cap[i].Position.Y, MathF.Max(cap[i + 1].Position.Y, cap[i + 2].Position.Y))
                    - MathF.Min(cap[i].Position.Y, MathF.Min(cap[i + 1].Position.Y, cap[i + 2].Position.Y)), 0, 2.01f);
            }
            var samples = TorsoCutSurface.KeptVertices(source, groups, cut).ToArray();
            Assert.All(samples, v => Assert.True(v.Position.X * side >= -1e-6f));
            Assert.Contains(samples, v => v.Position.X == 0 && v.Position.Y >= 4);
        }
    }

    [Fact]
    public void Soft_tissue_cutoff_keeps_complete_triangles_without_joining_later_organ_faces()
    {
        static EcorcheVertex V(float x, float y) => new(new(x, y, 0), Vector3.UnitZ, default);
        EcorcheVertex[] source = [V(0, 0), V(1, 1), V(2, 1.5f), V(3, 1), V(4, 2.5f), V(5, 1.5f), V(6, 0.5f), V(7, 1.5f), V(8, 2)];
        var kept = AnatomyTriangles.BelowHeight(source, 2).ToArray();
        Assert.Equal(6, kept.Length);
        Assert.Equal(0, kept.Length % 3);
        Assert.Equal(source[..3], kept[..3]);
        Assert.Equal(source[6..], kept[3..]);
        Assert.All(kept, v => Assert.True(v.Position.Y <= 2));
    }

    [Theory]
    [InlineData("male", "human_male.mesh")]
    [InlineData("female", "human_female.mesh")]
    [Slow]
    public void Installed_human_caps_fit_varied_height_and_angle_extrema_without_cutting_limb_junctions(string gender, string file)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var path = GameFiles.Resolve(Path.Combine(install.DataDirectory, "character", "meshes", "human", file));
        CheckInstalledCaps(install, gender, path, file);
    }

    [Theory]
    [InlineData("Stick_person2.mesh")]
    [InlineData("Stick_person3.mesh")]
    [Slow]
    public void Installed_worker_and_soldier_high_hip_junctions_stay_below_varied_torso_caps(string file)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var path = CollisionPaths.Resolve(install, "mods/VanillaRace4CharactersEmotions/character/meshes/CEBodies/" + file);
        Assert.SkipWhen(path is null, "Installed CE Worker/Soldier body not found");
        CheckInstalledCaps(install, "male", path!, file);
    }

    static void CheckInstalledCaps(GameInstall install, string gender, string path, string file)
    {
        var skeletonPath = GameFiles.Resolve(Path.Combine(install.DataDirectory, "character", "meshes", gender + "_skeleton", gender + "_skeleton.skeleton"));
        Assert.SkipWhen(!File.Exists(skeletonPath), "Human skeleton not found");
        var skeleton = OgreSkeletonReader.ReadFile(skeletonPath);
        int bones = skeleton.Bones.Max(b => b.Handle) + 1;
        var groups = new uint[8];
        foreach (var bone in skeleton.Bones) if (bone.Handle < 64) groups[bone.Handle / 8] |= (uint)(bone.Name == "Bip01 Jaw" ? 6 : WoundMasks.BodyGroup(bone.Name)) << (bone.Handle % 8 * 4);
        var model = Model.Build(OgreMeshReader.ReadFile(path), bones);
        Vertex[] source = [.. model.Parts.SelectMany(p => p.Indices.Select(i => p.Vertices[i]))];
        float maxX = TorsoBisectionPolicy.MaximumSlopeX, maxZ = TorsoBisectionPolicy.MaximumSlopeZ;
        (float X, float Z)[] slopes = [(0, 0), (maxX, 0), (-maxX, 0), (0, maxZ), (0, -maxZ), (maxX, maxZ), (maxX, -maxZ), (-maxX, maxZ), (-maxX, -maxZ)];
        var planes = new List<Vector4>();
        foreach (float height in new[] { TorsoBisectionPolicy.MinimumHeight, (TorsoBisectionPolicy.LegJunctionCeiling + TorsoBisectionPolicy.ShoulderJunctionFloor) / 2, TorsoBisectionPolicy.MaximumHeight })
            foreach (var slope in slopes)
            {
                float use = TorsoBisectionPolicy.JunctionHalfWidth * MathF.Abs(slope.X) + TorsoBisectionPolicy.JunctionHalfDepth * MathF.Abs(slope.Z);
                float budget = MathF.Min(TorsoBisectionPolicy.ShoulderJunctionFloor - height, height - TorsoBisectionPolicy.LegJunctionCeiling);
                float scale = use > budget ? budget / use : 1;
                planes.Add(new(slope.X * scale, 1, slope.Z * scale, -height));
            }
        for (ulong seed = 0; seed < 16; seed++)
        {
            float angle = seed * 0.7f;
            planes.Add(TorsoBisectionPolicy.PlaneForDirection(seed % 3 == 0 ? 2 : seed % 3 == 1 ? 3 : 9, seed, new(MathF.Sin(angle), MathF.Cos(angle)), angle * 0.5f));
        }
        foreach (var plane in planes)
        {
            string sample = $"{file}: height {-plane.W / plane.Y}, slopes {plane.X / plane.Y}/{plane.Z / plane.Y}";
            for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
            {
                var vertices = source.AsSpan(triangle, 3);
                int a = TorsoCutSurface.Group(vertices[0], groups), b = TorsoCutSurface.Group(vertices[1], groups), c = TorsoCutSurface.Group(vertices[2], groups);
                if (a != 0 && b != 0 && c != 0) continue;
                bool arm = a is 1 or 2 || b is 1 or 2 || c is 1 or 2;
                bool leg = a is 3 or 4 || b is 3 or 4 || c is 3 or 4;
                if (!arm && !leg) continue;
                foreach (var vertex in vertices)
                {
                    float distance = Vector3.Dot(new(plane.X, plane.Y, plane.Z), vertex.Position) + plane.W;
                    Assert.True(arm ? distance >= 0.05f : distance <= -0.05f, sample + " crosses an arm/leg attachment strip");
                }
            }
            foreach (int side in new[] { -1, 1 })
            {
                var cap = TorsoCutSurface.Build(source, groups, new(plane, side));
                Assert.True(cap is { Length: >= 12 and <= 8192 }, sample + " did not fit a bounded cap");
                var normalized = plane / new Vector3(plane.X, plane.Y, plane.Z).Length();
                Assert.All(cap!, v =>
                {
                    Assert.True(float.IsFinite(v.Position.LengthSquared()), sample + " produced a nonfinite vertex");
                    if (v.Uv.X < 0.5f) Assert.InRange(MathF.Abs(Vector3.Dot(new(normalized.X, normalized.Y, normalized.Z), v.Position) + normalized.W), 0, 0.04f);
                });
            }
        }
        CheckInstalledFullBody(source, groups, file);
    }

    static void CheckInstalledFullBody(Vertex[] source, uint[] groups, string file)
    {
        foreach (var style in new[] { TorsoBisectionStyle.Vertical, TorsoBisectionStyle.Diagonal })
            for (ulong seed = 0; seed < 4; seed++)
            {
                var state = TorsoBisectionPolicy.StateForDirection(seed % 2 == 0 ? 1 : 7, seed, new(0.4f, -0.9f), 0.3f, style);
                Assert.True(state.FullBody);
                foreach (int side in new[] { -1, 1 })
                {
                    string sample = $"{file}: {style} seed {seed} side {side}";
                    var cap = TorsoCutSurface.Build(source, groups, new(state.Plane, side, true));
                    Assert.True(cap is { Length: >= 12 and <= 32768 }, sample + " did not fit a bounded full-body cap");
                    Assert.All(cap!, v => Assert.True(float.IsFinite(v.Position.LengthSquared()), sample + " produced a nonfinite cap"));
                    var rim = cap!.Where(v => v.Uv.X < 0.5f).ToArray();
                    Assert.True(rim.Min(v => v.Position.Y) < 10, sample + " did not continue through the pelvis into a leg");
                    Assert.True(rim.Max(v => v.Position.Y) > 13.5f, sample + " did not reach the chest");
                    if (style == TorsoBisectionStyle.Vertical) Assert.True(rim.Max(v => v.Position.Y) > 17.5f, sample + " did not cap the sliced head");
                    foreach (var v in rim)
                        Assert.InRange(MathF.Abs(Vector3.Dot(new(state.Plane.X, state.Plane.Y, state.Plane.Z), v.Position) + state.Plane.W), 0, 0.04f);
                    for (int i = 0; i < cap.Length; i += 3)
                    {
                        Assert.Equal(cap[i].Uv.Y, cap[i + 1].Uv.Y);
                        Assert.Equal(cap[i].Uv.Y, cap[i + 2].Uv.Y);
                    }
                }
            }
    }
}
