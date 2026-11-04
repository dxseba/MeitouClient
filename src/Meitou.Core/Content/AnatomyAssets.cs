namespace Meitou.Content;

/// <summary>The optional anatomical bundle, resolved once a gore renderer actually needs it.</summary>
public static class AnatomyAssets
{
    /// <summary>
    /// The bundled body file beside the application; older local installs may use the original mod folder.
    /// Brain and mouth files always come from the selected body's directory, never another bundle.
    /// </summary>
    public static string? FindBody(string? legacyModsDirectory = null)
    {
        string bundled = GameFiles.Resolve(Path.Combine(AppContext.BaseDirectory, "assets", "anatomy", "Ecorche.anatomy"));
        if (File.Exists(bundled)) return bundled;
        if (legacyModsDirectory is null) return null;
        string legacy = GameFiles.Resolve(Path.Combine(legacyModsDirectory, "Gauntlet of Crowns", "Ecorche.anatomy"));
        return File.Exists(legacy) ? legacy : null;
    }

    /// <summary>An optional companion of the chosen body file, with its actual spelling on case-sensitive systems.</summary>
    public static string? Companion(string bodyFile, string fileName)
    {
        string path = GameFiles.Resolve(Path.Combine(Path.GetDirectoryName(bodyFile)!, fileName));
        return File.Exists(path) ? path : null;
    }
}
