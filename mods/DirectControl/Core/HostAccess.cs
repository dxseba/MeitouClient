using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// The game host's internal members the port reads and sets on the host's thread (by name; a nightly that renames one turns the feature that needs
/// it off with a log line). The host itself is captured by <see cref="Patches"/> from the view update.
/// </summary>
static class HostAccess
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly Type? HostType = AccessTools.TypeByName("Meitou.Game.GameHost");
    static readonly Type? PlayerType = AccessTools.TypeByName("Meitou.Game.PlayerInterface");

    static readonly FieldInfo? SessionField = HostType?.GetField("Session", Any);
    static readonly FieldInfo? PlayerField = HostType?.GetField("Player", Any);
    static readonly FieldInfo? CameraField = HostType?.GetField("Camera", Any);
    static readonly MemberInfo? GpuMember = (MemberInfo?)HostType?.GetField("Gpu", Any) ?? HostType?.GetProperty("Gpu", Any);
    static readonly MethodInfo? DrawnPositionMethod = HostType?.GetMethod("DrawnPosition", Any, [typeof(CharacterSnapshot)]);
    static readonly MethodInfo? CoveredGetter = HostType?.GetProperty("Covered", Any)?.GetMethod;

    static readonly PropertyInfo? FollowProperty = PlayerType?.GetProperty("Follow", Any);
    static readonly PropertyInfo? StealthViewProperty = PlayerType?.GetProperty("StealthView", Any);
    static readonly PropertyInfo? ModalProperty = PlayerType?.GetProperty("ModalOpen", Any);
    static readonly PropertyInfo? CapturesProperty = PlayerType?.GetProperty("UiCapturesPointer", Any);

    /// <summary>
    /// The running game host, once seen.
    /// </summary>
    public static object? Host { get; set; }

    /// <summary>
    /// Every member found (else the camera and pointer parts stay off).
    /// </summary>
    public static bool Complete => SessionField is not null && PlayerField is not null && CameraField is not null && DrawnPositionMethod is not null
        && FollowProperty is not null && StealthViewProperty is not null && ModalProperty is not null;

    public static WorldSession? Session => Host is { } h ? SessionField?.GetValue(h) as WorldSession : null;

    public static WorldCamera? Camera => Host is { } h ? CameraField?.GetValue(h) as WorldCamera : null;

    static object? Player => Host is { } h ? PlayerField?.GetValue(h) : null;

    /// <summary>
    /// The character the strategy camera follows (the game's Follow).
    /// </summary>
    public static CharacterId? Follow
    {
        get => Player is { } p ? FollowProperty?.GetValue(p) as CharacterId? : null;
        set
        {
            if (Player is { } p) FollowProperty?.SetValue(p, value);
        }
    }

    /// <summary>
    /// A window that needs the mouse is open (inventory, trade, dialogue, menus) or the interface holds the pointer.
    /// </summary>
    public static bool InterfaceBusy => Player is { } p
        && (ModalProperty?.GetValue(p) is true || CapturesProperty?.GetValue(p) is true);

    /// <summary>
    /// A loading screen or the warm-up covers the world.
    /// </summary>
    public static bool Covered => Host is { } h && CoveredGetter?.Invoke(h, null) is true;

    /// <summary>
    /// Where the character is drawn this frame (between ticks).
    /// </summary>
    public static Vector3 Drawn(CharacterSnapshot c) =>
        Host is { } h && DrawnPositionMethod?.Invoke(h, [c]) is Vector3 at ? at : c.Position;

    /// <summary>
    /// The head bone as last drawn, or null.
    /// </summary>
    public static Vector3? Head(CharacterId id)
    {
        if (Player is not { } p || StealthViewProperty?.GetValue(p) is not { } view) return null;

        var drawn = view.GetType().GetProperty("DrawnHead", Any)?.GetValue(view) as Func<CharacterId, Vector3?>;
        return drawn?.Invoke(id);
    }

    /// <summary>
    /// Whether the character renderer has the look's meshes ready.
    /// </summary>
    public static bool Prepared(CharacterAppearance look) => RendererCall("IsPrepared", look) is true;

    /// <summary>
    /// Asks the character renderer to prepare the look's meshes.
    /// </summary>
    public static void Prefetch(CharacterAppearance look) => RendererCall("Prefetch", look);

    /// <summary>
    /// The foliage renderer (its grass range), or null.
    /// </summary>
    public static Game.Rendering.Foliage.FoliageRenderer? Foliage
    {
        get
        {
            if (Host is not { } h || GpuMember is null) return null;

            object? gpu = GpuMember is FieldInfo f ? f.GetValue(h) : ((PropertyInfo)GpuMember).GetValue(h);
            return gpu?.GetType().GetField("Foliage", Any)?.GetValue(gpu) as Game.Rendering.Foliage.FoliageRenderer;
        }
    }

    static object? RendererCall(string method, CharacterAppearance look)
    {
        if (Host is not { } h || GpuMember is null) return null;

        object? gpu = GpuMember is FieldInfo f ? f.GetValue(h) : ((PropertyInfo)GpuMember).GetValue(h);
        object? renderer = gpu?.GetType().GetField("Characters", Any)?.GetValue(gpu);
        var call = renderer?.GetType().GetMethod(method, Any, [typeof(CharacterAppearance)]);
        return call?.Invoke(renderer, [look]);
    }

    static readonly MethodInfo? HoldPointerMethod = HostType?.GetMethod("HoldPointer", Any);
    static readonly FieldInfo? FrameHost = typeof(Modding.ModFrame).GetField("_host", Any);

    /// <summary>
    /// Pins the pointer at <paramref name="anchor"/> (the host's own hold, as the orbit's).
    /// </summary>
    public static void HoldPointer(object mouse, Vector2 anchor)
    {
        if (Host is { } h) HoldPointerMethod?.Invoke(h, [mouse, anchor]);
    }

    /// <summary>
    /// Calls the player interface's <paramref name="method"/>(point) (its pointer move).
    /// </summary>
    public static void PlayerCall(string method, Vector2 at)
    {
        if (Player is { } p) p.GetType().GetMethod(method, Any, [typeof(Vector2)])?.Invoke(p, [at]);
    }

    /// <summary>
    /// A click of <paramref name="button"/> at <paramref name="at"/> (interface pixels) through the player interface, as the game view's own.
    /// </summary>
    public static void Click(Game.Host.Input.MouseButton button, Vector2 at)
    {
        if (Player is not { } p) return;

        var type = p.GetType();
        type.GetMethod("MouseDown", Any)?.Invoke(p, [button, at, false, false, false]);
        type.GetMethod("MouseUp", Any)?.Invoke(p, [button, at, false]);
    }

    /// <summary>
    /// Takes the host from a frame callback's view of it.
    /// </summary>
    public static void Capture(Modding.ModFrame frame) => Host ??= FrameHost?.GetValue(frame);

    /// <summary>
    /// The snapshot character with this id, or null.
    /// </summary>
    public static CharacterSnapshot? Find(WorldSnapshot snapshot, CharacterId id)
    {
        foreach (var c in snapshot.Characters)
        {
            if (c.Id == id) return c;
        }

        return null;
    }
}
