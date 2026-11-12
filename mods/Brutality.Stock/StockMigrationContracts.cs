using Meitou.Data.Save;

namespace Meitou.Brutality;

// The stock build uses the original codec without installing the original host patches.
public sealed record BrutalityDurableState(string Key, float Experience, SevereHeadWoundState SevereHead,
    HeadCutState? Head, TorsoBisectionState? Torso, LimbCutState[] Limbs)
{
    public BodyWoundState Wounds { get; init; } = BodyWoundState.Empty;
    public ChestHoleState? ChestHole { get; init; }
}

internal static class SaveIdentityAdapter
{
    internal static string? KeyFor(Hand handle) => handle.IsNull ? null
        : FormattableString.Invariant($"hand:v1:{handle.Type}:{handle.C}:{handle.Cs}:{handle.I}:{handle.S}");
}
