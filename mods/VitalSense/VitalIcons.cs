using Meitou.Content;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.VitalSense;

/// <summary>
/// The state icons (<c>*_icon_texture</c> of <c>mod-config.defaults.json</c>): the original's own picture where one is found, else the
/// <c>Kenshi_UI.png</c> atlas cell the DLL falls back to. Host thread only.
/// </summary>
sealed class VitalIcons(string folder)
{
    /// <summary>
    /// The original's default pictures: recovery coma, dying and dead (unconscious and playing dead default to the atlas).
    /// </summary>
    static readonly Dictionary<VitalState, string> Pictures = new()
    {
        [VitalState.RecoveryComa] = "gui/gfx/heart_64px.png",
        [VitalState.Dying] = "gui/gfx/death_64px.png",
        [VitalState.Dead] = "gui/gfx/grave_64px.png",
    };

    readonly Dictionary<VitalState, string?> _files = [];

    readonly string? _gameGfx = GameInstall.Locate() is { } install ? Path.Combine(install.DataDirectory, "gui", "gfx") : null;

    string? _atlas;
    bool _atlasLooked;

    /// <summary>
    /// <paramref name="state"/>'s icon file and crop; null without one (no install for the atlas).
    /// </summary>
    public (string File, UiImageCrop? Crop)? Of(VitalState state)
    {
        if (!_files.TryGetValue(state, out var picture)) _files[state] = picture = Pictures.TryGetValue(state, out var p) ? Find(p) : null;
        if (picture is not null) return (picture, null);

        if (!_atlasLooked) (_atlas, _atlasLooked) = (Find("Kenshi_UI.png"), true);
        return _atlas is { } atlas ? (atlas, AtlasCell(state)) : null;
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
    /// The path under the mod's folder, the bare name in its <c>icons/</c>, <c>gui/gfx/</c> and folder, then the game's <c>data/gui/gfx</c>.
    /// </summary>
    string? Find(string texture)
    {
        string name = Path.GetFileName(texture);
        string?[] candidates =
        [
            Path.Combine(folder, texture),
            Path.Combine(folder, "icons", name),
            Path.Combine(folder, "gui", "gfx", name),
            Path.Combine(folder, name),
            _gameGfx is null ? null : Path.Combine(_gameGfx, name),
        ];
        return candidates.FirstOrDefault(c => c is not null && File.Exists(c));
    }
}
