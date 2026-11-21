using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimbToughness;

/// <summary>
/// Port of LimbToughness (workshop 3787222853), rebuilt from its description: every point of Toughness raises each fleshy part's maximum health by
/// <see cref="ToughnessSettings.PercentPerPoint"/> of its own (0.5%: 50 Toughness = +25%), additively. Applied on the part's HP multiplier.
/// </summary>
public sealed class LimbToughnessMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new ToughnessSettings();
        context.AddSettings(settings);
        context.AddSystem("limbs", _ => new LimbSystem(settings), SystemAnchor.End);
        context.Log("Limb Toughness initialized");
    }
}

public sealed class ToughnessSettings
{
    [Setting("Health % per Toughness", Hint = "Extra maximum health of each part per point of Toughness, in % of its own.", Min = 0, Max = 5, Step = 0.05)]
    public float PercentPerPoint { get; set; } = 0.5f;

    [Setting("Robot races", Hint = "Apply to races marked as robots (skeletons).")]
    public bool Robots { get; set; } = true;

    [Setting("Prosthetic limbs", Hint = "Apply to fitted robotic limbs too.")]
    public bool Prosthetics { get; set; }
}

/// <summary>
/// Every 60th tick in the serial slow step, sets each living character's part multiplier to the one it had when first seen (kept by part object, so
/// a new world starts afresh) times 1 + Toughness x the rate. A part keeps its share of health as its maximum moves, so a raise does not wound.
/// </summary>
sealed class LimbSystem(ToughnessSettings settings) : ITickSystem
{
    readonly ConditionalWeakTable<HealthPart, StrongBox<float>> _original = new();

    public void SlowWorld(World world)
    {
        if (world.Tick % 60 != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical, Stats: { } stats } cold) continue;

            bool robot = cold.Race?.IsRobot == true;
            float factor = robot && !settings.Robots ? 1 : 1 + settings.PercentPerPoint / 100 * stats.Stored(StatsEnumerated.Toughness);
            foreach (var part in medical.Parts) Apply(part, factor);
        }
    }

    void Apply(HealthPart part, float factor)
    {
        float original = _original.GetValue(part, p => new StrongBox<float>(p.HpMultiplier)).Value;
        if (part.Limb == LimbState.Replaced && !settings.Prosthetics) factor = 1;

        float wanted = original * factor;
        if (MathF.Abs(part.HpMultiplier - wanted) < 1e-4f) return;

        float before = part.MaxHp;
        part.HpMultiplier = wanted;
        if (before > 0 && part.MaxHp > 0) part.Flesh *= part.MaxHp / before;
    }
}
