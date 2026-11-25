using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Transmog;

/// <summary>
/// The drawn look: a postfix on <c>World.FillIdentity</c> (where a character's look enters the snapshot the renderer and the interface draw from)
/// that, for a character with its transmog on, publishes the transmogrified look in place of its own. The look is built once per change of the
/// character's own look, its outfit or the settings and kept beside the character; the character itself keeps its own look (display only).
/// </summary>
static class PublishPatch
{
    /// <summary>The look published last for a character and what it was made from.</summary>
    sealed class Made
    {
        public CharacterAppearance? Own;
        public Outfit? Outfit;
        public int Version;
        public CharacterAppearance? Look;
    }

    static readonly ConditionalWeakTable<CharacterCold, Made> Looks = new();

    static Action<CharacterSnapshot, CharacterAppearance?>? s_setLook;
    static ModContext? s_context;
    static LookBuilder? s_builder;
    static TransmogSettings? s_settings;
    static int s_version;
    static int s_faulted;

    public static void Install(ModContext context, LookBuilder builder, TransmogSettings settings)
    {
        (s_context, s_builder, s_settings) = (context, builder, settings);
        var setter = AccessTools.PropertySetter(typeof(CharacterSnapshot), nameof(CharacterSnapshot.Appearance));
        var fill = AccessTools.Method(typeof(World), "FillIdentity");
        if (setter is null || fill is null)
        {
            context.LogError("World.FillIdentity or CharacterSnapshot.Appearance not found: transmog looks are not drawn");
            return;
        }

        s_setLook = AccessTools.MethodDelegate<Action<CharacterSnapshot, CharacterAppearance?>>(setter);
        context.Harmony.Patch(fill, postfix: new HarmonyMethod(typeof(PublishPatch), nameof(FillIdentityPostfix)));
    }

    /// <summary>A changed setting makes every transmog look again at the next publish.</summary>
    public static void SettingsChanged() => Interlocked.Increment(ref s_version);

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void FillIdentityPostfix(World __instance, CharacterSnapshot s, int i)
    {
        try
        {
            if (s.Appearance is { } own && __instance.Characters.Cold(i) is { } cold) Publish(s, cold, own);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a transmog look failed, the character's own is drawn: {e}");
        }
    }
#pragma warning restore IDE1006

    static void Publish(CharacterSnapshot s, CharacterCold cold, CharacterAppearance own)
    {
        if (TransmogSystem.Of(cold.LastingId) is not { } outfit || outfit.Idle(s_settings!.SelectedSlotsOnly, s_settings.IndependentWeapons)) return;

        var made = Looks.GetValue(cold, _ => new Made());
        int version = Volatile.Read(ref s_version);
        if (made.Own != own || made.Outfit != outfit || made.Version != version)
        {
            (made.Own, made.Outfit, made.Version) = (own, outfit, version);
            made.Look = s_builder!.Build(cold, own, outfit);
        }

        if (made.Look is { } look) s_setLook!(s, look);
    }
}
