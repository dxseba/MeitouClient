using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

/// <summary>Filters source triangle lists without joining unrelated faces, layers or bones.</summary>
static class AnatomyTriangles
{
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
