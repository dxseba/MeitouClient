using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Split_head_caps_close_the_fitted_brain_footprint_with_cortex_and_lighter_internal_tissue()
    {
        using var scene = new Scene(withAnatomy: true);
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null,
            State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit)));
        Assert.Equal(2, result.Detached.Count);
        foreach (var fragment in result.Detached)
        {
            var anatomy = Assert.Single(fragment.Geometry.Parts!, part => part.Part.Field == "anatomy");
            var fittedBrain = NativeVertices(anatomy.Mesh!).Where(vertex => vertex.Tangent.W == 6).ToArray();
            Assert.NotEmpty(fittedBrain);
            var minimum = fittedBrain.Aggregate(new Vector3(float.MaxValue), (point, vertex) => Vector3.Min(point, vertex.Position));
            var maximum = fittedBrain.Aggregate(new Vector3(float.MinValue), (point, vertex) => Vector3.Max(point, vertex.Position));
            var cap = Assert.Single(fragment.Geometry.Parts!, part => part.Part.Field == "wound-section");
            var capVertices = NativeVertices(cap.Mesh!);
            var brainSection = capVertices.Where(vertex => vertex.Uv.X == NativeSectionGeometry.BrainSectionBand).ToArray();
            Assert.NotEmpty(brainSection);
            Assert.InRange(capVertices.Length, 1, NativeCapGeometry.VertexCapacity);
            var normal = Vector3.Normalize(new(fragment.CutPlane.X, fragment.CutPlane.Y, fragment.CutPlane.Z));
            float side = fragment.StableKey == 2 ? 1 : -1;
            Assert.All(brainSection, vertex =>
            {
                Assert.InRange(vertex.Position.Y, minimum.Y - .0001f, maximum.Y + .0001f);
                Assert.InRange(vertex.Position.Z, minimum.Z - .0001f, maximum.Z + .0001f);
                Assert.InRange(Vector4.Dot(fragment.CutPlane, new(vertex.Position, 1)) * side, .0499f, .0501f);
                Assert.True(Vector3.Dot(vertex.Normal, -normal * side) > .99f);
                Assert.Equal(32, vertex.Tangent.W);
                Assert.Equal(3u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights);
            });
            Assert.Contains(brainSection, vertex => vertex.Colour.X > .6f && vertex.Colour.Y > .4f);
            Assert.Contains(brainSection, vertex => vertex.Colour.X < .55f && vertex.Colour.Y < .4f);
            // The head's full skin footprint is taller than this fitted brain, so a skull-sized filler would fail.
            var head = HeadReliefGeometry.HeadPoints(scene.Body, Rig, scene.Appearance);
            float headHeight = head.Max(point => point.Y) - head.Min(point => point.Y);
            Assert.True(brainSection.Max(vertex => vertex.Position.Y) - brainSection.Min(vertex => vertex.Position.Y) < headHeight * .75f);
        }
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    public void Brain_sections_are_added_only_to_organic_full_body_or_head_only_cuts(int mode, bool mechanical, bool expected)
    {
        using var scene = new Scene(withAnatomy: true);
        var native = NativeGeometry.Build(scene.Adapter.Anatomy, scene.Appearance, Rig, scene.Body, default);
        var brain = NativeVertices(Assert.IsType<OgreMesh>(native.Anatomy)).Where(vertex => vertex.Tangent.W == 6).ToArray();
        Assert.NotEmpty(brain);
        var source = Model.Build(scene.Body, Rig.BoneCount).Parts.SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray();
        var rule = new CutHalfspace(new(0, 1, 0, -18), Group: mode == 2 ? 7 : -1, LegacyTorso: mode == 0);
        var cap = NativeCapGeometry.Build(source, Rig, null, scene.Appearance,
            [rule], null, 0x800000, mechanical, brainSource: brain);
        Assert.Equal(expected, cap is not null && NativeVertices(cap).Any(vertex => vertex.Uv.X == NativeSectionGeometry.BrainSectionBand));
        if (!expected)
        {
            // Legacy torso sections deliberately ignore head-group triangles, so this high cut may have no flesh cap either.
            var baseline = NativeCapGeometry.Build(source, Rig, null, scene.Appearance, [rule], null, 0x800000, mechanical);
            Assert.Equal(baseline is null, cap is null);
            if (baseline is not null && cap is not null) Assert.Equal(NativeVertices(baseline), NativeVertices(cap));
        }
    }

    [Fact]
    public void Brain_section_over_budget_keeps_the_existing_flesh_buffer_unchanged()
    {
        var brain = Box(new(-.6f, -.4f, -.5f), new(.6f, .4f, .5f), 3);
        var groups = new uint[(Rig.BoneCount + 7) / 8]; groups[0] = 7u << 12;
        var cut = new NativeSectionCut(new(1, 0, 0, 0), 1, FullBody: true, HeadOnly: true);
        var section = Assert.IsType<Vertex[]>(NativeCapGeometry.WithBrainSection(null, brain, groups, cut, null, 0x800000));
        Assert.NotEmpty(section);
        var existing = new Vertex[NativeCapGeometry.VertexCapacity - 2];
        Assert.Same(existing, NativeCapGeometry.WithBrainSection(existing, brain, groups, cut, null, 0x800000));
    }
}
