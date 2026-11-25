using System.Diagnostics;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Sky;
using Meitou.Modding;

namespace Meitou.FlowingHair;

/// <summary>
/// Port of Teirdalin's Flowing Hair: scalp hair and beards sway with the character's movement, gravity and the weather's wind, wetter and heavier
/// in rain, for the nearest characters within a distance of the camera. Renderer side only (Harmony on Meitou's character renderer): the mesh is
/// baked onto a short chain of palette copies, and the chain is simulated each frame.
/// </summary>
public sealed class FlowingHairMod : IMod
{
    internal static readonly FlowingHairSettings Settings = new();

    int _frames;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    TimeSpan _last;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        var game = typeof(WorldWeather).Assembly;
        try
        {
            MeshPatches.Install(context, game);
            PosePatches.Install(context, game);
            ParticlePatches.Install(context);
        }
        catch (Exception e) when (e is MissingMemberException or InvalidOperationException or ArgumentException)
        {
            context.LogError($"the character renderer has changed ({e.Message}); hair is drawn as without the mod");
            context.Harmony.UnpatchAll(context.Id);
            return;
        }

        context.AddFrame(Frame);
        context.AddKeyAction("stress-wind", "Toggle stress wind", ToggleStress, Binding.Of(Key.P));
        context.Log("Flowing Hair initialized");
    }

    /// <summary>The original's debug key: a maximum steady wind on and off (only with its setting on; off again when the setting goes off).</summary>
    static void ToggleStress(ModKeyContext _) => HairWind.StressOn = Settings.StressWindKey && !HairWind.StressOn;

    /// <summary>Once a frame: the wind, the camera's eye, which characters flow, and every 10 frames which of them are the player's.</summary>
    void Frame(ModFrame frame)
    {
        var now = _clock.Elapsed;
        HairWind.Tick(Settings, (float)(now - _last).TotalSeconds, frame.Paused);
        _last = now;
        if (!Settings.StressWindKey) HairWind.StressOn = false;

        PosePatches.Eye = frame.CameraEye;
        PosePatches.Choose(Settings.CharacterLimit);
        if (_frames++ % 10 != 0) return;

        var players = new HashSet<int>();
        foreach (var c in frame.Snapshot.Characters)
        {
            if (c.IsPlayer) players.Add(c.Id.Slot);
        }

        PosePatches.MarkPlayers(players);
    }
}
