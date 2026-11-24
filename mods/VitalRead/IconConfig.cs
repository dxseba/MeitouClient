using System.Text.Json;
using Meitou.Data.Ui;

namespace Meitou.VitalRead;

/// <summary>
/// One state's icon as the original configures it: the texture, its size (<c>IconSizePx</c>, 0: the picture's own) and its crop in texture
/// pixels (<c>IconCoordLeft</c> / <c>Top</c> / <c>Width</c> / <c>Height</c>; null: none).
/// </summary>
readonly record struct IconSource(string Texture, int Size, UiRect? Crop);

/// <summary>
/// The original's per-state icon keys (<c>&lt;state&gt;IconTexture</c>, <c>IconSizePx</c>, <c>IconCoord*</c>) over its defaults: read once from
/// a <c>mod-config.json</c> in the first folder that has one (this mod's, then the installed original's, which its Mod Hub writes). The other
/// keys of that file are this port's settings and are not read here.
/// </summary>
static class IconConfig
{
    /// <summary>
    /// The original's defaults (<c>mod-config.defaults.json</c>): the key prefix per state, texture, size and crop.
    /// </summary>
    static readonly Dictionary<VitalState, (string Key, IconSource Source)> Defaults = new()
    {
        [VitalState.Unconscious] = ("unconscious", new("Kenshi_UI.png", 64, new UiRect(45, 122, 32, 32))),
        [VitalState.RecoveryComa] = ("recoveryComa", new("gui/gfx/heart_64px_opt.png", 64, null)),
        [VitalState.Dying] = ("dying", new("gui/gfx/death_64px_opt.png", 64, null)),
        [VitalState.PlayingDead] = ("playingDead", new("Kenshi_UI.png", 64, new UiRect(80, 126, 38, 27))),
        [VitalState.Starving] = ("starving", new("gui/gfx/starving_64px_opt.png", 64, null)),
        [VitalState.CrippledArm] = ("crippledArm", new("gui/gfx/broken-arm_64px_opt.png", 64, null)),
        [VitalState.CrippledLeg] = ("crippledLeg", new("gui/gfx/broken-leg_64px_opt.png", 64, null)),
        [VitalState.Repairing] = ("repairing", new("gui/gfx/repair-wrench_64.png", 64, null)),
        [VitalState.InBed] = ("inBed", new("gui/gfx/bed-plus_64.png", 64, null)),
    };

    /// <summary>
    /// Each state's icon: the defaults with the first <c>mod-config.json</c>'s keys over them.
    /// </summary>
    public static Dictionary<VitalState, IconSource> Load(IReadOnlyList<string> folders, Action<string> log)
    {
        var sources = Defaults.ToDictionary(p => p.Key, p => p.Value.Source);
        if (Read(folders, log) is not { } config) return sources;

        using (config)
        {
            foreach (var (state, (key, source)) in Defaults) sources[state] = Over(config.RootElement, key, source);
        }

        return sources;
    }

    static JsonDocument? Read(IReadOnlyList<string> folders, Action<string> log)
    {
        foreach (string folder in folders)
        {
            string path = Path.Combine(folder, "mod-config.json");
            if (!File.Exists(path)) continue;

            try
            {
                var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true });
                if (document.RootElement.ValueKind == JsonValueKind.Object) return document;

                document.Dispose();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                log($"{path} not read: {e.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="source"/> with the keys of <paramref name="key"/> that <paramref name="root"/> sets; a crop needs all four coordinates and
    /// a width and height above 0.
    /// </summary>
    static IconSource Over(JsonElement root, string key, IconSource source)
    {
        string texture = Text(root, key + "IconTexture") ?? source.Texture;
        int size = Number(root, key + "IconSizePx") ?? source.Size;
        var crop = source.Crop;
        if (Number(root, key + "IconCoordLeft") is { } x && Number(root, key + "IconCoordTop") is { } y
            && Number(root, key + "IconCoordWidth") is { } w && Number(root, key + "IconCoordHeight") is { } h)
        {
            crop = w > 0 && h > 0 ? new UiRect(x, y, w, h) : null;
        }

        return new IconSource(texture, size, crop);
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : null;
}
