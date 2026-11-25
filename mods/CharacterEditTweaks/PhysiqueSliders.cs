using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// The physique sliders on the BODY tab of a playable race: Body Bulk (-30 to 100, default 35) for every such race and Muscularity (0 to 100,
/// default 50) for one whose <c>nm male</c> and <c>nm male strong</c> maps are both set and differ, as the original adds them. Each has an
/// override row (Off/On, in place of the original's tick box); while it is on, the drawn body takes the slider's value / 100 as its bulk (bone
/// shape and the muscle term) or its muscle definition (the strong normal map's blend) in place of the stats' (the original's
/// <c>updatePhysiqueMuscleMults</c> hook). The values live on the look record (<c>ModLooks</c>), so CONFIRM, the save and EXPORT keep them. The
/// draw is a prefix on <c>CharacterRenderer.ApplyBuild</c>: display only, nothing in the world changes.
/// </summary>
static class PhysiqueSliders
{
    public const string BulkOn = "bulk on", Bulk = "bulk", MuscleOn = "muscle on", Muscle = "muscle";

    const int BulkMin = -30, BulkMax = 100, BulkMid = 35, MuscleMin = 0, MuscleMax = 100, MuscleMid = 50;

    static ModContext? s_context;
    static AccessTools.FieldRef<object, CharacterAppearance>? s_appearance;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        context.AddEditorRows(Rows);

        var renderer = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer");
        var asset = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterAsset");
        var apply = renderer is null ? null : AccessTools.Method(renderer, "ApplyBuild");
        if (apply is null || asset is null || AccessTools.Field(asset, "Appearance") is null)
        {
            context.LogError("CharacterRenderer.ApplyBuild not found: the physique sliders are saved but not drawn");
            return;
        }

        s_appearance = AccessTools.FieldRefAccess<CharacterAppearance>(asset, "Appearance");
        context.Harmony.Patch(apply, new HarmonyMethod(typeof(PhysiqueSliders), nameof(ApplyBefore)));
    }

    /// <summary>Whether the race's male normal maps give it the Muscularity slider (both set and different).</summary>
    static bool HasMuscularity(GameRecord race)
    {
        string normal = race.GetPath("nm male"), strong = race.GetPath("nm male strong");
        return normal.Length > 0 && strong.Length > 0 && !string.Equals(normal, strong, StringComparison.OrdinalIgnoreCase);
    }

    static IEnumerable<LookRow> Rows(LookRowContext context)
    {
        if (!context.Race.GetBool("playable")) yield break;

        string[] onOff = ["Off", "On"];
        yield return LookRow.Choice(EditorTab.Body, BulkOn, Tr("Body Bulk") + " (" + Tr("override") + ")", onOff);
        yield return LookRow.Slider(EditorTab.Body, Bulk, Tr("Body Bulk"), BulkMin, BulkMax, BulkMid);
        if (!HasMuscularity(context.Race)) yield break;

        yield return LookRow.Choice(EditorTab.Body, MuscleOn, Tr("Muscularity") + " (" + Tr("override") + ")", onOff);
        yield return LookRow.Slider(EditorTab.Body, Muscle, Tr("Muscularity"), MuscleMin, MuscleMax, MuscleMid);
    }

    static string Tr(string english) => s_context?.Tr(english) ?? english;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void ApplyBefore(object asset, ref BodyBuild build)
    {
        try
        {
            if (s_appearance is null || s_context is null) return;

            var look = s_appearance(asset);
            if (look is { Animal: null, Loadout.Appearance.Record: { } record }) build = Overridden(look, record, build);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"the physique sliders failed, bodies follow the stats: {e}");
        }
    }
#pragma warning restore IDE1006

    /// <summary>The build with the look's overrides that are on: bulk for bones and muscle, definition for a race with a strong map.</summary>
    static BodyBuild Overridden(CharacterAppearance look, FcsRecord record, BodyBuild build)
    {
        string id = s_context!.Id;
        if (ModLooks.Int(record, id, BulkOn, 0) == 1)
        {
            float bulk = Math.Clamp(ModLooks.Int(record, id, Bulk, BulkMid), BulkMin, BulkMax) * 0.01f;
            build = build with { Muscle = bulk, ShapeMuscle = bulk };
        }

        bool strong = look.BodyNormalStrong is { Length: > 0 } map && map != look.BodyNormal;
        if (strong && ModLooks.Int(record, id, MuscleOn, 0) == 1)
        {
            build = build with { Definition = Math.Clamp(ModLooks.Int(record, id, Muscle, MuscleMid), MuscleMin, MuscleMax) * 0.01f };
        }

        return build;
    }
}
