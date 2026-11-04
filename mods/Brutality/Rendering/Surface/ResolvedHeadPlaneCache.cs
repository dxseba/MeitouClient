using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>Cut planes depend on immutable bind geometry and appearance, never the current ragdoll pose.</summary>
internal sealed class ResolvedHeadPlaneCache(Func<OgreMesh, SkeletonRig, CharacterAppearance, Vector4, Vector4> resolve)
{
    internal const int PlanesPerAppearance = 64;
    readonly record struct Key(OgreMesh Body, SkeletonRig Rig, Vector4 Normalized);
    sealed class Entry { public readonly Dictionary<Key, Vector4> Planes = []; }
    readonly ConditionalWeakTable<CharacterAppearance, Entry> looks = new();

    public Vector4 For(OgreMesh body, SkeletonRig rig, CharacterAppearance look, Vector4 normalized)
    {
        var entry = looks.GetValue(look, static _ => new());
        var key = new Key(body, rig, normalized);
        lock (entry)
        {
            if (entry.Planes.TryGetValue(key, out var ready)) return ready;
            var value = resolve(body, rig, look, normalized);
            if (entry.Planes.Count >= PlanesPerAppearance) entry.Planes.Clear();
            entry.Planes.Add(key, value);
            return value;
        }
    }
    public void Clear() => looks.Clear();
}
