namespace Meitou.Brutality.Ui;

// The linked overlay reads this default in both builds. The stock binding sets it before registering the overlay.
internal static class BrutalityUiAdapter
{
    internal static BrutalityUiPreferences Preferences { get; set; } = new();
}
