using System.Runtime.CompilerServices;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.DesolationSystem;

/// <summary>Assassinate card modes (Silent Verdict).</summary>
enum Verdict : byte
{
    None,
    /// <summary>A landed stealth knock-out puts the target out for 270 s.</summary>
    Sneak,
    /// <summary>A landed stealth knock-out kills the target silently.</summary>
    Harvest,
}

/// <summary>
/// What the Harmony patches need to know of a character, filled by the system's serial step every <see cref="DesoSystem.Cadence"/> ticks and read
/// by the patched methods (any thread, read only). A character without an entry has no patched perk.
/// </summary>
readonly record struct Traits(
    float DodgeRate,
    bool Phantom,
    bool MasterKey,
    bool SureKill,
    bool BlueEye,
    bool NoCrime,
    bool SilentTouch,
    Verdict Verdict,
    float PorterSpeed,
    float Artifice);

/// <summary>The per-character and per-fighter tables (keyed weakly: a world's objects leave with it).</summary>
static class TraitTable
{
    static readonly ConditionalWeakTable<CharacterCold, StrongBox<Traits>> ByCharacter = new();
    static readonly ConditionalWeakTable<Fighter, StrongBox<float>> Reaches = new();

    internal static void Set(CharacterCold cold, Traits traits) => Put(ByCharacter, cold, traits, traits == default);

    internal static Traits Of(CharacterCold? cold) => cold is not null && ByCharacter.TryGetValue(cold, out var box) ? box.Value : default;

    /// <summary>The fighter's reach factor (Heavy Onslaught, Polearm Supremacy, Universal Mastery); 1 without one.</summary>
    internal static void SetReach(Fighter fighter, float factor) => Put(Reaches, fighter, factor, factor == 1);

    internal static float ReachOf(Fighter fighter) => Reaches.TryGetValue(fighter, out var box) ? box.Value : 1;

    static void Put<TKey, T>(ConditionalWeakTable<TKey, StrongBox<T>> table, TKey key, T value, bool none) where TKey : class
    {
        if (none)
        {
            table.Remove(key);
            return;
        }

        if (table.TryGetValue(key, out var box)) box.Value = value;
        else table.Add(key, new StrongBox<T>(value));
    }
}
