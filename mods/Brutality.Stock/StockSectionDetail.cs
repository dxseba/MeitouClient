using Meitou.Game.Rendering.Frame;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Shades organic cut surfaces using plane UVs and the mesh's tissue code in vertex alpha.</summary>
internal static class StockSectionDetail
{
    // The stock item path does not read SkinTone. Keep the marker distinct from the wet weapon marker.
    const float Marker = -6187;
    const string ShaderFlag = "bool flag(uint f, uint bit) { return (f & bit) != 0u; }";
    const string ItemGloss = "if (flag(f, 16384u) && flag(f, 1024u)) metal = cm.b;";
    const string DetailFunctions = """

        float brutalitySectionHash(vec2 p)
        {
            vec3 h = fract(p.xyx * 0.1031);
            h += dot(h, h.yzx + 33.33);
            return fract((h.x + h.y) * h.z);
        }

        float brutalitySectionNoise(vec2 p)
        {
            vec2 cell = floor(p);
            vec2 w = fract(p);
            w = w * w * (3.0 - 2.0 * w);
            float a = mix(brutalitySectionHash(cell), brutalitySectionHash(cell + vec2(1, 0)), w.x);
            float b = mix(brutalitySectionHash(cell + vec2(0, 1)), brutalitySectionHash(cell + vec2(1)), w.x);
            return mix(a, b, w.y);
        }

        """;
    static readonly string DetailLighting = "\n" + $$"""
        if (!icon && flag(f, 524288u) && m.skinTone.x == {{Marker.ToString("0.0", CultureInfo.InvariantCulture)}})
        {
            // Alpha encodes muscle, fascia, cortical bone and marrow in order. RGB keeps the race's tissue colours.
            float tissue = vBrutalityColour.a;
            float bone = smoothstep(0.53, 0.68, tissue);
            float marrow = smoothstep(0.80, 0.98, tissue);
            float fascia = smoothstep(0.24, 0.39, tissue) * (1.0 - bone);
            vec2 width = fwidth(uv);
            float mottle = brutalitySectionNoise(uv * vec2(5.0, 7.0));

            // Warped fascicles and fine fibres along the cut face. Fade the fine pattern below a pixel.
            float across = uv.y + 0.15 * sin(uv.x * 5.0 + mottle * 2.6) + 0.08 * mottle;
            float bundles = 0.5 + 0.5 * cos(18.0 * across + 1.8 * uv.x);
            float grooves = smoothstep(0.77, 0.98, bundles);
            float fineFade = 1.0 - smoothstep(0.75, 2.2, dot(width, vec2(13.0, 93.0)));
            float fibres = cos(93.0 * across + 13.0 * uv.x) * fineFade;
            float muscleShade = 0.82 + 0.34 * mottle - 0.19 * grooves + 0.13 * fibres;
            float fasciaShade = 0.79 + 0.40 * mottle + 0.08 * fibres;

            // Broken cancellous bone and marrow are mottled and porous; the cortical edge is denser.
            vec2 grid = uv * 33.0;
            float poreSeed = brutalitySectionHash(floor(grid));
            vec2 jitter = vec2(poreSeed, fract(poreSeed * 5.173)) - 0.5;
            vec2 grain = fract(grid) - 0.5 - 0.40 * jitter;
            float poreSize = mix(0.09, 0.22, marrow) * mix(0.75, 1.22, poreSeed);
            float poreFade = 1.0 - smoothstep(0.25, 0.85, 33.0 * max(width.x, width.y));
            float pits = (1.0 - smoothstep(poreSize * 0.38, poreSize, length(grain))) * poreFade;
            float mineralShade = 0.84 + 0.29 * mottle + (poreSeed - 0.5) * 0.16 * poreFade
                - pits * mix(0.19, 0.40, marrow);
            albedo *= mix(mix(muscleShade, fasciaShade, fascia), mineralShade, bone);
            gloss = mix(mix(0.33 + 0.20 * mottle, 0.63, fascia), mix(0.19, 0.43, marrow), bone);
            metal = 0.0;
        }
        """;

    static FieldInfo? skinTone;
    static volatile bool enabled;

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
                context.Log("Brutality cut surface detail unavailable for this stock shader. Using mesh colours.");
                return;
            }
            context.Harmony.Patch(fragment, postfix: new HarmonyMethod(typeof(StockSectionDetail), nameof(FragmentPostfix)));
            skinTone = tone;
            enabled = true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality cut surface detail unavailable: {error.GetBaseException().Message}");
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
        int functions = UniqueAnchor(ShaderFlag);
        int lighting = UniqueAnchor(ItemGloss);
        if (functions < 0 || lighting <= functions || source.Contains("brutalitySectionHash", StringComparison.Ordinal)) return false;
        patched = source.Insert(lighting + ItemGloss.Length, DetailLighting)
            .Insert(functions + ShaderFlag.Length, DetailFunctions);
        return true;

        int UniqueAnchor(string anchor)
        {
            int first = source.IndexOf(anchor, StringComparison.Ordinal);
            return first >= 0 && source.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal) < 0 ? first : -1;
        }
    }
}
