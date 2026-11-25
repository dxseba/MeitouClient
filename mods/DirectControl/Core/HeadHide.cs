using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// First person hides the driven character's own head, hair, beard and headgear (the original's per-pixel head hide): its drawn look is swapped
/// for a copy marked as Meitou decapitation's headless look, whose head vertices and head parts the character renderer leaves out (docs:
/// <c>CharacterAppearance.Headless</c>). The copy is drawn once the renderer has prepared it (asked for on the first frame), and Meitou
/// decapitation is kept from taking it for a cut-off head (no falling head). Display only.
/// </summary>
static class HeadHide
{
    static readonly ConditionalWeakTable<CharacterAppearance, CharacterAppearance> Copies = new();

    static readonly Func<object, object>? Clone =
        AccessTools.Method(typeof(object), "MemberwiseClone") is { } m ? o => m.Invoke(o, null)! : null;

    static readonly Action<CharacterAppearance, bool>? SetHeadless =
        AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.Headless)) is { } setter
            ? AccessTools.MethodDelegate<Action<CharacterAppearance, bool>>(setter)
            : null;

    public static bool Ready => Clone is not null && SetHeadless is not null;

    static int s_faulted;

    /// <summary>
    /// Postfix of <c>GameHost.Drawn</c>: the hidden head's character is drawn with its headless copy once prepared.
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter names
    public static void DrawnPostfix(CharacterSnapshot c, ref CharacterInstance __result)
    {
        try
        {
            if (Pilot.Current is not { } pilot || c.Id != pilot.HiddenHead || __result.Appearance is not { Headless: false } look) return;

            var bare = Copies.GetValue(look, Headless);
            if (HostAccess.Prepared(bare)) __result = __result with { Appearance = bare };
            else HostAccess.Prefetch(bare);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

#pragma warning restore IDE1006

    /// <summary>
    /// Prefix of <c>Decapitation.CutHead</c>: not for the hidden head.
    /// </summary>
    public static bool CutHeadPrefix(CharacterSnapshot c)
    {
        try
        {
            return Pilot.Current is not { } pilot || c.Id != pilot.HiddenHead;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    static CharacterAppearance Headless(CharacterAppearance look)
    {
        var copy = (CharacterAppearance)Clone!(look);
        SetHeadless!(copy, true);
        Made.Add(copy, Marker);
        return copy;
    }

    /// <summary>
    /// The headless copies made here (their materials are drawn two-sided).
    /// </summary>
    static readonly ConditionalWeakTable<CharacterAppearance, object> Made = new();

    static readonly object Marker = new();

    /// <summary>
    /// Postfix of <c>CharacterContent.BodyMaterial</c> and <c>ItemMaterial</c>: a first-person copy's body and worn items are drawn without back-face
    /// culling (the original's "body solid from the inside"), with the setting on. The materials are made per look, so only the copy's change.
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter names
    public static void MaterialPostfix(CharacterAppearance c, object __result)
    {
        try
        {
            if (Pilot.Current is not { Settings.BodySolidInside: true } || !Made.TryGetValue(c, out _)) return;

            __result.GetType().GetField("DoubleSided")?.SetValue(__result, true);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) Pilot.Current?.LogError($"head hide failed, the head stays drawn: {e.Message}");
    }
}
