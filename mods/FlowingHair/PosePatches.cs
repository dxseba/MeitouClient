using System.Collections;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;

namespace Meitou.FlowingHair;

/// <summary>
/// The pose side: after the renderer poses a character and swings its gear (<c>CharacterRenderer.Sway</c>, every pose, in the parallel pose loop),
/// each baked hair mesh's copies are written: the chain's nodes for a character chosen to flow, else the head bone. A still portrait's palette
/// (<c>PosePortrait</c>) and a held pose (<c>Freeze</c>, a cut-off head) get the head bone too, as the engine copies every bone there. Display only.
/// </summary>
static class PosePatches
{
    /// <summary>What the port keeps of one drawn character.</summary>
    internal sealed class Drawn
    {
        public object? Asset;
        public HairChain?[] Chains = [];
        public TailRod?[] Tails = [];
        public float Distance = float.MaxValue;
        public volatile bool Active;
        public volatile bool Player;
    }

    static readonly ConditionalWeakTable<object, Drawn> States = new();

    /// <summary>This frame's characters within reach that could flow, for the next frame's choice (<see cref="Choose"/>).</summary>
    static ConcurrentQueue<Drawn> s_candidates = new();
    static readonly HashSet<Drawn> ActiveSet = [];

    static Func<object, object?>? s_asset;
    static Func<object, Matrix4x4[]>? s_bones;
    static Func<object, Matrix4x4>? s_world;
    static Func<object, Vector3>? s_centre;
    static Func<object, object?>? s_ragdoll;
    static Func<object, SkeletonRig>? s_rig;
    static Func<object, double>? s_clock;
    static Func<object, Matrix4x4[]>? s_portraitBones;
    static Func<object, object>? s_states;
    static FieldInfo? s_liveAsset;
    static ModContext? s_context;
    static int s_faulted;

    /// <summary>The renderer last seen posing (for the player lookup).</summary>
    static volatile object? s_renderer;

    /// <summary>The camera's eye, set each frame.</summary>
    public static Vector3 Eye;

