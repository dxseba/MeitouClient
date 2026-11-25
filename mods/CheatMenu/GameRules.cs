using System.Reflection;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>
/// The Game rules page and Max squad size: the original multiplies fields of the game's constants (<b>Verified</b>, its table of constant offsets
/// per rule, set to the value times the field's own when the rule changes, back to the own value at "normal"). The port does the same on the
/// merged <see cref="GameConstants"/> the systems share: their init-only properties are set by reflection from the cheat system's serial step, to
/// the value read at start times the rule's factor. A new world starts with every rule normal (<see cref="Reset"/>). Time until starving and
/// Item prices are the cheat system's filters instead.
/// </summary>
internal sealed class GameRules
{
    readonly GameConstants _constants;
    readonly ModContext _context;
    readonly Dictionary<PropertyInfo, object> _own = [];

    static readonly (Tier Tier, string[] Fields)[] Map =
    [
        (Tier.Bleeding, [nameof(GameConstants.BleedRate)]),
        (Tier.BloodRecovery, [nameof(GameConstants.BloodRecoveryRate)]),
        (Tier.WoundHealing, [nameof(GameConstants.HealRateMult), nameof(GameConstants.RestingHealRateMult)]),
        (Tier.KnockoutTime, [nameof(GameConstants.KnockoutTimeBase)]),
        (Tier.CarryWeight, [nameof(GameConstants.CarryWeightMult)]),
        (Tier.PrisonTime, [nameof(GameConstants.PrisonTime)]),
        (Tier.MaxSquadSize, [nameof(GameConstants.MaxSquadSize)]),
    ];

    public GameRules(ModContext context, GameConstants constants)
    {
        _context = context;
        _constants = constants;
        foreach (var (_, fields) in Map)
        {
            foreach (string field in fields)
            {
                if (typeof(GameConstants).GetProperty(field) is { CanWrite: true } p && p.GetValue(constants) is { } own) _own[p] = own;
                else context.LogError($"GameConstants.{field} not found: its rule does nothing");
            }
        }
    }

    /// <summary>Every rule back to the game's own values (a new world).</summary>
    public void Reset()
    {
        foreach (var (p, own) in _own) Write(p, own);
    }

    /// <summary>Sets the constants to the system's tiers; serial steps only.</summary>
    public void Apply(World world, CheatSystem system)
    {
        foreach (var (tier, fields) in Map)
        {
            float factor = system.Value(tier);
            foreach (string field in fields)
            {
                if (typeof(GameConstants).GetProperty(field) is not { } p || !_own.TryGetValue(p, out var own)) continue;

                Write(p, Scaled(tier, own, factor));
            }
        }
    }

    /// <summary>A multiplier times the own value; Max squad size's value replaces it (0: the game's own).</summary>
    static object Scaled(Tier tier, object own, float factor) => own switch
    {
        int whole when tier == Tier.MaxSquadSize => factor > 0 ? (int)factor : whole,
        int whole => (int)MathF.Round(whole * factor),
        float f => f * factor,
        _ => own,
    };

    void Write(PropertyInfo p, object value)
    {
        try
        {
            p.SetValue(_constants, value);
        }
        catch (Exception e) when (e is TargetInvocationException or ArgumentException or MethodAccessException)
        {
            _context.LogError($"GameConstants.{p.Name} not set: {e.Message}");
        }
    }
}
