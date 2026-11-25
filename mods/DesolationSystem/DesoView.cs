using Meitou.Simulation;

namespace Meitou.DesolationSystem;

/// <summary>A polearm's slow on a victim: the fraction its attacks lose until a tick.</summary>
readonly record struct Slow(float Amount, long Until);

/// <summary>A squad's Field Nutrition aura: the nutritionist and the bonus its squadmates get.</summary>
readonly record struct Aura(CharacterId Source, float Bonus);

/// <summary>A player character's line in the panel: its name, Ryu style and the ids of its active perks.</summary>
sealed record Shown(string Name, int Style, string[] Perks);

/// <summary>
/// What the tick publishes for readers outside its serial steps (the parallel speed, part and hunger filters, the host's HUD asks and the
/// cards): replaced whole, never changed after it is published.
/// </summary>
sealed record DesoView(
    IReadOnlyDictionary<CharacterId, int> Styles,
    IReadOnlyDictionary<CharacterId, Slow> Slows,
    IReadOnlyDictionary<CharacterId, long> Bursts,
    IReadOnlyDictionary<int, int> Humanoids,
    IReadOnlySet<int> ChefSquads,
    IReadOnlySet<int> WildSquads,
    IReadOnlyDictionary<CharacterId, Shown> Active)
{
    internal static readonly DesoView Empty = new(
        new Dictionary<CharacterId, int>(), new Dictionary<CharacterId, Slow>(), new Dictionary<CharacterId, long>(), new Dictionary<int, int>(),
        new HashSet<int>(), new HashSet<int>(), new Dictionary<CharacterId, Shown>());
}
