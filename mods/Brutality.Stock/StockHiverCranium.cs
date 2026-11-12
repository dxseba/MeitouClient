using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

internal static partial class StockHeadAnatomy
{
    // Hiver heads have a tapering shell, without the protruding teeth and sockets of the human asset.
    static OgreMesh HiverCranium(StockCranialEnvelope envelope, Vector3 minimum, Vector3 size,
        string skeleton, int bone, StockTissuePalette palette)
    {
        const int Rings = 12, Sides = 28;
        var points = new Vector3[Rings, Sides];
        var centre = minimum + size * .5f;
        for (int ring = 0; ring < Rings; ring++)
        {
            float t = ring / (float)(Rings - 1);
            float y = minimum.Y + size.Y * (.31f + t * .65f);
            var middle = envelope.Contain(centre with { Y = y });
            float taper = .18f + .82f * MathF.Pow(MathF.Max(0, MathF.Sin(t * MathF.PI)), .28f);
            for (int i = 0; i < Sides; i++)
            {
                float angle = MathF.Tau * i / Sides;
                var ray = new Vector3(MathF.Cos(angle) * size.X * .56f, 0, MathF.Sin(angle) * size.Z * .56f);
                var edge = envelope.Contain(centre with { X = centre.X + ray.X, Y = y, Z = centre.Z + ray.Z }, .05f);
                points[ring, i] = Vector3.Lerp(middle, edge, taper * (.93f + .03f * MathF.Sin(angle * 3 + t * 2)));
            }
        }

        var surface = new List<Vertex>(Rings * Sides * 6);
        for (int ring = 0; ring < Rings; ring++)
        {
            for (int i = 0; i < Sides; i++)
            {
                int next = (i + 1) % Sides;
                var a = points[ring, i]; var b = points[ring, next];
                if (ring + 1 < Rings)
                {
                    var c = points[ring + 1, i]; var d = points[ring + 1, next];
                    var expected = (a + b + c + d) * .25f - centre;
                    expected.Y = 0;
                    Triangle(a, b, c, expected);
                    Triangle(b, d, c, expected);
                }
                if (ring is 0 or Rings - 1)
                {
                    var middle = envelope.Contain(centre with { Y = a.Y });
                    Triangle(a, b, middle, ring == 0 ? -Vector3.UnitY : Vector3.UnitY);
                }
            }
        }
        var mesh = MeshCut.FromTriangles(surface, skeleton);
        OgreMeshWeld.Apply(mesh);
        return mesh;

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 expected)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(normal, expected) < 0) { (b, c) = (c, b); normal = -normal; }
            normal = Vector3.Normalize(normal);
            Add(a); Add(b); Add(c);

            void Add(Vector3 p)
            {
                float shade = .88f + .12f * MathF.Sin(p.X * 15 + p.Y * 14 - p.Z * 12);
                surface.Add(new Vertex { Position = p, Normal = normal, Colour = Shade(Vector4.Lerp(palette.Bone, palette.Fat, .16f), shade),
                    Bones = (uint)bone, Weights = Vector4.UnitX });
            }
        }
    }
}
