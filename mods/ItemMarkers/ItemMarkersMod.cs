using Meitou.Modding;

namespace Meitou.ItemMarkers;

/// <summary>
/// Port of Item Markers (QoL) (Workshop 3816608451): marks on item icons in every item grid (inventory, bags, limbs, loot, trader, containers,
/// equipment): the grade colour of armour, weapons, crossbows and limbs in four styles, the steal badge on stolen goods, the hand badge on faction
/// uniforms, an eye on learned blueprints and fully known location maps, a bag on items bought or sold in the open trade, a colour of its own for
/// Meitou-class weapons (also in their tooltip), and custom grades from profiles and other mods (<see cref="ItemMarkersApi"/>). Its pictures and
/// its catalogues (<c>item_markers</c>: en_GB, ru_RU, zh_CN) are read from the installed original.
/// </summary>
public sealed class ItemMarkersMod : IMod
{
    static readonly MarkerSettings Settings = new();

    public void Initialize(ModContext context)
    {
        string? original = OriginalFolder.Find(context);
        if (original is not null) context.AddTranslations("item_markers", original);

        ItemMarkersApi.Log = context.Log;
        var rules = new GradeRules(context.Log);
        string[] folders = [Path.Combine(context.Folder, "grades"), .. original is null ? Array.Empty<string>() : [Path.Combine(original, "grades")]];
        rules.Reload(folders);
        Settings.ReloadRules = () => rules.Reload(folders);
        context.AddSettings(Settings);

        // The original's "Show key" (HoldKey, NONE by default): the marks show while it is held; unbound, they always show.
        var show = context.AddKeyAction("show_marks", "Show key", _ => { });
        context.AddFrame(frame =>
        {
            CellPatch.Snapshot = frame.Snapshot;
            CellPatch.UiScale = frame.UiScale;
        });

        if (CellFacts.Create(context.Data, context.Population, context.Log) is not { } facts) return;

        var grades = new Grades(context.Data, rules);
        var pictures = new MarkPictures(original);
        Patch(context, "marks", () => CellPatch.Install(context, Settings, facts, grades, pictures, () => show.Keys.Count == 0 || show.Held));
        Patch(context, "uniform badge", () => UniformBook.Install(context));
        Patch(context, "Meitou tooltip colour", () => TooltipPatch.Install(context));
        context.Log("Item Markers initialized");
    }

    /// <summary>Installs one group of patches; a patch Harmony refuses leaves that part off (logged) instead of refusing the mod.</summary>
    static void Patch(ModContext context, string part, Action install)
    {
        try
        {
            install();
        }
        catch (Exception e) when (e is HarmonyLib.HarmonyException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            context.Log($"{part}: patch refused ({e.Message}); left off");
        }
    }
}
