using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Committed_dense_brain_closes_real_crossing_contours_inside_its_fitted_footprint_and_cap_budget()
    {
        using var scene = new Scene(withAnatomy: true);
        File.Copy(CommittedBrain(), scene.Adapter.Anatomy.PathFor(AnatomyKind.Brain)!, overwrite: true);
        var native = NativeGeometry.Build(scene.Adapter.Anatomy, scene.Appearance, Rig, scene.Body, default);
        var brain = NativeVertices(Assert.IsType<OgreMesh>(native.Anatomy)).Where(vertex => vertex.Tangent.W == 6).ToArray();
        Assert.True(brain.Length > 100_000, "Exercise the committed dense atlas, not the synthetic box brain.");
        var minimum = brain.Aggregate(new Vector3(float.MaxValue), (point, vertex) => Vector3.Min(point, vertex.Position));
        var maximum = brain.Aggregate(new Vector3(float.MinValue), (point, vertex) => Vector3.Max(point, vertex.Position));
        var centre = (minimum + maximum) * .5f;
        var headSkin = NativeCapGeometry.SelectHeadSkin(Model.Build(scene.Body, Rig.BoneCount).Parts
            .SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray(), Rig);
        var groups = new uint[(Rig.BoneCount + 7) / 8]; groups[0] = 7u << 12 | 6u << 16;
        // The mid-sagittal fissure is naturally open: offset the longitudinal plane into a hemisphere.
        var crossings = new[] { (Vector3.UnitX, (maximum.X - minimum.X) * .12f), (Vector3.UnitZ, -.08f), (Vector3.Normalize(new(.4f, .6f, 1)), .06f) };
        foreach (var (normal, offset) in crossings)
            foreach (int side in new[] { -1, 1 })
            {
                var plane = new Vector4(normal, -Vector3.Dot(normal, centre) - offset);
                var cut = new NativeSectionCut(plane, side, FullBody: true, HeadOnly: true);
                var flesh = NativeSectionGeometry.Build(headSkin, groups, cut);
                var complete = Assert.IsType<Vertex[]>(NativeCapGeometry.WithBrainSection(flesh, brain, groups, cut, null, 0x800000));
                var section = complete.Where(vertex => vertex.Uv.X == NativeSectionGeometry.BrainSectionBand).ToArray();
                Assert.NotEmpty(section);
                Assert.InRange(complete.Length, 1, NativeCapGeometry.VertexCapacity);
                Assert.All(section, vertex =>
                {
                    Assert.True(float.IsFinite(vertex.Position.LengthSquared()));
                    Assert.InRange(Vector4.Dot(plane, new(vertex.Position, 1)) * side, .0498f, .0502f);
                    var projected = vertex.Position - normal * (side * .05f);
                    for (int axis = 0; axis < 3; axis++) Assert.InRange(projected[axis], minimum[axis] - .0002f, maximum[axis] + .0002f);
                    Assert.True(Vector3.Dot(vertex.Normal, -normal * side) > .99f);
                    Assert.Equal(3u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights);
                });
            }
    }

    [Fact]
    public void Longitudinal_head_caps_never_include_the_torso_or_arm_sections_below_the_head()
    {
        using var scene = new Scene();
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null,
            State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit)));
        Assert.Equal(2, result.Detached.Count);
        foreach (var fragment in result.Detached)
        {
            var part = Assert.Single(fragment.Geometry.Parts!, part => part.Part.Field == "wound-section");
            var section = NativeVertices(Assert.IsType<OgreMesh>(part.Mesh));
            Assert.NotEmpty(section);
            Assert.All(section, vertex =>
            {
                Assert.True(vertex.Position.Y > 16.3f, "A head-only flesh cap must not contain a full-body sagittal contour.");
                Assert.Equal(3u, vertex.Bones); Assert.Equal(Vector4.UnitX, vertex.Weights);
            });
        }
    }

    static string CommittedBrain()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string file = Path.Combine(folder.FullName, "mods", "Brutality", "assets", "anatomy", "Brain.anatomy");
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException("The committed Brutality brain fixture is required for dense contour coverage.");
    }
}
