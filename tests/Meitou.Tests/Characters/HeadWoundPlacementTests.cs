using System.Numerics;
using Meitou.Content;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class HeadWoundPlacementTests
{
    static Vertex[] Wall(float z, Vector3 normal, uint morphSlot = 0)
    {
        Vertex V(float x, float y) => new() { Position = new(x, y, z), Normal = normal, Uv = new(0.5f, -0.5f), Colour = new(BitConverter.UInt32BitsToSingle(morphSlot), 0, 0, 1) };
        return [V(-2, -2), V(2, -2), V(-2, 2), V(2, -2), V(2, 2), V(-2, 2)];
    }
    static void Near(Vector3 expected, Vector3 actual) => Assert.InRange(Vector3.Distance(expected, actual), 0, 0.00002f);

    [Fact]
    public void Estimated_anchor_inside_head_projects_to_the_incoming_surface_not_the_far_wall()
    {
        Vertex[] source = [.. Wall(-1, -Vector3.UnitZ), .. Wall(1, Vector3.UnitZ)];
        var placement = new CharacterHeadWoundPlacement(new(0.2f, 0.3f, 0), Vector3.UnitZ, new(1, 0, 0.4f));
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 0, out var front));
        Near(new(0.2f, 0.3f, 1), front.Centre);
        Near(Vector3.UnitZ, front.Normal);
        Near(Vector3.UnitX, front.Along);
        Assert.Equal(1, front.Packed.M41);
        Assert.Equal(front.Centre.Z, front.Packed.M13);
        Assert.Equal(front.HalfWidth, front.Packed.M24);

        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement with { Normal = -Vector3.UnitZ }, 0, out var back));
        Near(new(0.2f, 0.3f, -1), back.Centre);
        Near(-Vector3.UnitZ, back.Normal);
        Assert.NotEqual(front.Packed, back.Packed);
    }

    [Fact]
    public void Invalid_or_unsupported_placement_keeps_the_legacy_path()
    {
        var source = Wall(1, Vector3.UnitZ);
        var valid = new CharacterHeadWoundPlacement(default, Vector3.UnitZ, Vector3.UnitX);
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Normal = default }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Normal = new(float.MaxValue, float.MaxValue, float.MaxValue) }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Point = new(float.NaN, 0, 0) }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Point = new(20, 20, 0) }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Tangent = Vector3.UnitZ }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { Tangent = new(float.MaxValue, float.MaxValue, float.MaxValue) }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { LengthScale = float.NaN }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { WidthScale = float.PositiveInfinity }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { LengthScale = 0.49f }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement(source, valid with { WidthScale = 2.01f }, 0, out _));
        Assert.False(HeadWoundSurface.TryProjectPlacement([], valid, 0, out _));
    }

    [Fact]
    public void Placement_mesh_applies_face_morph_once_while_retaining_the_original_skin_frame()
    {
        var source = Wall(1, Vector3.UnitZ, 1);
        var placement = new CharacterHeadWoundPlacement(default, Vector3.UnitZ, Vector3.UnitX);
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 3, out var frame));
        var plain = HeadWoundSurface.PlacementVertices(source, frame, 4, null, 3, true);
        Vector4[] morph = [new(0.1f, -0.2f, 0.3f, 0)];
        var moved = HeadWoundSurface.PlacementVertices(source, frame, 4, morph, 3, true);
        Assert.NotNull(plain); Assert.NotNull(moved);
        Assert.NotEmpty(plain);
        Assert.Equal(plain.Length, moved.Length);
        Assert.InRange(plain.Length, 1, 4096);
        for (int i = 0; i < plain.Length; i++) Near(new(0.1f, -0.2f, 0.3f), moved[i].Position - plain[i].Position);
        Near(new(0, 0, 1), frame.Centre);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Placement_scales_dimensions_without_moving_the_fixed_anchor_or_axis(int mechanism)
    {
        var source = Wall(1, Vector3.UnitZ);
        var placement = new CharacterHeadWoundPlacement(default, Vector3.UnitZ, Vector3.UnitX);
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 0, out var original, mechanism));
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement with { LengthScale = 1, WidthScale = 1 }, 0, out var legacy, mechanism));
        Assert.Equal(original, legacy);
        var scaled = placement with { LengthScale = 1.2f, WidthScale = 1.3f };
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, scaled, 0, out var frame, mechanism));
        Near(original.Centre, frame.Centre);
        Near(original.Along, frame.Along);
        Near(original.Normal, frame.Normal);
        Assert.Equal(original.HalfLength * 1.2f, frame.HalfLength);
        Assert.Equal(original.HalfWidth * 1.3f, frame.HalfWidth);
        var vertices = HeadWoundSurface.FitPlacementVertices(source, frame, 0, null, 0, true, out var fitted);
        Assert.NotNull(vertices);
        Near(frame.Centre, fitted.Centre);
        Near(frame.Along, fitted.Along);
        Assert.Equal(frame.HalfWidth, fitted.HalfWidth);
        Assert.Equal(frame.HalfLength, fitted.Packed.M14);
        Assert.Equal(frame.HalfWidth, fitted.Packed.M24);
    }

    [Fact]
    public void Mechanisms_keep_the_same_surface_anchor_and_axis_but_have_distinct_compact_footprints()
    {
        var source = Wall(1, Vector3.UnitZ);
        var placement = new CharacterHeadWoundPlacement(default, Vector3.UnitZ, Vector3.UnitX);
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 3, out var slash));
        Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 3, out var explicitSlash, 1));
        var approved = HeadWoundSurface.PlacementVertices(source, slash, 0, null, 3, true)!;
        var selectedSlash = HeadWoundSurface.PlacementVertices(source, explicitSlash, 0, null, 3, true)!;
        Assert.Equal(slash.HalfLength, explicitSlash.HalfLength);
        Assert.Equal(slash.HalfWidth, explicitSlash.HalfWidth);
        Assert.Equal(approved.Length, selectedSlash.Length);
        for (int i = 0; i < approved.Length; i++)
        {
            Assert.Equal(approved[i].Position, selectedSlash[i].Position);
            Assert.Equal(approved[i].Colour, selectedSlash[i].Colour);
            Assert.Equal(approved[i].Normal, selectedSlash[i].Normal);
        }

        static float Span(Vertex[] vertices, Vector3 axis) => vertices.Max(v => Vector3.Dot(v.Position, axis)) - vertices.Min(v => Vector3.Dot(v.Position, axis));
        var compact = new List<(float Length, float Width)>();
        for (int mechanism = 2; mechanism <= 3; mechanism++)
        {
            Assert.True(HeadWoundSurface.TryProjectPlacement(source, placement, 3, out var frame, mechanism));
            Near(slash.Centre, frame.Centre);
            Near(slash.Along, frame.Along);
            Near(slash.Normal, frame.Normal);
            Assert.Equal(mechanism, frame.Packed.M42);
            var vertices = HeadWoundSurface.FitPlacementVertices(source, frame, 0, null, 3, true, out var fitted);
            Assert.NotNull(vertices);
            Assert.InRange(vertices.Length, 1, 4096);
            Near(frame.Centre, fitted.Centre);
            compact.Add((Span(vertices, frame.Along), Span(vertices, frame.Side)));
        }
        float slashLength = Span(approved, slash.Along);
        Assert.True(compact[0].Length < slashLength * 0.5f);
        Assert.True(compact[1].Length < slashLength * 0.7f);
        Assert.True(compact[1].Width > compact[0].Width * 1.5f);
        Assert.True(compact[1].Length / compact[1].Width < 2);
    }

    [Fact]
    public void Compact_mechanism_fallback_keeps_shared_body_parts_without_legacy_slash_relief()
    {
        static AssetPart Part(string label, bool relief) => new() { Label = label, IsHeadRelief = relief, Shared = true, Mesh = null!, Material = null!, BoundsCentre = default, BoundsRadius = 1 };
        var asset = new CharacterAsset { Appearance = null!, Rig = null! };
        var body = Part("body", false);
        var oldRelief = Part("legacy slash relief", true);
        asset.Parts.AddRange([body, oldRelief]);
        for (uint mechanism = 0; mechanism <= 3; mechanism++)
        {
            var fallback = CharacterHeadWoundPool.Fallback(asset, 1u << 7 | mechanism << 12);
            Assert.False(fallback.ReliefReady);
            Assert.Equal(0, fallback.Frame.M41);
            Assert.Equal(mechanism, fallback.Frame.M42);
            Assert.Same(body, fallback.Parts[0]);
            if (mechanism < 2) Assert.Same(asset.Parts, fallback.Parts);
            else Assert.Single(fallback.Parts);
        }
        Assert.Equal(2, asset.Parts.Count);
    }

    [Theory]
    [InlineData("human_male.mesh")]
    [InlineData("human_female.mesh")]
    [Slow]
    public void Human_head_projection_covers_incoming_sides(string file)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var path = GameFiles.Resolve(Path.Combine(install.DataDirectory, "character", "meshes", "human", file));
        var source = HeadWoundSurface.Source(Model.Build(OgreMeshReader.ReadFile(path), null));
        for (int side = 0; side < 8; side++)
        {
            float angle = side * MathF.PI / 4;
            var normal = Vector3.Normalize(new Vector3(MathF.Sin(angle), 0.15f, MathF.Cos(angle)));
            var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
            var up = Vector3.Normalize(Vector3.Cross(normal, across));
            for (int mechanism = 1; mechanism <= 3; mechanism++)
                for (int profile = 0; profile < 6; profile++)
                {
                    var along = (profile % 3) switch { 1 => Vector3.Normalize(-across * 0.83f + up * 0.55f), 2 => up, _ => Vector3.Normalize(across * 0.83f + up * 0.55f) };
                    var placement = new CharacterHeadWoundPlacement(new(0, 18.65f, 0), normal, along);
                    (float Length, float Width)[] scales = profile < 3 ? [(1, 1), (0.9f, 0.85f), (1.2f, 1.3f)] : [(1, 1)];
                    foreach (var (length, width) in scales)
                    {
                        var scaled = placement with { LengthScale = length, WidthScale = width };
                        string sample = $"{file}: side {side}, profile {profile}, mechanism {mechanism}, scales {length}/{width}";
                        Assert.True(HeadWoundSurface.TryProjectPlacement(source, scaled, profile, out var frame, mechanism), sample + " did not project to head skin");
                        Assert.True(Vector3.Dot(frame.Normal, normal) > 0, sample + " selected the opposite wall");
                        var vertices = HeadWoundSurface.FitPlacementVertices(source, frame, 0, null, profile, true, out var fitted);
                        Assert.True(vertices is { Length: > 0 and <= 4096 }, sample + " could not fit the cut");
                        Near(frame.Centre, fitted.Centre);
                        Near(frame.Along, fitted.Along);
                        Assert.Equal(mechanism, fitted.Packed.M42);
                        Assert.InRange(fitted.HalfLength, frame.HalfLength * 0.85f, frame.HalfLength);
                    }
                }
        }
    }
}

