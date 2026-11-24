using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Meitou.Content;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;

namespace Meitou.KathunLightsabers;

/// <summary>
/// A matched saber: its colour, its blades as fractions of the drawn weapon frame (0 the hilt end, 1 the tip; <c>EffectAnchor.Weapon</c>) and
/// the model's length along that frame in its own units.
/// </summary>
sealed record Saber(SaberColour Colour, IReadOnlyList<(float From, float To)> Blades, float ModelLength);

/// <summary>
/// Which held weapon models are sabers (README "Facts"): a model of the settings' <c>mesh=</c> list, or one whose textures hold a <c>texture=</c>
/// keyword; its colour is the model entry's, else the texture entry's. Each model is looked at once (its file read on first sight, host thread).
/// </summary>
sealed class SaberCatalog(SaberConfig config, Action<string> log)
{
    static readonly Regex MaterialBlock = new(@"^\s*material\s+(\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    static readonly Regex TextureLine = new(@"^\s*texture\s+(\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    readonly ConcurrentDictionary<string, Saber?> _byMesh = new(StringComparer.OrdinalIgnoreCase);
    readonly GameInstall? _install = Locate();

    /// <summary>The saber a weapon model is (its record path), or null.</summary>
    public Saber? Of(string meshPath) => _byMesh.GetOrAdd(meshPath, Match);

    Saber? Match(string meshPath)
    {
        try
        {
            return MatchFile(meshPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidDataException
            or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            log($"weapon model {meshPath} not read ({e.Message})");
            return null;
        }
    }

    Saber? MatchFile(string meshPath)
    {
        string? file = _install is null ? null : CollisionPaths.Resolve(_install, meshPath);
        config.Meshes.TryGetValue(Path.GetFileName(meshPath.Replace('\\', '/')), out var entry);

        // The texture keyword is looked for only when the model entry does not name the colour or there is no entry.
        var texture = entry?.Colour is null && file is not null ? TextureOf(file) : null;
        if (entry is null && texture is null) return null;

        if (config.Colour(entry?.Colour ?? texture?.Colour) is not { } colour) return null;

        var (blades, length) = entry is null ? ([(0.3f, 1f)], ModelLengthOf(file)) : Fractions(entry.Blades, file);
        log($"saber {Path.GetFileName(meshPath)}: {colour.Name}, {blades.Count} blade(s)");
        return new Saber(colour, blades, length);
    }

    /// <summary>
    /// The model's blades along its y mapped onto the weapon frame, which spans the model's long extent from its hilt end (the bounds' y; the
    /// longer side is the tip side, as the engine's blade frame takes it). Without bounds the extent is 0 (or the lowest blade) to the highest.
    /// </summary>
    static (List<(float, float)> Blades, float Length) Fractions(IReadOnlyList<(float From, float To)> blades, string? file)
    {
        float lo = MathF.Min(0, blades.Min(b => MathF.Min(b.From, b.To)));
        float hi = blades.Max(b => MathF.Max(b.From, b.To));
        if (file is not null && OgreMeshReader.TryReadBounds(file, out var bounds)) (lo, hi) = (bounds.Min.Y, bounds.Max.Y);

        bool flipped = hi < -lo;
        if (flipped) (lo, hi) = (-hi, -lo);
        float span = MathF.Max(hi - lo, 1e-3f);

        float At(float y) => Math.Clamp(((flipped ? -y : y) - lo) / span, 0, 1);
        return ([.. blades.Select(b => (MathF.Min(At(b.From), At(b.To)), MathF.Max(At(b.From), At(b.To))))], span);
    }

    /// <summary>A model without an entry: its long extent from its bounds, else a single saber's (about 14.5 units).</summary>
    static float ModelLengthOf(string? file)
    {
        if (file is null || !OgreMeshReader.TryReadBounds(file, out var b)) return 14.5f;

        var size = b.Max - b.Min;
        return MathF.Max(MathF.Max(size.X, size.Y), MathF.Max(size.Z, 1e-3f));
    }

    /// <summary>The first texture entry whose keyword one of the model's materials or their textures hold; null when none does.</summary>
    SaberTexture? TextureOf(string file)
    {
        if (!File.Exists(file)) return null;

        var names = OgreMeshReader.ReadFile(file).SubMeshes.Select(s => s.MaterialName).Where(n => n.Length > 0).ToHashSet();
        var words = new List<string>(names);
        foreach (string script in MaterialScripts(file)) words.AddRange(TexturesOf(File.ReadAllText(script), names));

        return config.Textures.FirstOrDefault(t => words.Any(w => w.Contains(t.Keyword, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The texture names of the named materials' blocks in one material script.</summary>
    static IEnumerable<string> TexturesOf(string text, HashSet<string> names)
    {
        var blocks = MaterialBlock.Matches(text);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!names.Contains(blocks[i].Groups[1].Value)) continue;

            int end = i + 1 < blocks.Count ? blocks[i + 1].Index : text.Length;
            foreach (Match t in TextureLine.Matches(text[blocks[i].Index..end])) yield return t.Groups[1].Value;
        }
    }

    /// <summary>
    /// The material scripts of the model's mod: under the folder up from the model that holds a <c>.mod</c> file (at most four levels up), else
    /// the model's own folder.
    /// </summary>
    static IEnumerable<string> MaterialScripts(string file)
    {
        string? folder = Path.GetDirectoryName(file);
        if (folder is null) return [];

        string search = folder;
        string? up = folder;
        for (int level = 0; level < 4 && up is not null; level++, up = Path.GetDirectoryName(up))
        {
            if (!Directory.EnumerateFiles(up, "*.mod").Any()) continue;

            search = up;
            break;
        }

        return Directory.EnumerateFiles(search, "*.material", SearchOption.AllDirectories).Take(200);
    }

    static GameInstall? Locate()
    {
        try
        {
            return GameInstall.Locate();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
