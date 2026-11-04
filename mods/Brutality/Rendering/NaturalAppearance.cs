using System.Reflection;
using Meitou.Data;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering;

/// <summary>Restore only bones controlled by explicit cut geometry; unrelated native medical losses keep their normal shape and visibility.</summary>
internal static class NaturalAppearance
{
    static readonly PropertyInfo hiddenLimbs = typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.HiddenLimbs))!;
    static readonly PropertyInfo headless = typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.Headless))!;
    static readonly PropertyInfo headOnly = typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.HeadOnly))!;

    public static CharacterAppearance Make(GameDatabase data, CharacterAppearance appearance, IEnumerable<CharacterPart>? parts = null,
        BrutalityActorView? state = null)
    {
        int restore = 0;
        if (state is not null)
            foreach (var cut in state.Limbs) if (cut.IsValid) restore |= 1 << cut.LimbSlot;
        var copy = appearance.WithParts(parts ?? appearance.Parts);
        var shape = appearance.Shape ?? CharacterShape.InputFor(data, appearance);
        copy.Shape = shape with
        {
            Missing = shape.Missing.Where(limb => (restore & 1 << (int)limb) == 0).ToHashSet(),
            HideStump = shape.HideStump & ~(HideStump)(restore & 3),
        };
        hiddenLimbs.SetValue(copy, appearance.HiddenLimbs & ~restore);
        if (state?.Head is { IsValid: true })
        {
            // A custom head cut supplies the visible head pieces itself.
            headless.SetValue(copy, false);
            headOnly.SetValue(copy, false);
        }
        else if (!appearance.Headless && state?.SevereHead.Kind == SevereHeadWoundKind.JawLost)
        {
            // An attached jaw wound needs the original skull; a later native decapitation must stay headless.
            headOnly.SetValue(copy, false);
        }
        return copy;
    }
}
