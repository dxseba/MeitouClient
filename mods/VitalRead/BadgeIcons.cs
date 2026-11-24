using Meitou.Content;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.VitalRead;

/// <summary>
/// The state icons: each state's texture, size and crop (<see cref="IconConfig"/>), found as the original looks for them and drawn as image
/// nodes. The original's own PNGs are not shipped with the port: a state whose file is not found has no icon.
/// </summary>
sealed class BadgeIcons(IReadOnlyList<string> folders, Action<string> log)
{
    readonly Dictionary<VitalState, IconSource> _sources = IconConfig.Load(folders, log);

    /// <summary>
    /// Each state's file once looked for (null: not found).
    /// </summary>
    readonly Dictionary<VitalState, string?> _files = [];

    /// <summary>
    /// The game's interface pictures (<c>data/gui/gfx</c>), found once; null without an install.
    /// </summary>
    readonly string? _gameGfx = GameInstall.Locate() is { } install ? Path.Combine(install.DataDirectory, "gui", "gfx") : null;

    /// <summary>
    /// <paramref name="state"/>'s icon as an image node's file and crop; null when it has none. Without a crop the original shows the top left
    /// square of the icon's size (0: the picture's width), so the whole of a picture that size.
    /// </summary>
    public (string File, UiImageCrop? Crop)? Image(VitalState state)
    {
        if (!_sources.TryGetValue(state, out var source) || File(state, source) is not { } file) return null;
        if (source.Crop is { } c) return (file, UiImageCrop.Pixels(c.X, c.Y, c.Width, c.Height));

        return (file, source.Size > 0 ? UiImageCrop.Pixels(0, 0, source.Size, source.Size) : null);
    }

    string? File(VitalState state, IconSource source)
    {
        if (_files.TryGetValue(state, out var known)) return known;

        string? file = Find(source.Texture);
        if (VitalReadMod.Settings.DebugLogging) log($"{state} icon '{source.Texture}': {file ?? "not found"}");
        return _files[state] = file;
    }

    /// <summary>
    /// The original's lookup, in each folder (this mod's, then the installed original's): the path as given (absolute, or under the folder), then
    /// the bare name in its <c>icons/</c>, <c>gui/gfx/</c> and itself; then the game's <c>data/gui/gfx</c>.
    /// </summary>
    string? Find(string texture)
    {
        if (texture.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || Path.GetFileName(texture) is not { Length: > 0 } name) return null;

        var candidates = folders.SelectMany(folder => Candidates(folder, texture, name)).ToList();
        if (_gameGfx is not null) candidates.Add(Path.Combine(_gameGfx, name));

        return candidates.FirstOrDefault(System.IO.File.Exists);
    }

    static string[] Candidates(string folder, string texture, string name) =>
    [
        Path.IsPathRooted(texture) ? texture : Path.Combine(folder, texture),
        Path.Combine(folder, "icons", name),
        Path.Combine(folder, "gui", "gfx", name),
        Path.Combine(folder, name),
    ];
}
