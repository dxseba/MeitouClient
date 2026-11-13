using HarmonyLib;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>
/// Gives the character fragment shader the mesh's own vertex colour as <c>vBrutalityColour</c>. The stock vertex stage passes it in
/// <c>vColour</c> only for icons and puts the mesh's world up axis there elsewhere, but generated tissue carries its colour (rgb) and
/// tissue code (alpha) in the vertex colour.
/// </summary>
internal static class StockVertexColour
{
    internal const string Name = "vBrutalityColour";
    const string VertexDeclaration = "out vec4 vColour;";
    const string VertexAssignment = "vUv = aUv;";
    const string FragmentDeclaration = "in vec4 vColour;";
    const string ColouredItem = "if (flag(f, 524288u)) d.rgb *= vColour.rgb;";
    static volatile bool enabled;

    /// <summary>Whether the fragment stage declares <see cref="Name"/>; shader edits reading it require this.</summary>
    internal static bool Enabled => enabled;

    internal static bool Install(ModContext context)
    {
        if (enabled) return true;
        try
        {
            var shaders = typeof(Model).Assembly.GetType("Meitou.Game.Rendering.Characters.CharacterShaders", throwOnError: true)!;
            var source = AccessTools.Method(shaders, "VertexSource", [typeof(bool)]);
            var vertex = AccessTools.Method(shaders, "Vertex", []);
            var fragment = AccessTools.Method(shaders, "Fragment", [typeof(bool)]);
            if (source?.ReturnType != typeof(string) || vertex is null || fragment?.ReturnType != typeof(string)
                || source.Invoke(null, [false]) is not string plain || !TryPatchVertex(plain, out _)
                || fragment.Invoke(null, [false]) is not string early || !TryPatchFragment(early, out _)
                || fragment.Invoke(null, [true]) is not string late || !TryPatchFragment(late, out _))
            {
                context.Log("Brutality tissue vertex colours unavailable for this stock shader.");
                return false;
            }

            context.Harmony.Patch(source, postfix: new HarmonyMethod(typeof(StockVertexColour), nameof(VertexPostfix)));
            // The fragment stage may only read the varying once the vertex stage writes it.
            if (vertex.Invoke(null, null) is not string patched || !patched.Contains(Name, StringComparison.Ordinal))
            {
                context.Log("Brutality tissue vertex colours unavailable: the stock vertex shader was not patched.");
                return false;
            }
            context.Harmony.Patch(fragment, postfix: new HarmonyMethod(typeof(StockVertexColour), nameof(FragmentPostfix)));
            enabled = true;
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality tissue vertex colours unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static void VertexPostfix(ref string __result)
    {
        if (TryPatchVertex(__result, out string patched)) __result = patched;
    }

    static void FragmentPostfix(ref string __result)
    {
        if (TryPatchFragment(__result, out string patched)) __result = patched;
    }

    static bool TryPatchVertex(string source, out string patched)
    {
        patched = source;
        if (source.Contains(Name, StringComparison.Ordinal) || !Unique(source, VertexDeclaration) || !Unique(source, VertexAssignment))
            return false;

        patched = source.Replace(VertexDeclaration, $"{VertexDeclaration}\nout vec4 {Name};", StringComparison.Ordinal)
            .Replace(VertexAssignment, $"{VertexAssignment}\n    {Name} = aColour;", StringComparison.Ordinal);
        return true;
    }

    /// <summary>A vertex-coloured item is tinted by its mesh's colour again, as before the stock shader reused <c>vColour</c>.</summary>
    static bool TryPatchFragment(string source, out string patched)
    {
        patched = source;
        if (source.Contains($"in vec4 {Name};", StringComparison.Ordinal) || !Unique(source, FragmentDeclaration) || !Unique(source, ColouredItem))
            return false;

        patched = source.Replace(FragmentDeclaration, $"{FragmentDeclaration}\nin vec4 {Name};", StringComparison.Ordinal)
            .Replace(ColouredItem, ColouredItem.Replace("vColour", Name, StringComparison.Ordinal), StringComparison.Ordinal);
        return true;
    }

    static bool Unique(string source, string text)
    {
        int first = source.IndexOf(text, StringComparison.Ordinal);
        return first >= 0 && source.IndexOf(text, first + text.Length, StringComparison.Ordinal) < 0;
    }
}
