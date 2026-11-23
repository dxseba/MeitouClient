using Meitou.Content;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.VitalRead;

/// <summary>
/// The state icons: the original's default texture and crop per state (<c>mod-config.defaults.json</c>), found as the original looks for them and
/// drawn as image nodes. Its own PNGs are not shipped with the port: a state whose file is not found has no icon.
/// </summary>
sealed class BadgeIcons(string folder)
{
    /// <summary>
    /// One state's icon: the configured texture and its crop in texture pixels (null: the whole image).
    /// </summary>
    readonly record struct Source(string Texture, UiRect? Crop);

    static readonly Dictionary<VitalState, Source> Defaults = new()
    {
        [VitalState.Unconscious] = new("Kenshi_UI.png", new UiRect(45, 122, 32, 32)),
        [VitalState.RecoveryComa] = new("gui/gfx/heart_64px_opt.png", null),
        [VitalState.Dying] = new("gui/gfx/death_64px_opt.png", null),
        [VitalState.PlayingDead] = new("Kenshi_UI.png", new UiRect(80, 126, 38, 27)),
        [VitalState.Starving] = new("gui/gfx/starving_64px_opt.png", null),
        [VitalState.CrippledArm] = new("gui/gfx/broken-arm_64px_opt.png", null),
        [VitalState.CrippledLeg] = new("gui/gfx/broken-leg_64px_opt.png", null),
        [VitalState.Repairing] = new("gui/gfx/repair-wrench_64.png", null),
        [VitalState.InBed] = new("gui/gfx/bed-plus_64.png", null),
    };

    /// <summary>
    /// Each state's file once looked for (null: not found).
    /// </summary>
    readonly Dictionary<VitalState, string?> _files = [];

    /// <summary>
    /// The game's interface pictures (<c>data/gui/gfx</c>), found once; null without an install.
    /// </summary>
    readonly string? _gameGfx = GameInstall.Locate() is { } install ? Path.Combine(install.DataDirectory, "gui", "gfx") : null;

    /// <summary>
    /// <paramref name="state"/>'s icon as an image node's file and crop; null when it has none.
    /// </summary>
    public (string File, UiImageCrop? Crop)? Image(VitalState state)
    {
        if (!Defaults.TryGetValue(state, out var source) || File(state, source) is not { } file) return null;

        return (file, source.Crop is { } c ? UiImageCrop.Pixels(c.X, c.Y, c.Width, c.Height) : null);
    }

    string? File(VitalState state, Source source)
    {
        if (_files.TryGetValue(state, out var known)) return known;

        return _files[state] = Find(source.Texture);
    }

    /// <summary>
    /// The original's lookup: the path as given (absolute, or under the mod's folder), then the bare name in the mod's <c>icons/</c>,
    /// <c>gui/gfx/</c> and folder, then the game's <c>data/gui/gfx</c>.
    /// </summary>
    string? Find(string texture)
    {
        string name = Path.GetFileName(texture);
        string?[] candidates =
        [
            Path.IsPathRooted(texture) ? texture : Path.Combine(folder, texture),
            Path.Combine(folder, "icons", name),
            Path.Combine(folder, "gui", "gfx", name),
            Path.Combine(folder, name),
            _gameGfx is null ? null : Path.Combine(_gameGfx, name),
        ];
        return candidates.FirstOrDefault(c => c is not null && System.IO.File.Exists(c));
    }
}
