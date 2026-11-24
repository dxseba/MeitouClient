using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Bodies;

namespace Meitou.BowMod;

/// <summary>
/// The original's save field: it keeps a character's Bows level as the STATS float <c>bowmod skill</c> (finite, 0 to 100, else 1 when read).
/// Meitou keeps it as <c>meitou skill bowmod.archery</c>. Harmony postfixes on <c>Meitou.Simulation.Bodies.CharacterStats.WriteSave</c> (also write
/// <c>bowmod skill</c>) and <c>CharacterStats.ReadSave</c> (a save with only <c>bowmod skill</c>, one the original wrote, gets that level) carry
/// it both ways. A failure logs once and leaves the save as Meitou writes and reads it.
/// </summary>
static class BowSave
{
    const string Field = "bowmod skill";

    static ModSkill? s_skill;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context, ModSkill skill)
    {
        s_context = context;
        s_skill = skill;
        var write = AccessTools.Method(typeof(CharacterStats), nameof(CharacterStats.WriteSave));
        var read = AccessTools.Method(typeof(CharacterStats), nameof(CharacterStats.ReadSave));
        if (write is null || read is null)
        {
            context.LogError("CharacterStats.WriteSave or ReadSave not found: Bows levels are saved as Meitou's only");
            return;
        }

        context.Harmony.Patch(write, postfix: new HarmonyMethod(typeof(BowSave), nameof(Written)));
        context.Harmony.Patch(read, postfix: new HarmonyMethod(typeof(BowSave), nameof(Read)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Written(CharacterStats __instance, IDictionary<string, float> floats)
    {
        try
        {
            if (s_skill is { } skill && floats.ContainsKey(ModSkillLevels.SaveKeyPrefix + skill.Id)) floats[Field] = skill.Level(__instance);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void Read(IReadOnlyDictionary<string, float> floats, CharacterStats __result)
    {
        try
        {
            if (s_skill is not { } skill || floats.ContainsKey(ModSkillLevels.SaveKeyPrefix + skill.Id) || !floats.TryGetValue(Field, out float v))
                return;

            skill.Set(__result, float.IsFinite(v) && v is >= 0 and <= 100 ? v : 1);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"bow save field failed ({e.GetType().Name}: {e.Message})");
    }
}
