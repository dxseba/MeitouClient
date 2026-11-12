using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>
/// Reports landings from the stock falling-head path when an advanced fragment has no physics slot.
/// </summary>
internal sealed class StockNativeHeadPools
{
    /// <summary>
    /// The stock distance from the floor to a landed head's placement, in world units.
    /// </summary>
    const float HeadRestHeight = 1.3f;

    /// <summary>
    /// The installed observer.
    /// </summary>
    static StockNativeHeadPools? s_installed;

    /// <summary>
    /// Tracked heads stay associated with their native draw owner.
    /// </summary>
    readonly ConditionalWeakTable<object, Display> _displays = new();

    /// <summary>
    /// The stock head list, head placement fields and host session.
    /// </summary>
    readonly FieldInfo _heads, _key, _tick, _landed, _start, _session;

    /// <summary>
    /// One stock draw owner's observed and still-bleeding heads.
    /// </summary>
    sealed class Display
    {
        /// <summary>
        /// Native object identity prevents a resting head from acquiring another bleed budget.
        /// </summary>
        public readonly ConditionalWeakTable<object, Landing> Seen = new();

        /// <summary>
        /// Landings to report as stopped if the native head is removed.
        /// </summary>
        public readonly List<Landing> Active = [];

        /// <summary>
        /// The latest observation pass.
        /// </summary>
        public long Frame;
    }

    /// <summary>
    /// A native head observed this pass, without a reference to its render object.
    /// </summary>
    sealed class Landing(long key, double until)
    {
        /// <summary>
        /// The draw key of the stock head.
        /// </summary>
        public readonly long Key = key;

        /// <summary>
        /// A settled head no longer needs monitoring after its budget has dried up.
        /// </summary>
        public readonly double Until = until;

        /// <summary>
        /// The last pass in which the head was present.
        /// </summary>
        public long Frame;
    }

    /// <summary>
    /// Resolves the same stock head representation used by the cut-mesh replacement.
    /// </summary>
    StockNativeHeadPools(Type host, Type decapitation, Type head)
    {
        _heads = StockHostParts.Field(decapitation, "_heads");
        _key = StockHostParts.Field(head, "Key");
        _tick = StockHostParts.Field(head, "Tick");
        _landed = StockHostParts.Field(head, "Landed");
        _start = StockHostParts.Field(head, "Start");
        _session = StockHostParts.Field(host, "Session");
    }

    /// <summary>
    /// Installs the fallback after the stock and modded head draw paths have run.
    /// </summary>
    internal static bool Install(ModContext context)
    {
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var decapitation = StockHostParts.Decapitation.FieldType;
            var head = AccessTools.Inner(decapitation, "FallingHead") ?? throw new MissingMemberException(decapitation.Name, "FallingHead");
            var add = AccessTools.Method(decapitation, "AddHeads", [typeof(CharacterDrawList)])
                ?? throw new MissingMethodException(decapitation.Name, "AddHeads");
            var observer = new StockNativeHeadPools(host, decapitation, head);

            context.Harmony.Patch(add, postfix: new HarmonyMethod(typeof(StockNativeHeadPools), nameof(AfterDraw)) { priority = Priority.Last });
            s_installed = observer;
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock head puddle fallback unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    /// <summary>
    /// Visits the native head list from its owning render callback.
    /// </summary>
    static void AfterDraw(object __instance, CharacterDrawList list) => s_installed?.Observe(__instance, list);

    /// <summary>
    /// Publishes fresh landings and stops the corresponding emitter if a head disappears.
    /// </summary>
    void Observe(object decapitation, CharacterDrawList list)
    {
        if (_heads.GetValue(decapitation) is not IList falling || StockHostParts.HostOf(decapitation) is not { } host
            || _session.GetValue(host) is not WorldSession shown) return;
        if (falling.Count == 0 && !_displays.TryGetValue(decapitation, out _)) return;

        var display = _displays.GetOrCreateValue(decapitation);
        long frame = ++display.Frame;
        double now = (shown.ShownTick + shown.SimulationAlpha) * shown.World.TickSeconds;
        foreach (object head in falling)
        {
            if (display.Seen.TryGetValue(head, out var known))
                known.Frame = frame;
            else if (_landed.GetValue(head) is float landed && float.IsFinite(landed))
                Register(host, shown, list, display, head, landed, now);
        }

        for (int index = display.Active.Count - 1; index >= 0; index--)
        {
            var entry = display.Active[index];
            if (entry.Frame == frame && now < entry.Until) continue;

            if (entry.Frame != frame) StockDetachedPools.RemoveNativeHead(host, entry.Key);
            display.Active.RemoveAt(index);
        }
    }

    /// <summary>
    /// A head can land on another character in the stock path. Require contact with the walkable floor.
    /// </summary>
    void Register(object host, WorldSession shown, CharacterDrawList list, Display display, object head, float landed, double now)
    {
        if (_key.GetValue(head) is not long key || _tick.GetValue(head) is not double born
            || !double.IsFinite(born) || _start.GetValue(head) is not Vector3 start) return;

        var entry = new Landing(key, now + 16) { Frame = display.Frame };
        display.Seen.Add(head, entry);
        if (now - (born * shown.World.TickSeconds + landed) > 1) return;

        int index = list.Items.FindLastIndex(item => item.Key == key && !item.Hidden);
        if (index < 0) return;
        var draw = list.Items[index];
        var race = draw.Appearance.Race;
        if (!StockDetachedPools.CanBleed(race)) return;

        var at = draw.Position;
        float floor = shown.World.Walkability.GroundHeight(at.X, at.Z, start.Y);
        if (!float.IsFinite(floor) || MathF.Abs(at.Y - floor - HeadRestHeight) > .25f) return;

        at.Y = floor;
        StockDetachedPools.NativeHead(host, shown, key, at, race);
        display.Active.Add(entry);
    }
}
