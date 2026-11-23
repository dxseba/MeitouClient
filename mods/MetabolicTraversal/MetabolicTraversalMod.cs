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
/// The original's <c>calculateHungerMult</c> hook as a need rate filter (<see cref="INeedRateFilter"/>): every body update's hunger rate (the race's
/// rate times the game's situation factor, the HUD's figure too) times the factor for the character's effort. A pure read of the ask and of the
/// character's last finished tick (velocity, top speed), so it may run on any thread.
/// </summary>
sealed class MetabolismSystem(MetabolicSettings settings) : INeedRateFilter
{
    const float MinTopSpeed = 0.1f;

    public float Rate(World world, in NeedRateAsk ask)
    {
        if (ask.Kind != NeedKind.Hunger || !settings.Enabled || !world.Characters.IsAlive(ask.Character)) return ask.Rate;

        int slot = ask.Character.Slot;
        if (settings.PlayerOnly && world.Characters.Cold(slot) is not { IsPlayer: true }) return ask.Rate;

        return ask.Rate * settings.Factor(Effort(world.Characters.Previous[slot]));
    }

    /// <summary>Horizontal speed over top speed: 0 at a top speed of 0.1 or less, at most 1.</summary>
    static float Effort(in CharacterHot hot)
    {
        float speed = MathF.Sqrt(hot.Velocity.X * hot.Velocity.X + hot.Velocity.Z * hot.Velocity.Z);
        return hot.MaxSpeed > MinTopSpeed ? MathF.Min(speed / hot.MaxSpeed, 1) : 0;
    }
}
