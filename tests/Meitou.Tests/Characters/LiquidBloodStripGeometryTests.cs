using System.Numerics;
using Meitou.Data.Particles;
using Meitou.Rendering;

namespace Meitou.Tests.Characters;

public class LiquidBloodStripGeometryTests
{
    static readonly Vector4 Blood = new(0.3f, 0.01f, 0.005f, 0.95f);

    [Fact]
    public void Curved_tapered_segments_share_both_joint_corners_from_different_views()
    {
        LiquidBloodStream[] chain =
        [
            new(new(0, 5, 0), new(1, 4, 0), 0.3f, Blood, 0.2f),
            new(new(1, 4, 0), new(2, 2, 1), 0.2f, Blood, 0.08f),
            new(new(2, 2, 1), new(2, 0, 2), 0.08f, Blood, 0.01f),
        ];
        foreach (var eye in new[] { new Vector3(0, 4, 10), new Vector3(10, 3, 0), new Vector3(0, 5, 0) })
        {
            var packed = Enumerable.Range(0, chain.Length).Select(i => LiquidBloodStripGeometry.Pack(chain, i, eye)).ToArray();
            Assert.True(packed[0].Rotation < 0); // Only the two ends of the whole chain are capped.
            Assert.True(packed[^1].Width < 0);
            for (int i = 1; i < packed.Length; i++)
            {
                Assert.True(packed[i - 1].Width > 0 && packed[i].Rotation > 0);
                Assert.Equal(MathF.Abs(packed[i - 1].Width), MathF.Abs(packed[i].Rotation));
                foreach (float side in new[] { -0.5f, 0.5f })
                {
                    var a = Corner(packed[i - 1], head: false, side);
                    var b = Corner(packed[i], head: true, side);
                    Assert.True(Finite(a) && Finite(b));
                    Assert.InRange(Vector3.Distance(a, b), 0, 1e-6f);
                }
            }
        }
    }

    [Fact]
    public void Separate_streams_keep_their_end_caps_and_view_aligned_edges_remain_finite()
    {
        LiquidBloodStream[] separate =
        [
            new(new(0, 0, -1), new(0, 0, 1), 0.2f, Blood),
            new(new(3, 0, -1), new(3, 0, 1), 0.1f, Blood),
        ];
        for (int i = 0; i < separate.Length; i++)
        {
            var packed = LiquidBloodStripGeometry.Pack(separate, i, Vector3.Zero);
            Assert.True(packed.Rotation < 0 && packed.Width < 0);
            Assert.True(Finite(Corner(packed, head: true, -0.5f)));
            Assert.True(Finite(Corner(packed, head: false, 0.5f)));
        }
    }

    [Fact]
    public void A_straight_liquid_strip_preserves_the_existing_particle_quad_winding()
    {
        var head = new Vector3(0, 1, 0);
        var tail = new Vector3(0, -1, 0);
        var eye = new Vector3(0, 0, 10);
        LiquidBloodStream[] stream = [new(head, tail, 0.2f, Blood)];
        var p = LiquidBloodStripGeometry.Pack(stream, 0, eye);
        var bottomLeft = Corner(p, head: false, -0.5f);
        var topLeft = Corner(p, head: true, -0.5f);
        var bottomRight = Corner(p, head: false, 0.5f);
        var actualNormal = Vector3.Cross(topLeft - bottomLeft, bottomRight - bottomLeft);
        var up = Vector3.Normalize(head - tail);
        var right = Vector3.Normalize(Vector3.Cross(up, eye - (head + tail) * 0.5f));
        var originalNormal = Vector3.Cross(up, right);
        Assert.True(Vector3.Dot(actualNormal, originalNormal) > 0);
    }

    [Fact]
    public void Invalid_endpoints_and_widths_are_rejected_before_stream_upload()
    {
        var stream = new LiquidBloodStream(Vector3.Zero, Vector3.UnitY, 0.1f, Blood, 0.05f);
        Assert.True(LiquidBloodStripGeometry.Valid(stream));
        Assert.False(LiquidBloodStripGeometry.Valid(stream with { Head = new(float.NaN, 0, 0) }));
        Assert.False(LiquidBloodStripGeometry.Valid(stream with { Tail = new(float.PositiveInfinity, 0, 0) }));
        Assert.False(LiquidBloodStripGeometry.Valid(stream with { Width = 0 }));
        Assert.False(LiquidBloodStripGeometry.Valid(stream with { TailWidth = float.NaN }));
        Assert.False(LiquidBloodStripGeometry.Valid(stream with { Tail = Vector3.Zero }));
    }

    // Decode the instance contract as the vertex shader does, then compare actual shared edge positions.
    static Vector3 Corner(ParticleInstance p, bool head, float side)
    {
        var oct = head ? new Vector2(p.Height, p.Depth) : new Vector2(p.Pad0, p.Pad1);
        var tangent = new Vector3(oct, 1 - MathF.Abs(oct.X) - MathF.Abs(oct.Y));
        float fold = Math.Clamp(-tangent.Z, 0, 1);
        tangent.X += tangent.X >= 0 ? -fold : fold;
        tangent.Y += tangent.Y >= 0 ? -fold : fold;
        tangent = Vector3.Normalize(tangent);
        var point = head ? p.Position : p.Direction;
        var right = Vector3.Cross(-tangent, -point);
        right = right.LengthSquared() > 1e-10f ? Vector3.Normalize(right) : Vector3.UnitX;
        return point + right * (side * MathF.Abs(head ? p.Rotation : p.Width));
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
