using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ItTicklesMe;

/// <summary>
/// Drops the light hit reaction of a melee blow whose stagger damage is below a share of the victim's stumble threshold, so weak hits no longer stun-lock
/// a tough fighter. Heavy staggers and hits at or above the share are unchanged.
/// </summary>
public sealed class ItTicklesMeMod : IMod
{
    internal static readonly TicklesSettings Settings = new();

    /// <summary>
    /// The original's config record: a LOCATIONAL_DAMAGE of this name whose <c>affects move speed</c> holds the ratio (clamped 0..1).
    /// </summary>
    const string ConfigRecord = "It Tickles Me - Light Stagger Ratio";

    public void Initialize(ModContext context)
    {
        if (RecordRatio(context.Data) is { } ratio)
        {
            Settings.Ratio = ratio;
            context.Log($"It. Tickles. Me: ratio {ratio} from the record '{ConfigRecord}'");
        }

        context.AddSettings(Settings);

        context.AddSystem("it-tickles-me", _ => new TicklesFilter(Settings), SystemAnchor.End);
        context.Log("It. Tickles. Me initialized");
    }

    /// <summary>
    /// The ratio the original's data mod stores (its first record of the name, as <c>getDataByName</c>), or null without one.
    /// </summary>
    static float? RecordRatio(Meitou.Data.GameDatabase data)
    {
        var record = data.OfType(Meitou.Data.Fcs.FcsRecordType.LocationalDamage).FirstOrDefault(r => r.Name == ConfigRecord);
        if (record is null || !record.Floats.TryGetValue("affects move speed", out float value) || !float.IsFinite(value)) return null;

        return Math.Clamp(value, 0, 1);
    }
}

/// <summary>
/// Answers <see cref="BlowReactionKind.None"/> for a light melee stagger whose stagger damage is under the share of the victim's stumble threshold (the
/// original's <c>_startStumble</c> skip; it also needs a threshold above 0).
/// </summary>
sealed class TicklesFilter(TicklesSettings settings) : IBlowReactionFilter
{
    public BlowReactionKind Reaction(World world, in BlowReactionAsk ask)
    {
        if (!settings.Enabled || ask.Source != CombatHitSource.Melee || ask.Kind != BlowReactionKind.Light || ask.Threshold <= 0) return ask.Kind;

        return ask.StaggerDamage < ask.Threshold * settings.Ratio ? BlowReactionKind.None : ask.Kind;
    }
}

public sealed class TicklesSettings
{
    [Setting("Filter weak staggers", Hint = "Weak melee hits no longer interrupt with a light stumble.")]
    public bool Enabled { get; set; } = true;

    [Setting("Stagger threshold share", Hint = "A light stagger needs at least this share of the victim's stumble threshold.", Min = 0, Max = 1, Step = 0.01)]
    public float Ratio { get; set; } = 0.33f;
}
