using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality;

internal static partial class StockHeadAnatomy
{
    /// <summary>Weld a local tissue shell on a fine grid and remove collapsed or duplicate faces.</summary>
    static List<Vertex> Simplify(List<Vertex> source, Vector3 size, int budget)
    {
        float cell = MathF.Max(.033f, MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * .011f);
        var output = source;
        for (int attempt = 0; attempt < 4; attempt++, cell *= 1.40f)
        {
            var vertices = new Dictionary<Vector3, int>();
            var faces = new HashSet<(int A, int B, int C)>();
            output = new List<Vertex>(Math.Min(source.Count, budget * 3));
            for (int i = 0; i + 2 < source.Count; i += 3)
            {
                var a = Snap(source[i]); var b = Snap(source[i + 1]); var c = Snap(source[i + 2]);
                if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).LengthSquared() < 1e-12f) continue;
                int x = VertexId(a.Position), y = VertexId(b.Position), z = VertexId(c.Position);
                int min = Math.Min(x, Math.Min(y, z)), max = Math.Max(x, Math.Max(y, z));
                if (!faces.Add((min, x + y + z - min - max, max))) continue;
                output.Add(a); output.Add(b); output.Add(c);
            }
            if (output.Count <= budget * 3) break;

            Vertex Snap(Vertex v)
            {
                var p = v.Position / cell;
                return v with { Position = new Vector3(MathF.Round(p.X), MathF.Round(p.Y), MathF.Round(p.Z)) * cell };
            }

            int VertexId(Vector3 position)
            {
                if (vertices.TryGetValue(position, out int found)) return found;
                int id = vertices.Count;
                vertices.Add(position, id);
                return id;
            }
        }
        return output.Count == 0 ? source : output;
    }
}
