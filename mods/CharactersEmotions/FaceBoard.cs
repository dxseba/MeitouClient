using System.Collections.Concurrent;
using Meitou.Simulation;

namespace Meitou.CharactersEmotions;

/// <summary>
/// What the host's face clock hands the renderer and back: the shape key weights of each tracked character (replaced whole, read on the render
/// thread), and what each character's body mesh supports (written by the renderer). Characters are keyed by slot and generation, as the renderer's
/// draw key holds them.
/// </summary>
static class FaceBoard
{
    static volatile IReadOnlyDictionary<long, IReadOnlyDictionary<string, float>> s_weights =
        new Dictionary<long, IReadOnlyDictionary<string, float>>();

    /// <summary>The managed shape keys of each tracked character, set over its look's own weights; empty: none.</summary>
    public static IReadOnlyDictionary<long, IReadOnlyDictionary<string, float>> Weights
    {
        get => s_weights;
        set => s_weights = value;
    }

    /// <summary>Each drawn tracked character's mesh support, as the renderer last found it.</summary>
    public static ConcurrentDictionary<long, FaceSupport> Support { get; } = new();

    public static long Pack(CharacterId id) => (long)id.Slot << 32 | (uint)id.Generation;

    /// <summary>
    /// The character of a renderer draw key (<c>GameHost.DrawKey</c>: slot and generation plus one, the world's epoch in the top byte).
    /// </summary>
    public static long FromDrawKey(long key) => (key & 0x00FF_FFFF_FFFF_FFFFL) - 1;
}
