using Meitou.Content;
using Meitou.Modding;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The installed original's folder (Workshop 3742288543, the port's required item): its Workshop folder beside this one, else the installed mod
/// named "AdvancedMedicAI" without code. The port reads its DLL's texts and its <c>AdvancedMedicAI.ini</c> from there; nothing of it ships.
/// </summary>
static class OriginalFolder
{
    public const string Item = "3742288543";
    const string Name = "AdvancedMedicAI";

    public static string? Find(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && Directory.Exists(Path.Combine(parent, Item)))
            return Path.Combine(parent, Item);

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
            return ModList.Discover(install).FirstOrDefault(m => m.Name == Name && !m.HasCode
                && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase))?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The original's <c>AdvancedMedicAI.ini</c> tuning; the DLL's defaults without the original or its ini (one log line).
    /// </summary>
    public static MedicTuning Tuning(ModContext context)
    {
        if (Find(context) is not { } folder || !File.Exists(Path.Combine(folder, "AdvancedMedicAI.ini")))
        {
            context.Log("the original's AdvancedMedicAI.ini is not installed: tuning stays at the DLL's defaults");
            return new MedicTuning();
        }

        try
        {
            var tuning = MedicTuning.Parse(File.ReadLines(Path.Combine(folder, "AdvancedMedicAI.ini")));
            context.Log($"original tuning read from {Path.Combine(folder, "AdvancedMedicAI.ini")}");
            return tuning;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"AdvancedMedicAI.ini not read ({e.Message}): tuning stays at the DLL's defaults");
            return new MedicTuning();
        }
    }
}
