using System.Collections.Frozen;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SpeedMenu;

/// <summary>
/// Port of Speed menu (workshop 3818592083): a button beside the time controls opens SELECTED CHARACTER SPEEDS, the selected characters by
/// maximum running speed, slowest first, each with its current speed, in mph.
/// </summary>
public sealed class SpeedMenuMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("speeds", _ => new SpeedSystem(), SystemAnchor.End);
        HudAdapter.Install(context);
        context.AddUi(new SpeedMenuUi(DiamondImage(context.Folder)));
        context.Log("Speed menu initialized");
    }

    /// <summary>
    /// The original's button picture, read from the original's own folder (a required item: a sibling of this mod's folder, by its Workshop id
    /// or its mod name); null when it is not installed, and the button is a text one.
    /// </summary>
    static string? DiamondImage(string folder)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(folder)) is not { } parent) return null;

        return new[] { "3818592083", "Speed Menu" }
            .Select(name => Path.Combine(parent, name, "gui", "selected_speed_diamond.png"))
            .FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// One character's speeds in world units per second: its maximum running speed and its current speed.
/// </summary>
public readonly record struct Runner(string Name, float MaxSpeed, float Speed);

/// <summary>
/// Publishes each player character's name, top speed (the speed stat with its load and injuries) and current speed four times a second, the
/// original's refresh rate.
/// </summary>
sealed class SpeedSystem : ITickSystem
{
    /// <summary>
    /// The original's refreshes per second.
    /// </summary>
    const int RefreshesPerSecond = 4;

    /// <summary>
    /// The last published speeds, by character.
    /// </summary>
    volatile FrozenDictionary<CharacterId, Runner> _published = FrozenDictionary<CharacterId, Runner>.Empty;

    /// <summary>
    /// The last published speeds, by character.
    /// </summary>
    public FrozenDictionary<CharacterId, Runner> Published => _published;

    public void SlowWorld(World world)
    {
        long step = (long)MathF.Round(1 / (world.TickSeconds * RefreshesPerSecond));
        if (world.Tick % Math.Max(1, step) != 0) return;

        var table = world.Characters;
        var map = new Dictionary<CharacterId, Runner>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;

            var velocity = table.Next[slot].Velocity;
            float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            map[table.IdOf(slot)] = new(cold.Name, table.Next[slot].MaxSpeed, speed);
        }

        _published = map.ToFrozenDictionary();
    }
}
