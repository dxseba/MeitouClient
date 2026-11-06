using Meitou.Content;

namespace Meitou.Brutality;

/// <summary>Optional anatomy shipped beside this mod; no dependency on another mod's installation path.</summary>
public sealed class AnatomyAssets
{
    readonly string folder;

    public AnatomyAssets(string modFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modFolder);
        folder = Path.GetFullPath(modFolder);
        Ecorche = Find("Ecorche.anatomy");
        Brain = Find("Brain.anatomy");
        Mouth = Find("Mouth.anatomy");
    }

    public string? Ecorche { get; }
    public string? Brain { get; }
    public string? Mouth { get; }

    public string? Resolve(string name) => name switch
    {
        "Ecorche.anatomy" => Ecorche,
        "Brain.anatomy" => Brain,
        "Mouth.anatomy" => Mouth,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown anatomy asset."),
    };

    public string Require(string name) => Resolve(name) ?? throw new FileNotFoundException($"Missing mod anatomy asset: {name}");

    string? Find(string name)
    {
        var path = GameFiles.Resolve(Path.GetFullPath(Path.Combine(folder, "assets", "anatomy", name)));
        var relative = Path.GetRelativePath(folder, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Anatomy asset resolved outside the mod folder.");
        return File.Exists(path) ? path : null;
    }
}
