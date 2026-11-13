using Meitou.Game.Rendering.Frame;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Uses a held stain mesh's alpha as a wet mask in the stock item colour pass.</summary>
internal static class StockWeaponWetness
{
    // SkinTone is used exclusively by the body shading path. This value marks only our generated weapon materials.
    const float Marker = -6179;
    const string ItemGloss = "if (flag(f, 16384u) && flag(f, 1024u)) metal = cm.b;";
    static readonly string WetGloss = "\n" + $$"""
                    if (!icon && flag(f, 524288u) && m.skinTone.x == {{Marker.ToString("0.0", CultureInfo.InvariantCulture)}})
                    {
                        float wet = clamp(vBrutalityColour.a, 0.0, 1.0);
                        gloss = mix(gloss, 0.65, wet);
                        metal = mix(metal, 0.02, wet);
                    }
                    """;
    static FieldInfo? skinTone;
    static volatile bool enabled;

    internal static bool Enabled => enabled;

    internal static void Install(ModContext context)
    {
        // The tissue code in vertex alpha reaches the fragment stage only through the restored vertex colour.
        if (enabled || !StockVertexColour.Install(context)) return;
        try
        {
            var assembly = typeof(Model).Assembly;
            var shaders = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterShaders", throwOnError: true)!;
            var material = assembly.GetType("Meitou.Game.Rendering.Characters.PartMaterial", throwOnError: true)!;
            var tone = AccessTools.Field(material, "SkinTone");
            var fragment = AccessTools.Method(shaders, "Fragment", [typeof(bool)]);
            if (tone?.FieldType != typeof(Vector3) || fragment?.ReturnType != typeof(string)
                || fragment.Invoke(null, [false]) is not string early || !TryInject(early, out _)
                || fragment.Invoke(null, [true]) is not string late || !TryInject(late, out _))
            {
                context.Log("Brutality wet weapon lighting unavailable for this stock shader. Using blade tint.");
                return;
            }
            context.Harmony.Patch(fragment, postfix: new HarmonyMethod(typeof(StockWeaponWetness), nameof(FragmentPostfix)));
            skinTone = tone;
            enabled = true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality wet weapon lighting unavailable: {error.GetBaseException().Message}");
        }
    }

    internal static void Mark(object material)
    {
        if (enabled) skinTone!.SetValue(material, new Vector3(Marker, 0, 0));
    }

    static void FragmentPostfix(ref string __result)
    {
        if (TryInject(__result, out string patched)) __result = patched;
    }

    static bool TryInject(string source, out string patched)
    {
        patched = source;
        int start = source.IndexOf(ItemGloss, StringComparison.Ordinal);
        if (start < 0 || source.IndexOf(ItemGloss, start + ItemGloss.Length, StringComparison.Ordinal) >= 0
            || source.Contains(WetGloss, StringComparison.Ordinal)) return false;
        patched = source.Insert(start + ItemGloss.Length, WetGloss);
        return true;
    }
}
