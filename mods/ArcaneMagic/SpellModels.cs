using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Rendering.Objects;
using Meitou.Modding;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The spells' models (README "Facts"), drawn from the original's meshes, found by file name in its <c>assets/*/v001</c> folders as every mesh is:
/// the Earth Spike (<c>AM_EarthSpike_v001.mesh</c>), the twister (<c>AM_Whirlwind_v001.mesh</c>), the bomb in flight
/// (<c>AM_ArcaneBomb_Projectile_v001.mesh</c>) and its burst (<c>AM_ArcaneBomb_Explosion_v001.mesh</c>). They are rigid loose meshes of the renderer's
/// debris pass, as the bolts in flight are; their skeleton animations are stood in for by motion: the spikes rise out of the ground until their
/// impact and sink at the end, the twister turns, the bomb spins, the burst swells. Harmony: a postfix on <c>Meitou.Game.Effects.Debris.AddBolts</c>
/// (the host's thread, once a frame, after the bolts are listed), reading the spells the tick published (<see cref="ArcaneSystem.Shapes"/>) and
/// adding to <c>WorldGpu.DebrisPieces</c>: display only, nothing the simulation reads. A failure logs once and draws nothing more.
/// </summary>
static class SpellModels
{
    const string Spike = "AM_EarthSpike_v001.mesh", Twister = "AM_Whirlwind_v001.mesh", Orb = "AM_ArcaneBomb_Projectile_v001.mesh",
        Burst = "AM_ArcaneBomb_Explosion_v001.mesh";

    /// <summary>The spikes stand in a ring this wide round the target.</summary>
    const float SpikeRing = 6;

    static FieldInfo? s_host, s_gpu, s_pieces, s_debris;
    static ModContext? s_context;
    static int s_failed;
    static readonly long[] Ids = [.. Enumerable.Range(0, 64).Select(_ => DebrisPiece.NewId())];

    public static void Patch(ModContext context)
    {
        s_context = context;
        var add = AccessTools.Method("Meitou.Game.Effects.Debris:AddBolts");
        var debrisType = AccessTools.TypeByName("Meitou.Game.Effects.Debris");
        var gpuType = AccessTools.TypeByName("Meitou.Game.Rendering.Frame.WorldGpu");
        s_host = debrisType?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(f => f.FieldType.Name == "GameHost");
        s_gpu = s_host?.FieldType.GetField("Gpu", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        s_pieces = gpuType?.GetField("DebrisPieces");
        s_debris = gpuType?.GetField("Debris");
        if (add is null || s_gpu is null || s_pieces is null || s_debris is null)
        {
            context.Log("Debris.AddBolts or WorldGpu.DebrisPieces not found: the spells show their flashes only");
            return;
        }

        context.Harmony.Patch(add, postfix: new HarmonyMethod(typeof(SpellModels), nameof(Added)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Added(object __instance)
    {
        if (s_failed != 0 || ArcaneSystem.Running?.Shapes is not { Length: > 0 } shapes) return;

        try
        {
            object? gpu = s_gpu!.GetValue(s_host!.GetValue(__instance));
            if (gpu is null || s_pieces!.GetValue(gpu) is not List<DebrisPiece> pieces || s_debris!.GetValue(gpu) is not DebrisRenderer debris) return;

            int n = 0;
            foreach (var shape in shapes) Draw(pieces, debris, shape, ref n);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"spell models failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    static void Draw(List<DebrisPiece> pieces, DebrisRenderer debris, SpellShape shape, ref int n)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Spike:
                for (int i = 0; i < shape.Count; i++) Spiked(pieces, debris, shape, i, ref n);
                break;
            case ShapeKind.Whirlwind:
                Add(pieces, debris, Twister, shape.At, Quaternion.CreateFromAxisAngle(Vector3.UnitY, shape.Age * MathF.Tau * 2), 1, ref n);
                break;
            case ShapeKind.BombFlying:
                Add(pieces, debris, Orb, shape.At, Quaternion.CreateFromAxisAngle(Vector3.UnitY, shape.Age * MathF.Tau), 1, ref n);
                break;
            case ShapeKind.Burst:
                float swell = 0.3f + 1.2f * Math.Clamp(shape.Age / ArcaneRules.BurstShow, 0, 1);
                Add(pieces, debris, Burst, shape.At, Quaternion.Identity, swell, ref n);
                break;
        }
    }

    /// <summary>Spike <paramref name="i"/>: on the ring, out of the ground over the 0.18 s to its impact, back down over the last 0.38 s.</summary>
    static void Spiked(List<DebrisPiece> pieces, DebrisRenderer debris, SpellShape shape, int i, ref int n)
    {
        if (debris.Extent(Spike) is not { } extent) return;

        float angle = shape.Count == 1 ? 0 : i * MathF.Tau / shape.Count;
        var ring = shape.Count == 1 ? Vector3.Zero : new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * SpikeRing;
        float rise = Math.Clamp(shape.Age / ArcaneRules.SpikeImpact, 0, 1);
        float sink = Math.Clamp((ArcaneRules.SpikeLife - shape.Age) / 0.38f, 0, 1);
        float drop = extent.Y * 2 * (1 - MathF.Min(rise, sink));
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
        Add(pieces, debris, Spike, shape.At + ring - Vector3.UnitY * drop, turn, 1, ref n);
    }

    /// <summary>A mesh whose own origin is at <paramref name="origin"/> (the renderer places a piece by its middle).</summary>
    static void Add(List<DebrisPiece> pieces, DebrisRenderer debris, string mesh, Vector3 origin, Quaternion turn, float scale, ref int n)
    {
        if (debris.Centre(mesh) is not { } centre || n >= Ids.Length) return;

        var middle = origin + Vector3.Transform(centre * scale, turn);
        pieces.Add(new DebrisPiece(mesh, scale, turn, middle, Id: Ids[n++]));
    }
}
