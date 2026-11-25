using System.Text.Json;
using HarmonyLib;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// Port of Character Edit Tweaks: undo and redo of the character editor's slider and list changes (a changes window, Ctrl+Z, Ctrl+Shift+Z), Cancel
/// in play, a wider camera zoom and a camera height bar, and the Body Bulk and Muscularity physique sliders. Texts come from the installed
/// original's <c>Translations.json</c> (Workshop 3807964959, the port's required item).
/// </summary>
public sealed class CharacterEditTweaksMod : IMod
{
    public void Initialize(ModContext context)
    {
        AddTranslations(context);
        Safely(context, "the editor panel", InstallEditor);
        Safely(context, "undo and redo", DraftPatches.Install);
        Safely(context, "the editor camera", CameraPatches.Install);
        Safely(context, "the physique sliders", PhysiqueSliders.Install);
        context.AddUi(new EditorUi(context.Tr));
        context.Log("Character Edit Tweaks initialized");
    }

    /// <summary>Installs one feature's patches; a game that no longer has a member leaves that feature off, logged, and the rest on.</summary>
    static void Safely(ModContext context, string feature, Action<ModContext> install)
    {
        try
        {
            install(context);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or MissingMemberException
            or HarmonyException)
        {
            context.LogError($"{feature} not installed, left as without the mod: {e.Message}");
        }
    }

    /// <summary>A postfix on the editor panel's binding keeps the panel at hand for the session reads (<see cref="EditorAccess"/>).</summary>
    static void InstallEditor(ModContext context)
    {
        if (EditorAccess.BindEditor is not { } bind || !EditorAccess.Ready)
        {
            context.LogError("the character editor's panel was not found: undo, redo, Cancel and the camera bar are off");
            return;
        }

        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(CharacterEditTweaksMod), nameof(Bound)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Bound(object __instance) => EditorAccess.Capture(__instance);
#pragma warning restore IDE1006

    /// <summary>The seven English texts in <c>Translations.json</c>'s order (each language's array lists them so).</summary>
    static readonly string[] Texts = ["Body Bulk", "Muscularity", "Undo", "Redo", "Changes", "Camera", "Cancel"];

    /// <summary>The game language's array of the installed original's <c>Translations.json</c>, as the mod's translations.</summary>
    static void AddTranslations(ModContext context)
    {
        if (OriginalFolder.Find(context) is not { } folder)
        {
            context.Log("the original (Workshop 3807964959) is not installed: texts stay English");
            return;
        }

        string path = Path.Combine(folder, "Translations.json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty(context.Language, out var list) || list.ValueKind != JsonValueKind.Array) return;

            var messages = new Dictionary<string, string>();
            int i = 0;
            foreach (var text in list.EnumerateArray())
            {
                if (i < Texts.Length && text.GetString() is { Length: > 0 } shown) messages[Texts[i]] = shown;
                i++;
            }

            context.AddTranslations(context.Language, messages);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            context.Log($"{path} not read ({e.Message}): texts stay English");
        }
    }
}

/// <summary>The installed original's folder: its Workshop folder beside this mod's, else the code-less mod named "Character Edit Tweaks".</summary>
static class OriginalFolder
{
    const string Item = "3807964959", Name = "Character Edit Tweaks";

    public static string? Find(ModContext context)
    {
        string own = Path.GetFullPath(context.Folder);
        if (Path.GetDirectoryName(own) is { } parent && Directory.Exists(Path.Combine(parent, Item))) return Path.Combine(parent, Item);

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            bool IsOriginal(ModInfo m) =>
                m.Name == Name && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