    public static void Install(ModContext context, Assembly game)
    {
        s_context = context;
        var renderer = Access.Type(game, "Meitou.Game.Rendering.Characters.CharacterRenderer");
        var asset = Access.Type(game, "Meitou.Game.Rendering.Characters.CharacterAsset");
        var state = Access.Nested(renderer, "CharState");
        var live = Access.Nested(renderer, "Live");
        s_asset = Access.Getter<object?>(state, "Asset");
        s_bones = Access.Getter<Matrix4x4[]>(state, "Bones");
        s_world = Access.Getter<Matrix4x4>(state, "World");
        (s_centre, s_ragdoll) = (Access.Getter<Vector3>(state, "Centre"), Access.Getter<object?>(state, "PosedRagdoll"));
        s_rig = Access.Getter<SkeletonRig>(asset, "Rig");
        (s_clock, s_portraitBones) = (Access.Getter<double>(renderer, "_motionSeconds"), Access.Getter<Matrix4x4[]>(renderer, "_portraitBones"));
        s_states = Access.Getter<object>(renderer, "_states");
        s_liveAsset = live.GetField("Asset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        var harmony = context.Harmony;
        harmony.Patch(Access.Method(renderer, "Sway", 3), postfix: new HarmonyMethod(typeof(PosePatches), nameof(SwayPostfix)));
        harmony.Patch(Access.Method(renderer, "PosePortrait", 1), postfix: new HarmonyMethod(typeof(PosePatches), nameof(PortraitPostfix)));
        harmony.Patch(Access.Method(renderer, "Freeze", 3), postfix: new HarmonyMethod(typeof(PosePatches), nameof(FreezePostfix)));
    }

    static void Fault(string what, Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"{what} failed; that hair is drawn rigid: {e}");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void SwayPostfix(object __instance, object state)
    {
        try
        {
            s_renderer = __instance;
            Pose(state, s_clock!(__instance));
        }
        catch (Exception e)
        {
            Fault("moving hair", e);
            if (s_asset!(state) is { } asset && MeshPatches.Looks.TryGetValue(asset, out var hair)) RigidAll(hair, s_bones!(state));
        }
    }

    static void PortraitPostfix(object __instance, object?[] __args)
    {
        try
        {
            if (__args[0] is { } live && s_liveAsset!.GetValue(live) is { } asset && MeshPatches.Looks.TryGetValue(asset, out var hair))
                RigidAll(hair, s_portraitBones!(__instance));
        }
        catch (Exception e)
        {
            Fault("a portrait's hair", e);
        }
    }

    static void FreezePostfix(object __instance, long key, bool __result)
    {
        try
        {
            if (!__result || s_states!(__instance) is not IDictionary states || states[key] is not { } state) return;

            if (s_asset!(state) is { } asset && MeshPatches.Looks.TryGetValue(asset, out var hair)) RigidAll(hair, s_bones!(state));
        }
        catch (Exception e)
        {
            Fault("a held pose's hair", e);
        }
    }
#pragma warning restore IDE1006

    static void RigidAll(LookHair hair, Matrix4x4[] bones)
    {
        foreach (var plan in hair.Plans) HairChain.Rigid(plan, bones);
    }

    /// <summary>Writes a posed character's hair copies: its chains when it was chosen to flow, else rigid.</summary>
    static void Pose(object state, double time)
    {
        if (s_asset!(state) is not { } asset || !MeshPatches.Looks.TryGetValue(asset, out var hair)) return;

        var bones = s_bones!(state);
        if (bones.Length < 2 * s_rig!(asset).BoneCount) return;

        var drawn = States.GetValue(state, _ => new Drawn());
        if (!ReferenceEquals(drawn.Asset, asset)) Rebuild(drawn, asset, hair, state);

        var s = FlowingHairMod.Settings;
        bool flows = s.Enabled && s.Distance > 0 && s_ragdoll!(state) is null && (drawn.Player || s.SimulateNpcs);
        Offer(drawn, s_centre!(state), flows, s);
        flows &= drawn.Active;

        var world = s_world!(state);
        var wind = HairWind.Hair;
        float rain = HairWind.Weather?.Rain ?? 0;
        for (int i = 0; i < hair.Plans.Length; i++)
        {
            var plan = hair.Plans[i];
            if (flows && hair.Flows[i] && Wanted(plan, s)) Move(drawn, i, plan, s, bones, in world, time, wind, rain);
            else
            {
                drawn.Chains[i]?.Reset();
                drawn.Tails[i]?.Reset();
                HairChain.Rigid(plan, bones);
            }
        }
    }

    /// <summary>A tail flows only with its race's switch on.</summary>
    static bool Wanted(HairPlan plan, FlowingHairSettings s) => plan.Kind switch
    {
        HairKind.KhajjitTail => s.KhajjitTails,
        HairKind.CatTail => s.CatTails,
        _ => true,
    };

    static void Move(Drawn drawn, int i, HairPlan plan, FlowingHairSettings s, Matrix4x4[] bones, in Matrix4x4 world, double time, Vector3 wind,
        float rain)
    {
        if (drawn.Tails[i] is { } tail) tail.Advance(plan, s, bones, in world, time, wind, rain);
        else drawn.Chains[i]?.Advance(plan, s, bones, in world, time, wind, rain);
    }

    /// <summary>A new look: a chain per hair mesh, a rod per tail (its idle swish phase from the character's identity, display only).</summary>
    static void Rebuild(Drawn drawn, object asset, LookHair hair, object state)
    {
        float phase = (RuntimeHelpers.GetHashCode(state) & 0xFFFF) * (MathF.Tau / 0x10000);
        drawn.Asset = asset;
        drawn.Chains = [.. hair.Plans.Select(p => p.IsTail ? null : new HairChain())];
        drawn.Tails = [.. hair.Plans.Select(p => p.IsTail ? new TailRod(phase) : null)];
    }

    /// <summary>A character within reach (10 % inside it to start, the original's hysteresis) is a candidate for the next frame's choice.</summary>
    static void Offer(Drawn drawn, Vector3 centre, bool eligible, FlowingHairSettings s)
    {
        drawn.Distance = Vector3.Distance(centre, Eye);
        float reach = drawn.Active ? s.Distance : 0.9f * s.Distance;
        if (eligible && drawn.Distance <= reach) s_candidates.Enqueue(drawn);
    }

    /// <summary>
    /// Once a frame, before it is drawn: the nearest candidates of the last frame, up to the character limit, flow (the original: nearest visible
    /// eligible characters first, players and NPCs alike).
    /// </summary>
    public static void Choose(int limit)
    {
        var offered = Interlocked.Exchange(ref s_candidates, new ConcurrentQueue<Drawn>());
        var chosen = offered.Distinct().OrderBy(d => d.Distance).Take(Math.Max(1, limit)).ToHashSet();
        foreach (var drawn in ActiveSet)
        {
            if (!chosen.Contains(drawn)) drawn.Active = false;
        }

        ActiveSet.Clear();
        foreach (var drawn in chosen)
        {
            drawn.Active = true;
            ActiveSet.Add(drawn);
        }
    }

    /// <summary>
    /// Marks which drawn characters are the player's (renderer keys carry the character's slot: <c>((slot &lt;&lt; 32 | generation) + 1) | epoch &lt;&lt; 56</c>).
    /// Host thread, between the renderer's updates; a renderer busy elsewhere is tried again next frame.
    /// </summary>
    public static void MarkPlayers(HashSet<int> playerSlots)
    {
        if (s_renderer is not { } renderer || s_states?.Invoke(renderer) is not IDictionary states) return;

        try
        {
            foreach (DictionaryEntry entry in states)
            {
                if (entry.Value is null || !States.TryGetValue(entry.Value, out var drawn)) continue;

                long key = ((long)entry.Key & ((1L << 56) - 1)) - 1;
                drawn.Player = playerSlots.Contains((int)(key >> 32));
            }
        }
        catch (InvalidOperationException)
        {
            // changed while read: next frame
        }
    }
}
