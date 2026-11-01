using System.Numerics;
using Meitou.Content;
using Meitou.Data.Ogre;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Characters;

public partial class TorsoCutGeometryTests
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
    public void A_concave_full_body_section_has_solid_recessed_flesh_without_bridging_its_open_gap()
    {
        Vector2[] outline = [new(-2, -2), new(2, -2), new(2, 2), new(1, 2), new(1, -1), new(-1, -1), new(-1, 2), new(-2, 2)];
        Vertex V(Vector2 p, float z) => new() { Position = new(p, z), Bones = 0, Weights = Vector4.UnitX };
        var source = new List<Vertex>();
        void Quad(Vertex a, Vertex b, Vertex c, Vertex d) => source.AddRange([a, b, c, b, d, c]);
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Length];
            Quad(V(a, -0.3f), V(b, -0.3f), V(a, 0.3f), V(b, 0.3f));
        }
        foreach (float z in new[] { -0.3f, 0.3f })
            foreach (var (min, max) in new[] { (new Vector2(-2, -2), new Vector2(2, -1)), (new Vector2(-2, -1), new Vector2(-1, 2)), (new Vector2(1, -1), new Vector2(2, 2)) })
                Quad(V(min, z), V(new(max.X, min.Y), z), V(new(min.X, max.Y), z), V(max, z));
        var cut = new CharacterTorsoCut(new(0, 0, 1, 0), 1, true);
        var cap = Assert.IsType<Vertex[]>(TorsoCutSurface.Build(source.ToArray(), [0], cut));
        var floor = Enumerable.Range(0, cap.Length / 3).Select(i => cap[(i * 3)..(i * 3 + 3)]).Where(t => t.All(p => p.Uv.X == TorsoCutSurface.BackingBand)).ToArray();
        Assert.NotEmpty(floor);
        float covered = 0;
        foreach (var triangle in floor)
        {
            Assert.All(triangle, p => Assert.InRange(p.Position.Z, 0.008f, 0.25f));
            Vector2 a = new(triangle[0].Position.X, triangle[0].Position.Y), b = new(triangle[1].Position.X, triangle[1].Position.Y), c = new(triangle[2].Position.X, triangle[2].Position.Y);
            static float Cross(Vector2 x, Vector2 y) => x.X * y.Y - x.Y * y.X;
            covered += MathF.Abs(Cross(b - a, c - a)) * 0.5f;
            var centre = (a + b + c) / 3;
            Assert.False(centre.X is > -0.85f and < 0.85f && centre.Y > -0.85f, "Backing crosses the open gap of the U-shaped fragment.");
        }
        Assert.InRange(covered, 5, 10);
    }

    [Fact]
    public void Limb_sections_have_bone_and_marrow_and_keep_their_physical_components_separate()
    {
        Vertex[] source = [.. Box(new(-3, 0, -0.5f), new(-1, 2, 0.5f)), .. Box(new(1, 0, -0.5f), new(3, 2, 0.5f), 1)];
        uint[] groups = [1u | 2u << 4];
        var cut = new CharacterTorsoCut(new(0, 0, 1, 0), 1, true);
        var cap = Assert.IsType<Vertex[]>(TorsoCutSurface.Build(source, groups, cut, components: [7, 9]));
        Assert.Contains(cap, p => p.Colour.X > 0.6f && p.Colour.Y > 0.5f);
        Assert.Contains(cap, p => p.Colour.X is > 0.29f and < 0.31f && p.Colour.Y is > 0.05f and < 0.06f);
        Assert.Contains(cap, p => p.Uv.X == 2);
        for (int i = 0; i < cap.Length; i += 3)
        {
            var triangle = cap[i..(i + 3)];
            Assert.True(triangle.All(p => p.Position.X < -0.8f) || triangle.All(p => p.Position.X > 0.8f));
            int expected = triangle[0].Position.X < 0 ? 0 : 1;
            Assert.All(triangle, p =>
            {
                Assert.Equal(expected, (int)(p.Bones & 255));
                Assert.Equal(Vector4.UnitX, p.Weights);
            });
        }
    }
    [Fact]
    public void Full_body_caps_and_picking_support_front_back_and_opposite_diagonal_planes()
    {
        var source = Box(new(-1, -1, -1), new(1, 1, 1));
        Vector3[] normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ, new(1, 0.7f, 1), new(-1, -0.7f, 1)];
        foreach (var raw in normals)
        {
            var normal = Vector3.Normalize(raw);
            var cut = new CharacterTorsoCut(new(normal, 0), 1, true);
            var opposite = new CharacterTorsoCut(-cut.Plane, -1, true);
            var cap = TorsoCutSurface.Build(source, [0], cut)!;
            var sameHalf = TorsoCutSurface.Build(source, [0], opposite)!;
            Assert.NotNull(cap);
            Assert.NotNull(sameHalf);
            Assert.Equal(cap.Length, sameHalf.Length);
            Assert.All(cap, v =>
            {
                Assert.True(float.IsFinite(v.Position.LengthSquared()));
                Assert.True(Vector3.Dot(v.Normal, normal) <= 1e-5f);
                Assert.Contains(sameHalf, other => Vector3.DistanceSquared(v.Position, other.Position) <= 2.5e-9f);
            });
            Assert.Equal(3, TorsoCutSurface.RayBox(-normal * 3, normal, new(-1), new(1), 3, cut)!.Value, 1e-5f);
            var parallel = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.8f ? Vector3.UnitY : Vector3.UnitX));
            Assert.Null(TorsoCutSurface.RayBox(-normal * 0.5f, parallel, new(-1), new(1), 6, cut));
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
        var cases = new List<(string Name, Vector4 Plane, bool Head, bool Leg, bool Torso)>
        {
            ("centre left/right", new(1, 0, 0, 0), true, false, false),
            ("centre front/back", new(0, 0, 1, 0), true, true, false),
            ("long chest/pelvis/leg", new(1, 0.20f, 0, -2.2f), false, true, false),
        };
        // Couple direction, offset, tilt and pivot to cover the new envelope without a large Cartesian product.
        for (int direction = 0; direction < 8; direction++)
        {
            float angle = direction * MathF.PI / 4;
            var horizontal = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float radius = 1 / MathF.Sqrt(horizontal.X * horizontal.X / (0.70f * 0.70f) + horizontal.Y * horizontal.Y / (0.40f * 0.40f));
            if (direction % 2 == 0)
            {
                float offset = (direction % 4 == 0 ? 1 : -1) * radius;
                cases.Add(($"vertical extreme {direction}", new(horizontal.X, 0, horizontal.Y, -offset), false, false, false));
            }
            bool oblique = direction % 2 != 0;
            float tilt = (oblique ? 35 : 16) * MathF.PI / 180 * (direction % 4 < 2 ? 1 : -1);
            float pivot = oblique ? direction < 4 ? 11.8f : 14 : direction < 4 ? 10.2f : 11.5f;
            float useOffset = direction % 3 == 0 ? 0 : (direction % 3 == 1 ? 1 : -1) * (oblique ? 1.1f : 0.8f) * radius;
            var normal = Vector3.Normalize(new(horizontal.X, MathF.Tan(tilt), horizontal.Y));
            cases.Add(($"diagonal extreme {direction}", new(normal, -Vector3.Dot(normal, new(horizontal.X * useOffset, pivot, horizontal.Y * useOffset))), false, false, false));
        }
        for (ulong seed = 0; seed < 8; seed++)
        {
            float angle = seed * MathF.PI / 4;
            var style = seed % 2 == 0 ? TorsoBisectionStyle.Vertical : TorsoBisectionStyle.Diagonal;
            var state = TorsoBisectionPolicy.StateForDirection(seed % 3 == 0 ? 7 : 1, seed, new(MathF.Sin(angle), MathF.Cos(angle)), angle * 0.3f, style);
            Assert.True(state.FullBody);
            cases.Add(($"policy {style} seed {seed}", state.Plane, false, false, false));
        }
        (float Tilt, float Pivot, float Azimuth, float Offset)[] torsoCuts = [(40, 12, 0, 1), (-40, 13.2f, 0, -1), (60, 12, 25, -1), (-60, 13.2f, -25, 1)];
        foreach (var torso in torsoCuts)
        {
            float azimuth = torso.Azimuth * MathF.PI / 180;
            var horizontal = new Vector2(MathF.Cos(azimuth), MathF.Sin(azimuth));
            float radius = 1 / MathF.Sqrt(horizontal.X * horizontal.X / (0.70f * 0.70f) + horizontal.Y * horizontal.Y / (0.40f * 0.40f));
            float offset = torso.Offset * radius * 0.25f;
            var normal = Vector3.Normalize(new(horizontal.X, MathF.Tan(torso.Tilt * MathF.PI / 180), horizontal.Y));
            cases.Add(($"torso oblique {torso.Tilt} pivot {torso.Pivot}", new(normal, -Vector3.Dot(normal, new(horizontal.X * offset, torso.Pivot, horizontal.Y * offset))), false, false, true));
        }
        foreach (var sampleCase in cases)
        {
            var plane = sampleCase.Plane / new Vector3(sampleCase.Plane.X, sampleCase.Plane.Y, sampleCase.Plane.Z).Length();
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            var intersections = SectionSegments(source, plane);
            Assert.NotEmpty(intersections);
            foreach (int side in new[] { -1, 1 })
            {
                string sample = $"{file}: {sampleCase.Name} side {side}";
                var cut = new CharacterTorsoCut(plane, side, true);
                var cap = TorsoCutSurface.Build(source, groups, cut);
                Assert.True(cap is { Length: >= 12 and <= 32768 }, sample + " did not fit a bounded full-body cap");
                Assert.All(cap!, v => Assert.True(float.IsFinite(v.Position.LengthSquared()), sample + " produced a nonfinite cap"));
                var rim = cap!.Where(v => v.Uv.X < 0.5f).ToArray();
                if (sampleCase.Leg) Assert.True(rim.Min(v => v.Position.Y) < 10, sample + " did not continue through the pelvis into a leg");
                if (sampleCase.Head) Assert.True(rim.Max(v => v.Position.Y) > 17.5f, sample + " did not cap the sliced head");
                if (sampleCase.Leg) Assert.True(rim.Max(v => v.Position.Y) > 13.5f, sample + " did not reach the chest");
                if (sampleCase.Torso) Assert.Contains(rim, v => v.Position.Y is >= 11 and <= 14 && MathF.Abs(v.Position.X) < 1.5f && MathF.Abs(v.Position.Z) < 1.5f);
                foreach (var vertex in rim) Assert.InRange(MathF.Abs(Vector3.Dot(normal, vertex.Position) + plane.W), 0, 0.04f);
                Assert.Contains(source, vertex => (Vector3.Dot(normal, vertex.Position) + plane.W) * side > 0.05f);
                CheckSectionBacking(cap, source, plane, side, sample);
                int triangles = cap.Length / 3, stride = Math.Max(1, cap.Count(p => p.Uv.X < TorsoCutSurface.BackingBand) / 3 / 96), rimTriangle = 0;
                for (int triangle = 0; triangle < triangles; triangle++)
                {
                    var a = cap[triangle * 3]; var b = cap[triangle * 3 + 1]; var c = cap[triangle * 3 + 2];
                    var outer = new[] { a, b, c }.Where(v => v.Uv.X < 0.5f).ToArray();
                    if (outer.Length == 2)
                    {
                        float distance = SectionDistance((outer[0].Position + outer[1].Position) / 2, intersections);
                        Assert.True(distance <= 0.05f, sample + $" bridges unrelated section edges ({distance})");
                    }
                    if (a.Uv.X < TorsoCutSurface.BackingBand && b.Uv.X < TorsoCutSurface.BackingBand && c.Uv.X < TorsoCutSurface.BackingBand && rimTriangle++ % stride == 0)
                        Assert.True(SectionDistance((a.Position + b.Position + c.Position) / 3, intersections) <= 0.38f, sample + " stretches flesh across a neck, limb or pelvis gap");
                }
            }
        }
    }

    static List<(Vector3 A, Vector3 B)> SectionSegments(Vertex[] source, Vector4 plane)
    {
        var result = new List<(Vector3, Vector3)>();
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var points = new List<Vector3>(3);
            for (int edge = 0; edge < 3; edge++)
            {
                var a = source[triangle + edge].Position; var b = source[triangle + (edge + 1) % 3].Position;
                float da = Vector3.Dot(normal, a) + plane.W, db = Vector3.Dot(normal, b) + plane.W;
                if (MathF.Abs(da) <= 1e-5f) points.Add(a);
                else if ((da < 0) != (db < 0)) points.Add(Vector3.Lerp(a, b, da / (da - db)));
            }
            if (points.Count == 2) result.Add((points[0], points[1]));
            else if (points.Count == 3)
                for (int edge = 0; edge < 3; edge++) result.Add((points[edge], points[(edge + 1) % 3]));
        }
        return result;
    }

    static float SectionDistance(Vector3 point, List<(Vector3 A, Vector3 B)> segments)
    {
        float nearest = float.MaxValue;
        foreach (var (a, b) in segments)
        {
            var delta = b - a;
            float length = delta.LengthSquared();
            float along = length > 1e-12f ? Math.Clamp(Vector3.Dot(point - a, delta) / length, 0, 1) : 0;
            nearest = MathF.Min(nearest, Vector3.DistanceSquared(point, a + delta * along));
        }
        return MathF.Sqrt(nearest);
    }
}
