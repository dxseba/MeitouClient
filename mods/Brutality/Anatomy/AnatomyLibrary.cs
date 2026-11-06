using System.Collections.Concurrent;

namespace Meitou.Brutality.Anatomy;

public enum AnatomyKind { Body, Brain, Mouth }

/// <summary>One cached import per local anatomy file, shared by all races and detached parts of this mod session.</summary>
public sealed class AnatomyLibrary(AnatomyAssets assets)
{
    readonly ConcurrentDictionary<AnatomyKind, Lazy<IReadOnlyList<AnatomyBatch>>> loaded = new();

    public string? PathFor(AnatomyKind kind) => kind switch
    {
        AnatomyKind.Body => assets.Ecorche,
        AnatomyKind.Brain => assets.Brain,
        AnatomyKind.Mouth => assets.Mouth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Missing optional anatomy yields no triangles. Invalid present data is reported rather than silently substituted.</summary>
    public IReadOnlyList<AnatomyBatch> Get(AnatomyKind kind) =>
        loaded.GetOrAdd(kind, k => new Lazy<IReadOnlyList<AnatomyBatch>>(() => Read(k))).Value;

    IReadOnlyList<AnatomyBatch> Read(AnatomyKind kind)
    {
        if (PathFor(kind) is not { } path) return Array.Empty<AnatomyBatch>();
        using var stream = File.OpenRead(path);
        return AnatomyFile.Read(stream);
    }
}
