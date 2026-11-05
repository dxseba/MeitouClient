using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Brutality.Rendering;

/// <summary>Standing wounded actors use the same immutable geometry path without allocating a fragment physics owner.</summary>
internal static class BodyWoundRenderBridge
{
    sealed record Variant(CharacterAppearance Appearance, ulong Stamp, SevereHeadWoundState Severe, RuntimeCharacterGeometry Geometry, bool DeepAbdomen);
    sealed class Shown
    {
        public readonly Dictionary<CharacterId, Variant> Geometry = [];
        public readonly CutGeometryQueue<CharacterId, CharacterGeometryAdapter.BuildKey> Queue = new(start: CutGeometryWorker.StartWound);
    }
    static readonly ConditionalWeakTable<World, Shown> worlds = new();
    static RenderResources? resources;
    static Func<object, CharacterId, long>? drawKey;

    public static void Install(ModContext context, RenderResources source)
    {
        resources = source;
        var type = AccessTools.TypeByName("Meitou.Game.GameHost");
        if (type is null) return;
        var method = AccessTools.Method(type, "DrawKey") ?? throw new MissingMethodException(type.FullName, "DrawKey");
        var host = Expression.Parameter(typeof(object)); var character = Expression.Parameter(typeof(CharacterId));
        drawKey = Expression.Lambda<Func<object, CharacterId, long>>(Expression.Call(Expression.Convert(host, type), method, character), host, character).Compile();
        RenderBridge.BeforeEffects += Fill;
    }
    internal static void ReleaseWorld(World world)
    {
        if (!worlds.TryGetValue(world, out var shown)) return;
        shown.Queue.Dispose();
        foreach (var variant in shown.Geometry.Values) { variant.Geometry.Retire(); resources?.Geometry.Forget(variant.Appearance); }
        worlds.Remove(world);
    }
    internal static RuntimeCharacterGeometry? PendingGeometry(CharacterAppearance look, CharacterAppearance? previousLook,
        RuntimeCharacterGeometry? previous) => ReferenceEquals(look, previousLook) && previous is { IsRetired: false } ? previous : null;

    internal static RuntimeCharacterGeometry? CompatiblePendingGeometry(CharacterAppearance look, CharacterAppearance? previousLook,
        RuntimeCharacterGeometry? previous, SevereHeadWoundState? previousSevere, SevereHeadWoundState currentSevere,
        bool previousDeepAbdomen, bool currentDeepAbdomen) => previousSevere == currentSevere && previousDeepAbdomen == currentDeepAbdomen
            ? PendingGeometry(look, previousLook, previous) : null;

    static void Fill(object host, WorldSession session, CharacterDrawList list)
    {
        long started = RenderPhaseTrace.Start();
        try { FillWounds(host, session, list); }
        finally { RenderPhaseTrace.End(RenderPhase.Wounds, started); }
    }

    static void FillWounds(object host, WorldSession session, CharacterDrawList list)
    {
        if (resources is not { } source || drawKey is null) return;
        var frame = BrutalityRuntime.For(session.World)?.WoundFrameAt(session.ShownTick);
        var shown = worlds.GetValue(session.World, static _ => new());
        var needed = frame?.Characters.Where(pair => pair.Value.Torso is null && pair.Value.Head is null && pair.Value.Limbs.Count == 0)
            .ToDictionary(pair => drawKey(host, pair.Key), pair => pair.Value) ?? [];
        var current = needed.Values.Select(state => state.Character).ToHashSet();
        shown.Queue.Retain(current);
        foreach (var id in shown.Geometry.Keys.Where(id => !current.Contains(id)).ToArray())
        { shown.Geometry[id].Geometry.Retire(); shown.Geometry.Remove(id); }
        if (needed.Count == 0) return;

        // CPU geometry uses captured snapshots. Continue drawing the native look or a compatible mesh while it prepares.
        long captureStarted = Stopwatch.GetTimestamp(); int queued = 0;
        for (int i = 0; i < list.Items.Count; i++)
        {
            var instance = list.Items[i];
            if (RenderBridge.IsPresentationHeld(session.World, instance.Key) || !needed.TryGetValue(instance.Key, out var state)) continue;
            if (instance.Geometry is { } existing && CharacterGeometryAdapter.SurfaceFor(existing) is not null) continue;
            ulong stamp = state.Wounds.StateKey();
            var severe = state.SevereHead with { HeavyHits = 0 };
            bool deepAbdomen = state.Wounds.BellyHits >= 10;
            shown.Geometry.TryGetValue(state.Character, out var previous);
            if (previous is { } ready && ReferenceEquals(ready.Appearance, instance.Appearance)
                && ready.Stamp == stamp && ready.Severe == severe && !ready.Geometry.IsRetired)
            { list.Items[i] = instance with { Geometry = ready.Geometry }; continue; }

            CharacterGeometryResult? generated = null;
            if (RenderBridge.IsCurrentCutPending(session.World, state.Character, instance.Appearance, state))
                shown.Queue.Remove(state.Character);
            else if (source.Rigs.For(instance.Appearance) is { } setup && source.Mesh(instance.Appearance.BodyMesh) is { } body
                && !GeometryPreparations.ShouldDefer(session.World, instance.Appearance, setup.Rig, body))
            {
                var request = CharacterGeometryAdapter.RequestKey(instance.Appearance, setup.Rig, body, state, default);
                if (!source.Geometry.TryCached(instance.Appearance, setup.Rig, body, state, out generated, default(CharacterGeometryComponents)))
                {
                    if (shown.Queue.TryTake(state.Character, request, out var completed))
                        generated = source.Geometry.Publish(request, completed);
                    else if (queued < 4 && (queued == 0 || Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds < 5)
                        && shown.Queue.NeedsRequest(state.Character, request))
                    {
                        var input = CharacterGeometryAdapter.Capture(request, state, source.Mesh, deferMeshReads: true);
                        var adapter = source.Geometry;
                        if (shown.Queue.Request(state.Character, request, cancellation => adapter.BuildUnpublished(input, cancellation, prepareFragments: false))) queued++;
                    }
                    else if (shown.Queue.TryPendingKey(state.Character, out var pending) && pending != request)
                        shown.Queue.Remove(state.Character);
                }
            }
            if (generated is null)
            {
                if (CompatiblePendingGeometry(instance.Appearance, previous?.Appearance, previous?.Geometry, previous?.Severe, severe,
                    previous?.DeepAbdomen == true, deepAbdomen) is { } fallback)
                    list.Items[i] = instance with { Geometry = fallback };
                continue;
            }
            shown.Queue.Remove(state.Character);
            RenderBridge.Track(session.World, generated.Retained);
            if (previous is not null && !ReferenceEquals(previous.Geometry, generated.Retained)) previous.Geometry.Retire();
            shown.Geometry[state.Character] = new(instance.Appearance, stamp, severe, generated.Retained, deepAbdomen);
            list.Items[i] = instance with { Geometry = generated.Retained };
        }
        shown.Queue.StartNext();
    }
}
