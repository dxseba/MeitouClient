using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;

namespace Meitou.Nitoryu;

/// <summary>
/// The original's mirror (README "Other mods' animations": "the whole body is mirrored at run time, so a right-hand vanilla swing becomes a
/// left-hand one"), for the second blade's strikes that have no track of their own: the data phase adds an ANIMATION record
/// <c>nitoryu mirror &lt;clip&gt;</c> per vanilla clip of the pool (<see cref="NitoryuData"/>), the pose layer plays it in place of the vanilla
/// one (<see cref="PoseLayer"/>), and when the renderer poses that layer each bone takes its mirror bone's key (<c>L</c> and <c>R</c> swapped)
/// reflected through the body's side-to-side plane. Harmony: a prefix on <c>Meitou.Data.Characters.PoseScratch.AddLayer</c> (display only;
/// any other layer runs as it is). <b>Unknown</b>: the reflection's axes are a guess for the Biped bone frames (a centre bone's local Z taken as
/// the side axis, a side bone's key kept as its partner's), not checked in game, so it is a setting, off by default.
/// </summary>
static class Mirror
{
    /// <summary>The record name prefix of the mirrored clips.</summary>
    public const string Prefix = "nitoryu mirror ";

    delegate void SampleCall(BoneTrack track, float time, out Vector3 t, out Quaternion r, out Vector3 s);

    static readonly ConditionalWeakTable<SkeletonRig, int[]> Partners = [];
    static AccessTools.FieldRef<PoseScratch, SkeletonRig>? s_rig;
    static AccessTools.FieldRef<PoseScratch, Vector3[]>? s_pos;
    static AccessTools.FieldRef<PoseScratch, Vector3[]>? s_scale;
    static AccessTools.FieldRef<PoseScratch, Quaternion[]>? s_rot;
    static SampleCall? s_sample;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var add = AccessTools.Method(typeof(PoseScratch), "AddLayer");
        var sample = AccessTools.Method(typeof(PoseScratch), "Sample");
        if (add is null || sample is null || AccessTools.Field(typeof(PoseScratch), "_rig") is null)
        {
            context.Log("PoseScratch.AddLayer or its fields not found: the second blade's swings are not mirrored");
            return;
        }

        s_rig = AccessTools.FieldRefAccess<PoseScratch, SkeletonRig>("_rig");
        s_pos = AccessTools.FieldRefAccess<PoseScratch, Vector3[]>("_pos");
        s_scale = AccessTools.FieldRefAccess<PoseScratch, Vector3[]>("_scale");
        s_rot = AccessTools.FieldRefAccess<PoseScratch, Quaternion[]>("_rot");
        s_sample = sample.CreateDelegate<SampleCall>();
        context.Harmony.Patch(add, prefix: new HarmonyMethod(typeof(Mirror), nameof(AddPrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool AddPrefix(PoseScratch __instance, in ActiveLayer layer, float w, int count, float movement)
    {
        if (s_failed != 0 || !layer.Def.Label.StartsWith(Prefix, StringComparison.Ordinal)) return true;

        try
        {
            AddMirrored(__instance, layer, w, count, movement);
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"mirrored pose failed ({e.GetType().Name}: {e.Message})");
            return true;
        }
    }
#pragma warning restore IDE1006

    static void AddMirrored(PoseScratch scratch, in ActiveLayer layer, float w, int count, float movement)
    {
        var rig = s_rig!(scratch);
        var pos = s_pos!(scratch);
        var rot = s_rot!(scratch);
        var scale = s_scale!(scratch);
        int[] partner = Partners.GetValue(rig, Build);
        var clip = layer.Def.Clip;
        var excluded = layer.Def.Excluded;
        float time = clip.Length > 0 ? ((layer.Time % clip.Length) + clip.Length) % clip.Length : 0;
        for (int h = 0; h < count; h++)
        {
            int from = partner[h];
            var track = from >= 0 && from < clip.Tracks.Length ? clip.Tracks[from] : null;
            if (track is null || excluded is not null && excluded[h]) continue;

            s_sample!(track, time, out var t, out var r, out var s);
            if (from == h) (t, r) = (new Vector3(t.X, t.Y, -t.Z), new Quaternion(-r.X, -r.Y, r.Z, r.W));
            if (h == layer.Def.RootMotionBone) t = new Vector3(0, t.Y, 0);
            pos[h] += t * (w * movement);
            rot[h] = Quaternion.Normalize(rot[h] * Quaternion.Normalize(Quaternion.Lerp(Quaternion.Identity, r, w)));
            if (s != Vector3.One) scale[h] *= Vector3.One + (s - Vector3.One) * w;
        }
    }

    /// <summary>Each bone's mirror bone by handle (<c> L </c> and <c> R </c> swapped in its name), itself for a centre bone.</summary>
    static int[] Build(SkeletonRig rig)
    {
        var partner = new int[rig.ByHandle.Length];
        for (int h = 0; h < partner.Length; h++)
        {
            string name = rig.ByHandle[h]?.Name ?? "";
            string other = name.Contains(" L ", StringComparison.Ordinal) ? name.Replace(" L ", " R ", StringComparison.Ordinal)
                : name.Contains(" R ", StringComparison.Ordinal) ? name.Replace(" R ", " L ", StringComparison.Ordinal)
                : name.EndsWith(" L", StringComparison.Ordinal) ? name[..^2] + " R"
                : name.EndsWith(" R", StringComparison.Ordinal) ? name[..^2] + " L"
                : name;
            partner[h] = rig.Handle(other) ?? h;
        }

        return partner;
    }
}
