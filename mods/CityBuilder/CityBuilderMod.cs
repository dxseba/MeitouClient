using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CityBuilder;

/// <summary>
/// Port of "Kenshi: CITY BUILDER" (Workshop 3833745044, City.dll, a required item for its City.mod and files): the CITY build category's houses,
/// trade houses, guard posts and taverns. Buyers come some hours after a building is finished and offer to buy it; sold, settlers live in it or a
/// trader keeps a shop there and pays a daily tax; guards are hired for the posts; taverns get an innkeeper and pay a share of each night. Its
/// settings are the original's City.ini, under Mod settings.
/// </summary>
public sealed class CityBuilderMod : IMod
{
    internal static readonly CitySettings Settings = new();

    /// <summary>The running world's system, for the window; null before a world starts.</summary>
    internal static volatile CitySystem? Running;

    public void Initialize(ModContext context)
    {
        string? original = OriginalFolder.Find(context);
        ReadIni(context, original);
        CityTexts.Add(context, original);
        context.AddSettings(Settings);

        var catalog = new CityCatalog(context.Data);
        var layouts = CityLayouts.Read(original is null ? null : Path.Combine(original, "layouts.txt"));
        context.Log($"City: {catalog.Buildings.Count} CITY buildings, {layouts.Count} layouts");
        if (catalog.Buildings.Count == 0) context.Log("City: no CITY buildings in the data: enable the original City mod (Workshop 3833745044)");

        context.AddSystem("city", _ => Running = new CitySystem(context.Id, context.Data, catalog, layouts, Settings, context.Enqueue,
            text => context.Tr(text), context.RngPurpose, context.Population), SystemAnchor.End);

        var window = new CityWindow(context, Settings, new CityNames(catalog, context.Tr));
        context.AddUi(window);
        context.AddFrame(window.Frame);
        context.AddKeyAction("settlement", "Settlement window", _ => window.Toggle(),
            Binding.Of(Key.H).With(KeyModifiers.Ctrl | KeyModifiers.Shift));
        context.Log("City initialized");
    }

    static void ReadIni(ModContext context, string? original)
    {
        string? ini = original is null ? null : Path.Combine(original, "City.ini");
        if (ini is null || !File.Exists(ini)) return;

        try
        {
            Settings.ReadIni(ini);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"City.ini not read ({e.Message}): shipped defaults");
        }
    }
}
