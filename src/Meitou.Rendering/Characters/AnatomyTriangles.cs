using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

/// <summary>Filters source triangle lists without joining unrelated faces, layers or bones.</summary>
static class AnatomyTriangles
{
    /// <summary>The anatomy file assigns one rigid bone to a whole batch; nearby skin supplies UV/clearance, never unrelated articulated weights.</summary>
    public static Vertex FollowBone(in Vertex vertex, int bone) => vertex with { Bones = (uint)bone, Weights = Vector4.UnitX };

    public static IEnumerable<EcorcheVertex> BelowHeight(EcorcheVertex[] source, float height)
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
