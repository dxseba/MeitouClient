using System.Numerics;

namespace Meitou.Rendering.Characters;

internal sealed unsafe partial class CharacterRenderer
{
    internal static (Vector3 Centre, float Radius) BoundsOf(in CharacterInstance instance, float height, float radius, float scale) =>
        instance.WorldBounds is { IsValid: true } bounds ? (bounds.Centre, bounds.Radius)
            : (instance.Position + new Vector3(0, height * .5f * scale, 0), radius * scale);

    internal static (Vector3 Centre, float Radius) LoadingBounds(in CharacterInstance instance) =>
        instance.WorldBounds is { IsValid: true } bounds ? (bounds.Centre, bounds.Radius) : (instance.Position, 0);
}
