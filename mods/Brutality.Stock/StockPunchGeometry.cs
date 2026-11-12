using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Adapts the original flared chest tunnel to the stock CPU clipper and native rig.</summary>
internal static class StockPunchGeometry
{
    internal sealed record Tunnel(CutVolume Skin, CutVolume Clothing, OgreMesh Lining, Vertex[] Exit, Vertex[] Entry);

    internal static Tunnel? Build(OgreMesh body, SkeletonRig rig, CharacterAppearance look, in ChestHoleState hole)
    {
        if (!hole.IsValid) return null;
        bool shaved = look.Character?.GetBool("shaved") == true;
        var triangles = new List<Vertex>();
        foreach (var part in MeshCut.SourceModel(body, rig.BoneCount).Parts)
        {
            var offsets = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            foreach (uint index in part.Indices)
                triangles.Add(part.Vertices[index] with { Position = part.Vertices[index].Position + offsets[index] });
        }
        bool mechanical = look.Race.GetBool("is robot");
        var lining = ChestHoleGeometry.FitLining(triangles.ToArray(), rig, hole, StockTissuePalette.For(look),
            mechanical, body.SkeletonName, out var frame, out var exit, out var entry);
        if (lining is null) return null;
        var profile = ChestHoleGeometry.OpeningProfile(frame, mechanical);
        return new(Opening(frame, clothing: false, profile), Opening(frame, clothing: true, profile), lining, exit, entry);
    }

    static CutVolume Opening(ChestHoleFrame frame, bool clothing, float[]? profile)
    {
        int sides = profile?.Length ?? 28;
        float garment = clothing ? .16f : 0;
        float span = MathF.Max(frame.Radius * 2f, 1.8f);
        float entry = MathF.Max(.02f, frame.LiningRadius(-span) - .025f) + garment;
        float exit = MathF.Max(.02f, frame.LiningRadius(span) - .025f) + garment;
        var planes = new CutHalfspace[sides + 2];
        for (int side = 0; side < sides; side++)
        {
            float turn = side * MathF.Tau / sides, next = (side + 1) * MathF.Tau / sides;
            float current = profile?[side] ?? 1, following = profile?[(side + 1) % sides] ?? 1;
            var a = frame.Centre - frame.Axis * span + frame.Across(turn, entry * current);
            var b = frame.Centre - frame.Axis * span + frame.Across(next, entry * following);
            var c = frame.Centre + frame.Axis * span + frame.Across(turn, exit * current);
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (Vector3.Dot(normal, frame.Centre - a) < 0) normal = -normal;
            planes[side] = new(new(normal, -Vector3.Dot(normal, a)), MorphedDistance: true);
        }
        // Shoulder and hip bones can influence skin and garments inside the aperture.
        // Use its physical depth to contain the cut while allowing those triangles through.
        float depth = clothing ? MathF.Max(4, span + 1.4f) : MathF.Max(3.5f, span + .8f);
        float middle = Vector3.Dot(frame.Axis, frame.Centre);
        planes[sides] = new(new(frame.Axis, depth - middle), MorphedDistance: true);
        planes[sides + 1] = new(new(-frame.Axis, depth + middle), MorphedDistance: true);
        return new(planes, frame.Axis, FrontOnly: false, Group: -1,
            Bounds: new(frame.Centre, depth + MathF.Max(entry, exit) * MathF.Max(frame.Aspect, 1 / frame.Aspect) * (profile?.Max() ?? 1)));
    }
}
