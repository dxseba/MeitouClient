using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.MetabolicTraversal;

/// <summary>
/// Hunger follows effort: the vanilla hunger drain is scaled by <c>floor + (peak - floor) x effort^exponent</c>, effort being the character's speed
/// over its current top speed (which already carries encumbrance). Idle drains less, a walk about as vanilla, a sprint many times more, so walking
/// costs less food per distance than running.
/// </summary>
public sealed class MetabolicTraversalMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new MetabolicSettings();
        context.AddSettings(settings);
        context.AddSystem("metabolism", _ => new MetabolismSystem(settings), SystemAnchor.End);
        context.Log("Metabolic Traversal initialized");
    }
}

public sealed class MetabolicSettings
{
    [Setting("Metabolic hunger", Hint = "Hunger drain follows how hard a character moves.")]
    public bool Enabled { get; set; } = true;

    [Setting("Idle floor", Hint = "Drain multiplier when still or barely moving (1 = vanilla).", Min = 0, Max = 2, Step = 0.05)]
    public float Floor { get; set; } = 0.5f;

    [Setting("Sprint peak", Hint = "Drain multiplier at full speed.", Min = 1, Max = 30, Step = 0.5)]
    public float Peak { get; set; } = 12;

    [Setting("Effort curve", Hint = "Exponent of the effort curve: higher keeps walking cheap and makes sprinting costly.", Min = 1, Max = 5, Step = 0.1)]
    public float Exponent { get; set; } = 2.5f;

    [Setting("Player characters only", Hint = "Off: every character's hunger follows its effort.")]
    public bool PlayerOnly { get; set; } = true;
}

/// <summary>
/// Every 15th tick in the serial slow phase: for each character with an empty stomach whose hunger fell since the last pass (the vanilla drain), adds
/// the drain times (factor - 1), or gives part of it back when the factor is below 1. A fed stomach (hunger rising) is left alone.
/// </summary>
sealed class MetabolismSystem(MetabolicSettings settings) : ITickSystem
{
    const int Interval = 15;
    readonly Dictionary<CharacterId, float> _last = [];

    public void SlowWorld(World world)
    {
        if (world.Tick % Interval != 0) return;
        if (!settings.Enabled)
        {
            _last.Clear();
            return;
        }

        var table = world.Characters;
        var seen = new HashSet<CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical } cold || (settings.PlayerOnly && !cold.IsPlayer)) continue;

            var id = table.IdOf(slot);
            seen.Add(id);
            Scale(id, medical, Effort(table, slot));
        }

        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(gone);
    }

    static float Effort(CharacterTable table, int slot)
    {
        ref var next = ref table.Next[slot];
        float speed = MathF.Sqrt(next.Velocity.X * next.Velocity.X + next.Velocity.Z * next.Velocity.Z);
        return next.MaxSpeed > 0 ? Math.Clamp(speed / next.MaxSpeed, 0, 1) : 0;
    }

    void Scale(CharacterId id, Meitou.Simulation.Bodies.MedicalState medical, float effort)
    {
        float hunger = medical.Hunger;
        if (_last.TryGetValue(id, out float before) && medical.Fed <= 0 && hunger < before)
        {
            float factor = settings.Floor + (settings.Peak - settings.Floor) * MathF.Pow(effort, settings.Exponent);
            hunger = Math.Clamp(hunger - (before - hunger) * (factor - 1), 0, Meitou.Simulation.Bodies.MedicalState.MaxHunger);
            medical.Hunger = hunger;
        }

        _last[id] = hunger;
    }
}
