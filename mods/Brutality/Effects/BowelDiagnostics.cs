using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

/// <summary>Opt-in developer visual isolation; no effect on gameplay, save data or tissue physics.</summary>
internal static class BowelDiagnostics
{
    public static readonly bool Hide = Enabled("MEITOU_BRUTALITY_NO_BOWELS");
    public static readonly bool Trace = Enabled("MEITOU_BRUTALITY_BOWEL_TRACE");
    static bool Enabled(string name) => Environment.GetEnvironmentVariable(name) is { } value
        && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    public static void Assets(float scale, BowelAssetSet assets)
    {
        if (assets.Whole is { } whole) Console.WriteLine($"Brutality bowel source: scale={scale:F3}, wholeVerts={whole.Source.Length}, {Bounds(whole.Source.Select(v => v.Position))}");
        for (int i = 0; i < assets.Halves.Length; i++)
        {
            var half = assets.Halves[i];
            Console.WriteLine($"Brutality bowel half{i}: verts={half.Source.Length}, radius={half.Skinning.Geometry.Radius:F3}, {Bounds(half.Source.Select(v => v.Position))}");
        }
    }

    public static void Piece(CharacterId owner, int half, double age, bool attached, BowelAsset asset, Vertex[] vertices, BowelPose pose)
    {
        var centre = pose.Centre;
        var nodes = pose.RestNodes.Select((node, i) => centre + Vector3.Transform(node, pose.Transforms[i])).ToArray();
        float flex = 0;
        for (int a = 0; a < nodes.Length; a++)
            for (int b = a + 1; b < nodes.Length; b++)
                flex = MathF.Max(flex, MathF.Abs(Vector3.Distance(nodes[a], nodes[b]) - Vector3.Distance(pose.RestNodes[a], pose.RestNodes[b])));
        Console.WriteLine($"Brutality bowel pose: owner={owner}, half={half}, age={age:F3}, attached={attached}, flex={flex:F3}, mesh {Bounds(vertices.Select(v => centre + v.Position))}, nodes {Bounds(nodes)}, rest {Bounds(asset.Source.Select(v => v.Position))}");
    }

    static string Bounds(IEnumerable<Vector3> points)
    {
        var (min, max) = StockTissueColour.Bounds(points); var size = max - min;
        return $"min=({min.X:F3},{min.Y:F3},{min.Z:F3}) max=({max.X:F3},{max.Y:F3},{max.Z:F3}) size=({size.X:F3},{size.Y:F3},{size.Z:F3})";
    }
}
