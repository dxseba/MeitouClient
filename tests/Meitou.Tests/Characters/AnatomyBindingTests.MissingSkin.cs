using System.Numerics;
using System.Text;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public partial class AnatomyBindingTests
{
    [Fact]
    public void Missing_same_limb_skin_does_not_borrow_the_other_arms_uv_or_fit_surface()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.AddRange([
            new() { Name = "Bip01 Pelvis", Handle = 0 },
            new() { Name = "Bip01 Neck", Handle = 1, Parent = 0, Position = new(0, 6.1649f, 0) },
            new() { Name = "Bip01 L Forearm", Handle = 2, Parent = 0, Position = new(-2, 3, 0) },
            new() { Name = "Bip01 R Forearm", Handle = 3, Parent = 0, Position = new(2, 3, 0) },
        ]);
        var rig = new SkeletonRig(skeleton);
        WoundPoint[] skin = [new(new(-100, 3, 0), Vector3.UnitX, new(0.9f, 0.8f), 3, 3, Vector4.UnitX)];
        Vector3[] points = [new(-2, 3, 0), new(-1.5f, 3, 0), new(-2, 3.6f, 0)];
        string folder = Path.Combine(Path.GetTempPath(), "Meitou-anatomy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "Ecorche.anatomy");
            using (var writer = new BinaryWriter(File.Create(path), Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("ECR1"));
                writer.Write(1);
                var name = Encoding.ASCII.GetBytes("Bip01 L Forearm");
                writer.Write(name.Length);
                writer.Write(1);
                writer.Write(3);
                writer.Write(name);
                foreach (var point in points)
                {
                    var stored = point + new Vector3(-0.0446148f, 9.956298f, -0.227702f);
                    foreach (float value in new[] { stored.X, stored.Y, stored.Z, 0, 0, 1, 0, 0 }) writer.Write(value);
                }
            }
            var made = Assert.IsType<List<Vertex>>(CharacterAnatomy.Build(rig, skin, [], false, null, false, false, path));
            var muscle = made.Where(vertex => vertex.Tangent.W == 1).ToArray();
            Assert.Equal(3, muscle.Length);
            Assert.All(muscle, vertex =>
            {
                Assert.Equal(Vector2.Zero, vertex.Uv);
                Assert.Equal(2u, vertex.Bones);
                Assert.Equal(Vector4.UnitX, vertex.Weights);
                Assert.InRange(vertex.Position.X, -2.01f, -1.49f);
                Assert.InRange(vertex.Position.Z, -0.21f, -0.19f);
            });
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
