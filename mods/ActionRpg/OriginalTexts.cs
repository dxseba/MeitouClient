using System.Text;
using Meitou.Modding;

namespace Meitou.ActionRpg;

/// <summary>
/// The fork's settings page translations (<c>Settings.&lt;locale&gt;.tsv</c> in the installed original: its English text, a tab, the
/// translation; nine languages), read at runtime and given to the port's own labels where they say the same (<see cref="Labels"/>).
/// </summary>
static class OriginalTexts
{
    /// <summary>
    /// The port's label and the fork's English text for the same row.
    /// </summary>
    static readonly (string Ours, string Theirs)[] Labels =
    [
        ("Toggle V mode (Action RPG)", "Direct Control on / off"),
        ("Lock attack direction", "Lock attack direction"),
        ("Show direction arrows", "Show direction arrows"),
        ("Squad follows you (Action RPG)", "Companions follow me"),
        ("Squad moves to the crosshair (Action RPG)", "Companions move to crosshair"),
        ("WASD speed multiplier", "Movement speed multiplier"),
        ("Cap WASD speed", "Cap speed at the character's real max"),
        ("Camera faces character in inventory", "Camera faces character in inventory"),
        ("Character still blocks on their own", "Automatic block (AI chooses direction)"),
        ("Attack window (ms)", "Attack press kept for (ms)"),
        ("Attack recovery (ms)", "Pause after a swing (ms)"),
        ("Double-press guard (ms)", "Ignore double-presses within (ms)"),
        ("Crosshair target cone (degrees)", "Crosshair target cone (degrees)"),
        ("Target name and health bars", "Show NPC name and health"),
        ("Third-person mouse sensitivity", "Third-person mouse sensitivity"),
        ("Camera distance", "Distance behind your character"),
        ("Camera height", "Camera focus height"),
        ("Camera side", "Shoulder offset"),
        ("Mouse sensitivity", "Mouse look sensitivity"),
        ("Field of view", "Field of view (degrees)"),
        ("Hide your own head", "Hide your own head"),
        ("Show floors below you in buildings", "Show floors below you in buildings"),
        ("Body is solid from the inside", "Body is solid from the inside"),
        ("Eye height", "Eye drop below head bone"),
        ("Eye forward", "Eye forward of the head"),
        ("Clipping distance", "Clipping distance"),
        ("Movement", "MOVEMENT"),
        ("Third person", "THIRD PERSON CAMERA (V)"),
        ("First person", "FIRST PERSON"),
        ("Manual combat", "MANUAL COMBAT - ATTACK"),
    ];

    public static void Add(ModContext context)
    {
        if (OriginalFolder.Find(context) is not { } folder || File(folder, context.Language) is not { } path) return;

        try
        {
            var theirs = Read(path);
            var pairs = Labels.Where(l => theirs.ContainsKey(l.Theirs)).Select(l => KeyValuePair.Create(l.Ours, theirs[l.Theirs])).ToList();
            context.AddTranslations(context.Language, pairs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"the original's settings texts were not read: {e.Message}");
        }
    }

    /// <summary>
    /// The table of the game's language: <c>zh_CN</c> reads <c>Settings.zh-CN.tsv</c>, else the first of the same language part; English none.
    /// </summary>
    static string? File(string folder, string language)
    {
        string code = language.Replace('_', '-');
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return null;

        string exact = Path.Combine(folder, $"Settings.{code}.tsv");
        if (System.IO.File.Exists(exact)) return exact;

        string part = code.Split('-')[0];
        return Directory.GetFiles(folder, $"Settings.{part}-*.tsv").Order(StringComparer.Ordinal).FirstOrDefault();
    }

    static Dictionary<string, string> Read(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in System.IO.File.ReadLines(path, Encoding.UTF8))
        {
            if (line.StartsWith('#')) continue;

            var cells = line.Split('\t');
            if (cells.Length >= 2 && cells[0].Length > 0 && cells[1].Length > 0) map.TryAdd(cells[0], cells[1]);
        }

        return map;
    }
}
