using System.Globalization;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.MetabolicTraversal;

/// <summary>
/// Hunger follows effort, as the original's <c>calculateHungerMult</c> hook does (<b>Verified</b>, original DLL (decompiled)): the game's hunger
/// factor times <c>idle_hunger_rate + max_movement_penalty x effort^metabolic_curve</c>, effort = speed / top speed (0 when the top speed is 0.1 or
/// less, at most 1). The values come from the original's <c>config.json</c> when it is found, else its built-in defaults.
/// </summary>
public sealed class MetabolicTraversalMod : IMod
{
    const string OriginalItem = "3709852838";

    public void Initialize(ModContext context)
    {
        var settings = new MetabolicSettings();
        if (ConfigPath(context) is { } path) settings.Read(path, context.Log);
        context.AddSettings(settings);
        context.AddSystem("metabolism", _ => new MetabolismSystem(settings), SystemAnchor.End);
        context.Log("Metabolic Traversal initialized");
    }

    /// <summary><c>config.json</c> in this folder, else in the original's Workshop folder beside it; null when neither has one.</summary>
    static string? ConfigPath(ModContext context)
    {
        string own = Path.Combine(context.Folder, "config.json");
        if (File.Exists(own)) return own;

        string? parent = Path.GetDirectoryName(Path.GetFullPath(context.Folder));
        string original = parent is null ? "" : Path.Combine(parent, OriginalItem, "config.json");
        return File.Exists(original) ? original : null;
    }
}

/// <summary>The original's three <c>config.json</c> keys (defaults the DLL's own, <b>Verified</b>: 0.35, 6, 3) and the port's options.</summary>
public sealed class MetabolicSettings
{
    [Setting("Metabolic hunger", Hint = "Port option: hunger drain follows how hard a character moves.")]
    public bool Enabled { get; set; } = true;

    [Setting("Idle hunger rate", Hint = "idle_hunger_rate: the hunger multiplier at rest (1 = vanilla).", Min = 0, Max = 2, Step = 0.05)]
    public float Idle { get; set; } = 0.35f;

    [Setting("Max movement penalty", Hint = "max_movement_penalty: added to the multiplier at full speed.", Min = 0, Max = 30, Step = 0.5)]
    public float Penalty { get; set; } = 6;

    [Setting("Metabolic curve", Hint = "metabolic_curve: exponent of the effort; higher keeps walking cheap and makes sprinting costly.", Min = 0.1,
        Max = 6, Step = 0.1)]
    public float Curve { get; set; } = 3;

    [Setting("Player characters only", Hint = "Port option. On: only player characters (the original: everyone).")]
    public bool PlayerOnly { get; set; }

    /// <summary>
    /// The original's reading: for each key, the number after the first colon of a line holding it (C's <c>sscanf(" %f")</c>), so trailing commas
    /// and comments do not matter.
    /// </summary>
    internal void Read(string path, Action<string> log)
    {
        foreach (string line in File.ReadLines(path))
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || Number(line[(colon + 1)..]) is not { } value) continue;

            if (line.Contains("idle_hunger_rate", StringComparison.Ordinal)) Idle = value;
            if (line.Contains("max_movement_penalty", StringComparison.Ordinal)) Penalty = value;
            if (line.Contains("metabolic_curve", StringComparison.Ordinal)) Curve = value;
        }

        log(string.Create(CultureInfo.InvariantCulture, $"config.json read from {path}: idle {Idle}, penalty {Penalty}, curve {Curve}"));
    }

    static float? Number(string text)
    {
        string t = text.Trim();
        int end = 0;
        while (end < t.Length && (char.IsAsciiDigit(t[end]) || t[end] is '.' or '-' or '+' or 'e' or 'E')) end++;
        return float.TryParse(t.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;
    }

    /// <summary>The multiplier for an effort in 0..1.</summary>
    public float Factor(float effort) => Idle + Penalty * MathF.Pow(effort, Curve);
}

/// <summary>
/// Every 15th tick in the serial slow phase: for each character with an empty stomach whose hunger fell since the last pass (the game's drain), adds
/// the drain times (factor - 1), or gives part of it back when the factor is below 1. A fed stomach (hunger rising) is left alone, as the original's
/// factor only scales the empty-stomach drain.
/// </summary>
sealed class MetabolismSystem(MetabolicSettings settings) : ITickSystem
{
    const int Interval = 15;
    const float MinTopSpeed = 0.1f;
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
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical } cold) continue;
            if (settings.PlayerOnly && !cold.IsPlayer) continue;

            var id = table.IdOf(slot);
            seen.Add(id);
            Scale(id, medical, Effort(table, slot));
        }

        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList()) _last.Remove(gone);
    }

    /// <summary>Horizontal speed over top speed: 0 at a top speed of 0.1 or less, at most 1.</summary>
    static float Effort(CharacterTable table, int slot)
    {
        ref var next = ref table.Next[slot];
        float speed = MathF.Sqrt(next.Velocity.X * next.Velocity.X + next.Velocity.Z * next.Velocity.Z);
        return next.MaxSpeed > MinTopSpeed ? MathF.Min(speed / next.MaxSpeed, 1) : 0;
    }

    void Scale(CharacterId id, MedicalState medical, float effort)
    {
        float hunger = medical.Hunger;
        if (_last.TryGetValue(id, out float before) && medical.Fed <= 0 && hunger < before)
        {
            hunger = MathF.Min(hunger - (before - hunger) * (settings.Factor(effort) - 1), MedicalState.MaxHunger);
            medical.Hunger = hunger;
        }

        _last[id] = hunger;
    }
}
