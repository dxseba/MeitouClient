using Meitou.Content;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.VitalSense;

/// <summary>
/// The state icons (<c>*_icon_texture</c>, <c>*_icon_size_px</c>, from the settings): the configured picture where one is found, its top left
/// <c>size</c> square shown (the DLL's image coordinates 0, 0, size, size), else the <c>Kenshi_UI.png</c> atlas cell the DLL falls back to; and
/// the bounty glow picture. Host thread only.
/// </summary>
sealed class VitalIcons(IReadOnlyList<string> folders, Action<string> log)
{
    /// <summary>
    /// The DLL's glow picture (<c>show_bounty_glow</c>).
    /// </summary>
    const string GlowPicture = "gui/gfx/bounty_glow_64px.png";

    /// <summary>
    /// Each texture setting once looked for (null: not found).
    /// </summary>
    readonly Dictionary<string, string?> _files = new(StringComparer.OrdinalIgnoreCase);

    readonly string? _gameGfx = GameInstall.Locate() is { } install ? Path.Combine(install.DataDirectory, "gui", "gfx") : null;

    /// <summary>
    /// <paramref name="state"/>'s icon file and crop; null without one (no install for the atlas).
    /// </summary>
    public (string File, UiImageCrop? Crop)? Of(VitalState state)
    {
        var (texture, size) = VitalSenseMod.Settings.Icon(state);
        if (texture.Length > 0 && Lookup(texture) is { } picture) return (picture, UiImageCrop.Pixels(0, 0, size, size));

        return Lookup("Kenshi_UI.png") is { } atlas ? (atlas, AtlasCell(state)) : null;
    }

    /// <summary>
    /// The glow picture, its 64 px square (the DLL's coordinates); null when it is not found.
    /// </summary>
    public (string File, UiImageCrop? Crop)? Glow() => Lookup(GlowPicture) is { } file ? (file, UiImageCrop.Pixels(0, 0, 64, 64)) : null;

    string? Lookup(string texture)
    {
        if (_files.TryGetValue(texture, out var known)) return known;

        string? file = Find(texture);
        if (VitalSenseMod.Settings.DebugTextures) log($"texture '{texture}': {file ?? "not found"}");
        return _files[texture] = file;
    }

    /// <summary>
    /// The DLL's fallback cells (<b>Verified</b>, original DLL (decompiled)): dying and dead the 34 px cell at 108, 49; playing dead the 38 by 27
    /// stealth tag at 80, 126; unconscious and recovery coma the Knockout pointer (<c>pic_PointerKnockout</c>, 32 px at 45, 122).
    /// </summary>
    static UiImageCrop AtlasCell(VitalState state) => state switch
    {
        VitalState.Dying or VitalState.Dead => UiImageCrop.Pixels(108, 49, 34, 34),
        VitalState.PlayingDead => UiImageCrop.Pixels(80, 126, 38, 27),
        _ => UiImageCrop.Pixels(45, 122, 32, 32),
    };

    /// <summary>
    /// In each folder (this mod's, then the installed original's): the path under it, the bare name in its <c>icons/</c>, <c>gui/gfx/</c> and
    /// itself; then the game's <c>data/gui/gfx</c>.
    /// </summary>
    string? Find(string texture)
    {
        if (texture.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || Path.GetFileName(texture) is not { Length: > 0 } name) return null;

        var candidates = folders.SelectMany(folder => Candidates(folder, texture, name)).ToList();
        if (_gameGfx is not null) candidates.Add(Path.Combine(_gameGfx, name));

        return candidates.FirstOrDefault(File.Exists);
    }

    static string[] Candidates(string folder, string texture, string name) =>
        [Path.Combine(folder, texture), Path.Combine(folder, "icons", name), Path.Combine(folder, "gui", "gfx", name), Path.Combine(folder, name)];
}
