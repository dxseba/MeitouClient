using System.Globalization;
using Meitou.Modding;

namespace Meitou.JobTabs;

/// <summary>
/// The original's <c>JobTabs.cfg</c> (beside its DLL), read once at the start as the original does: <c>key=value</c> lines, <c>#</c> starts a
/// comment; <c>tabCount</c> a whole number 2..9, <c>activeStyle</c> <c>frame</c> or <c>dot</c>. An unknown key or a bad value is logged and keeps
/// the default. Its values become the Settings page's defaults (a value the player set there wins).
/// </summary>
static class OriginalConfig
{
    const string FileName = "JobTabs.cfg";

    public static void Apply(ModContext context, JobTabsSettings settings)
    {
        if (new OriginalFiles(context).Find(FileName) is not { } path)
        {
            context.Log("No JobTabs.cfg, using defaults");
            return;
        }

        try
        {
            foreach (string line in File.ReadLines(path)) Read(context, settings, line);

            context.Log($"Loaded {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"{FileName} not read: {e.Message}");
        }
    }

    static void Read(ModContext context, JobTabsSettings settings, string line)
    {
        int hash = line.IndexOf('#');
        string text = (hash < 0 ? line : line[..hash]).Trim();
        if (text.Length == 0) return;

        int equals = text.IndexOf('=');
        if (equals < 0)
        {
            context.Log($"{FileName}: '{text}' is not key=value, ignored");
            return;
        }

        string key = text[..equals].Trim();
        string value = text[(equals + 1)..].Trim();

        if (key == "tabCount") TabCount(context, settings, value);
        else if (key == "activeStyle") ActiveStyle(context, value);
        else context.Log($"{FileName}: unknown key '{key}', ignored");
    }

    static void TabCount(ModContext context, JobTabsSettings settings, string value)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int count))
        {
            context.Log($"{FileName}: tabCount={value} is not a whole number, using {settings.Count}");
            return;
        }

        if (count is < 2 or > JobTabsSettings.Most)
        {
            context.Log($"{FileName}: tabCount={value} out of range [2..{JobTabsSettings.Most}], using {settings.Count}");
            return;
        }

        settings.Count = count;
    }

    /// <summary>
    /// The style is read for its warnings only: the port has one look, the job panel button's lit state (no per-button skin in the engine).
    /// </summary>
    static void ActiveStyle(ModContext context, string value)
    {
        if (value == "dot") context.Log($"{FileName}: activeStyle=dot is drawn as the lit button (the port has no dot style)");
        else if (value != "frame") context.Log($"{FileName}: activeStyle={value} is not frame or dot, using frame");
    }
}
