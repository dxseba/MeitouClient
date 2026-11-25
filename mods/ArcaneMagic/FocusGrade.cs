using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The Focus grade look (README "Facts"): the plugin gives a Focus the textures of the vanilla weapon model of its grade, by the item's level
/// (above 94 <c>Meitou</c>, above 79 <c>Edge Type 3</c>, above 59 <c>Mk III</c>, from 40 <c>Catun No.3</c>, above 19 <c>Old Refitted Blade</c>, else
/// <c>Rusted Junk</c>; MATERIAL_SPECS_WEAPON records by name). Here the crossbow part of a character's look takes that model as its weapon model,
/// which Meitou's character renderer already draws with (its <c>texture map</c> and <c>normal map</c>). Harmony: a postfix on
/// <c>Meitou.Data.Characters.CharacterAppearance.ChooseWeapons</c> setting the model of the Focus entry from the loadout's crossbow level; it
/// changes only the look's material, which the simulation never reads. A failure logs once and leaves the Focus's own textures.
/// </summary>
static class FocusGrade
{
    static readonly ConditionalWeakTable<GameDatabase, GameRecord?[]> Models = [];
    static readonly string[] Names = ["Rusted Junk", "Old Refitted Blade", "Catun No.3", "Mk III", "Edge Type 3", "Meitou"];
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        if (AccessTools.Method("Meitou.Data.Characters.CharacterAppearance:ChooseWeapons") is not { } choose)
        {
            context.Log("CharacterAppearance.ChooseWeapons not found: the Focus keeps its own textures");
            return;
        }

        context.Harmony.Patch(choose, postfix: new HarmonyMethod(typeof(FocusGrade), nameof(Chosen)));
    }

    /// <summary>The original's grade tier of an item level (0 to 5).</summary>
    public static int Tier(int level) => level switch
    {
        > 94 => 5,
        > 79 => 4,
        > 59 => 3,
        >= 40 => 2,
        > 19 => 1,
        _ => 0,
    };

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Chosen(GameDatabase db, Loadout? loadout, List<(GameRecord Weapon, string Point, GameRecord? Model)> __result)
    {
        if (s_failed != 0 || loadout?.Crossbow is not { Weapon.StringId: ArcaneRules.Focus } focus) return;

        try
        {
            var model = Models.GetValue(db, Find)[Tier(focus.Level)];
            for (int i = 0; i < __result.Count && model is not null; i++)
                if (__result[i].Weapon.StringId == ArcaneRules.Focus) __result[i] = __result[i] with { Model = model };
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"Focus grade look failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    static GameRecord?[] Find(GameDatabase db) =>
        [.. Names.Select(name => db.Records.Values.FirstOrDefault(r => r.Type == FcsRecordType.MaterialSpecsWeapon && r.BaseName == name))];
}
