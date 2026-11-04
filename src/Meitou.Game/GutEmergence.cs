using System.Numerics;
using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>A section spill starts inside the posed cut and feeds out while most cut remnants retain their pinned tissue.</summary>
internal static class GutEmergence
{
    public const float FeedAge = 0.5f, ReleaseAge = 3f;

    public static bool KeepAttached(CharacterId owner)
    {
        ulong key = (ulong)(uint)owner.Slot << 32 | (uint)owner.Generation;
        return Rng.Mix(key ^ 0x1E57A11CEUL) % 5 != 0;
    }

    public static bool AttachedAt(bool keepAttached, float age) => keepAttached || age < ReleaseAge;

    public static Vector3 Anchor(CharacterCutContact contact, float depth, float age)
    {
        float t = Math.Clamp(age / FeedAge, 0, 1);
        t = t * t * (3 - 2 * t);
        return contact.Position + contact.Normal * (-depth * (1 - t) + 0.06f * t);
    }
}