public class HeadWoundAnatomyBoundsTests
{
    [Fact]
    public void Internal_head_bounds_reject_torso_spikes_and_horn_morph_slots_while_retaining_tall_heads()
    {
        const int head = 21, neck = 20, jaw = 23;
        static Vertex V(Vector3 point, uint bones, Vector4 weights, uint slot = 0, float v = -0.5f) => new()
        {
            Position = point, Uv = new(0.5f, v), Bones = bones, Weights = weights,
            Colour = new(BitConverter.UInt32BitsToSingle(slot), 0, 0, 1),
        };
        Vertex[] source =
        [
            V(new(-1, 17, -1), head, new(1, 0, 0, 0)),
            V(new(1, 20, 1), head, new(1, 0, 0, 0), 4),
            V(new(0, 22, 0), head, new(1, 0, 0, 0), 3),
            V(new(0, 16.8f, 0), head | jaw << 8 | neck << 16, new(0.3f, 0.3f, 0.4f, 0)),
            V(new(-12, 5, -9), neck, new(1, 0, 0, 0)),
            V(new(-5, 18, -8), head, new(1, 0, 0, 0), 1),
            V(new(5, 21, -7), head, new(1, 0, 0, 0), 2),
            V(new(100), head, new(1, 0, 0, 0), v: 0.5f),
        ];
        var morphs = new MeshMorphs
        {
            SlotCount = 4, SubMeshIndex = 0,
            Poses = new() { ["bone_horns_top_short"] = ([1, 4], [Vector3.UnitX, Vector3.Zero]), ["stick_long_horns"] = ([2], [Vector3.UnitZ]), ["head_height"] = ([3], [Vector3.UnitY]) },
        };
        Assert.True(HeadWoundSurface.TryAnatomyBounds(source, head, jaw, morphs, out var min, out var max));
        Assert.Equal(new Vector3(-1, 16.8f, -1), min);
        Assert.Equal(new Vector3(1, 22, 1), max);
        Assert.False(HeadWoundSurface.TryAnatomyBounds(source, -1, jaw, morphs, out _, out _));
        Assert.False(HeadWoundSurface.TryAnatomyBounds([source[4]], head, jaw, morphs, out _, out _));
    }
}
