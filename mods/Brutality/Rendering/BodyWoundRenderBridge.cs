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
        if (resources is null || drawKey is null) return;
        var frame = BrutalityRuntime.For(session.World)?.WoundFrameAt(session.ShownTick);
        var shown = worlds.GetValue(session.World, static _ => new());
        var needed = frame?.Characters.Where(pair => pair.Value.Torso is null && pair.Value.Head is null && pair.Value.Limbs.Count == 0)
            .ToDictionary(pair => drawKey(host, pair.Key), pair => pair.Value) ?? [];
        var current = needed.Values.Select(state => state.Character).ToHashSet();
        foreach (var id in shown.Geometry.Keys.Where(id => !current.Contains(id)).ToArray())
        { shown.Geometry[id].Geometry.Retire(); shown.Geometry.Remove(id); }
        if (needed.Count == 0) return;
        // Wounds are shader data; defer only initial mesh preparation, never gameplay or healing.
        long started = Stopwatch.GetTimestamp(); int rebuilt = 0;
        for (int i = 0; i < list.Items.Count; i++)
        {
            var instance = list.Items[i];
            if (RenderBridge.IsPresentationHeld(session.World, instance.Key)) continue;
            if (!needed.TryGetValue(instance.Key, out var state)) continue;
            if (instance.Geometry is { } existing && CharacterGeometryAdapter.SurfaceFor(existing) is not null) continue;
            ulong stamp = state.Wounds.StateKey();
            var severe = state.SevereHead with { HeavyHits = 0 };
            shown.Geometry.TryGetValue(state.Character, out var previous);
            bool sameLook = previous is not null && ReferenceEquals(previous.Appearance, instance.Appearance);
            if (sameLook && previous is { } ready && ready.Stamp == stamp && ready.Severe == severe && !ready.Geometry.IsRetired)
            { list.Items[i] = instance with { Geometry = ready.Geometry }; continue; }
            if (RenderBridge.IsCurrentCutPending(session.World, state.Character, instance.Appearance, state))
            {
                if (CompatiblePendingGeometry(instance.Appearance, previous?.Appearance, previous?.Geometry, previous?.Severe, severe,
                    previous?.DeepAbdomen == true, state.Wounds.BellyHits >= 10) is { } fallback)
                    list.Items[i] = instance with { Geometry = fallback };
                continue;
            }
            if (rebuilt >= 4 || rebuilt > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 5)
            {
                // A different appearance must use its native meshes until ready: never draw an old race, gender or outfit.
                if (CompatiblePendingGeometry(instance.Appearance, previous?.Appearance, previous?.Geometry, previous?.Severe, severe,
                    previous?.DeepAbdomen == true, state.Wounds.BellyHits >= 10) is { } fallback)
                    list.Items[i] = instance with { Geometry = fallback };
                continue;
            }
            if (resources.Rigs.For(instance.Appearance) is not { } setup || resources.Mesh(instance.Appearance.BodyMesh) is not { } body) continue;
            if (GeometryPreparations.ShouldDefer(session.World, instance.Appearance, setup.Rig, body))
            {
                if (CompatiblePendingGeometry(instance.Appearance, previous?.Appearance, previous?.Geometry, previous?.Severe, severe,
                    previous?.DeepAbdomen == true, state.Wounds.BellyHits >= 10) is { } fallback)
                    list.Items[i] = instance with { Geometry = fallback };
                continue;
            }
            var generated = resources.Geometry.Build(instance.Appearance, setup.Rig, body, resources.Mesh, state);
            rebuilt++;
            RenderBridge.Track(session.World, generated.Retained);
            if (previous is not null && !ReferenceEquals(previous.Geometry, generated.Retained)) previous.Geometry.Retire();
            shown.Geometry[state.Character] = new(instance.Appearance, stamp, severe, generated.Retained, state.Wounds.BellyHits >= 10);
            list.Items[i] = instance with { Geometry = generated.Retained };
        }
    }
}
