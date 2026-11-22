using System.Reflection;
using HarmonyLib;
using Meitou.Game.Rendering.Ui;

namespace Meitou.VitalRead;

/// <summary>
/// The state icons: the original's default texture and crop per state (<c>mod-config.defaults.json</c>), found as the original looks for them, and
/// loaded through the interface's painter. Its own PNGs are not shipped with the port: a state whose file is not found has no icon.
/// </summary>
sealed class BadgeIcons(string folder)
{
    /// <summary>
    /// One state's icon: the configured texture and its crop in texture pixels (null: the whole image).
    /// </summary>
    readonly record struct Source(string Texture, string? Crop);

    static readonly Dictionary<VitalState, Source> Defaults = new()
    {
        [VitalState.Unconscious] = new("Kenshi_UI.png", "45 122 32 32"),
        [VitalState.RecoveryComa] = new("gui/gfx/heart_64px_opt.png", null),
        [VitalState.Dying] = new("gui/gfx/death_64px_opt.png", null),
        [VitalState.PlayingDead] = new("Kenshi_UI.png", "80 126 38 27"),
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

    static FieldInfo? s_painter, s_resolve;

    public static void Bind(Type playerInterface)
    {
        s_painter = AccessTools.Field(playerInterface, "UiPainter");
        s_resolve = AccessTools.Field(playerInterface, "UiResolve");
    }

    /// <summary>
    /// The image properties of <paramref name="state"/>'s icon once its texture is on the GPU; null while it loads, or when it has none.
    /// </summary>
    public Dictionary<string, string>? Image(object player, VitalState state)
    {
        if (!Defaults.TryGetValue(state, out var source) || s_painter?.GetValue(player) is not UiPainter painter) return null;
        if (File(player, state, source) is not { } file || !painter.IconReady(file)) return null;

        var image = new Dictionary<string, string> { ["ImageTexture"] = UiPainter.IconTexture(file) };
        if (source.Crop is { } crop) image["ImageRect"] = crop;
        return image;
    }

    string? File(object player, VitalState state, Source source)
    {
        if (_files.TryGetValue(state, out var known)) return known;

        var resolve = s_resolve?.GetValue(player) as Func<string, string?>;
        return _files[state] = Find(source.Texture, resolve);
    }

    /// <summary>
    /// The original's lookup: the path as given (absolute, or under the mod's folder), then the bare name in the mod's <c>icons/</c>,
    /// <c>gui/gfx/</c> and folder, then the game's resources (Kenshi registers each mod's <c>gui/gfx</c>).
    /// </summary>
    string? Find(string texture, Func<string, string?>? resolve)
    {
        string name = Path.GetFileName(texture);
        string[] candidates =
        [
            Path.IsPathRooted(texture) ? texture : Path.Combine(folder, texture),
            Path.Combine(folder, "icons", name),
            Path.Combine(folder, "gui", "gfx", name),
            Path.Combine(folder, name),
        ];
        foreach (var candidate in candidates)
            if (System.IO.File.Exists(candidate)) return candidate;

        return resolve?.Invoke(name);
    }
}
