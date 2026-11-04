using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality.Effects;

/// <summary>The pinned cosmetic trackers use SplitMix's finalizer without Rng.Mix's initial increment.
/// Keep it separate from gameplay RNG so identical hits keep their electrical patterns and scrap sequence.</summary>
internal static class RobotVisualRandom
{
    internal static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }
    }

    internal static ulong DischargeSeed(in CombatHit hit, ulong worldSeed)
    {
        static ulong Identity(CharacterId id) => ((ulong)(uint)id.Slot << 32) | (uint)id.Generation;
        return Mix(worldSeed ^ Mix(Identity(hit.Victim)) ^ Mix(Identity(hit.Attacker)) ^ Mix((ulong)hit.Tick)
            ^ Mix((uint)hit.Part + 0x524F424F5453504BUL) ^ Mix(BitConverter.SingleToUInt32Bits(hit.Damage.Cut)) ^ Mix((uint)hit.CutDirection));
    }

    internal static ulong ScrapSeed(in GoreEffectCut cut, ulong worldSeed) => Mix(worldSeed
        ^ ((ulong)(uint)cut.Owner.Slot << 32 | (uint)cut.Owner.Generation) ^ Mix((ulong)cut.Tick)
        ^ Mix((uint)cut.Part + 0x53435241504D4554UL) ^ (uint)cut.Kind);

    internal static float Next(ref ulong value)
    {
        value = Mix(unchecked(value + 0x9E3779B97F4A7C15UL));
        return (value >> 40) / 16777216f;
    }
}
