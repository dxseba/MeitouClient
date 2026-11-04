using System.Reflection;
using Meitou.Data;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering;

/// <summary>A presentation-only copy: native medical loss flags must not shrink the same bones the generated cut mesh follows.</summary>
internal static class NaturalAppearance
{
    public static CharacterAppearance Make(GameDatabase data, CharacterAppearance appearance, IEnumerable<CharacterPart>? parts = null)
    {
        var copy = appearance.WithParts(parts ?? appearance.Parts);
        copy.Shape = (appearance.Shape ?? CharacterShape.InputFor(data, appearance)) with { Missing = new HashSet<Limb>(), HideStump = HideStump.None };
        foreach (string name in new[] { "HiddenLimbs", "Headless", "HeadOnly" })
        {
            var property = typeof(CharacterAppearance).GetProperty(name)!;
            property.SetValue(copy, name == "HiddenLimbs" ? (object)0 : false);
        }
        return copy;
    }
}
