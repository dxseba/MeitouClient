// Adapted from the integrated GPL-3.0-or-later renderer at 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Rendering;
using Meitou.Brutality.Anatomy;
using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>Filters source triangle lists without joining unrelated faces, layers or bones.</summary>
static class AnatomyTriangles
{
    /// <summary>The anatomy file assigns one rigid bone to a whole batch; nearby skin supplies UV/clearance, never unrelated articulated weights.</summary>
    public static Vertex FollowBone(in Vertex vertex, int bone) => vertex with { Bones = (uint)bone, Weights = Vector4.UnitX };

    public static IEnumerable<AnatomyVertex> BelowHeight(AnatomyVertex[] source, float height)
    {
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            if (source[i].Position.Y > height || source[i + 1].Position.Y > height || source[i + 2].Position.Y > height) continue;
            yield return source[i];
            yield return source[i + 1];
            yield return source[i + 2];
        }
    }
}
