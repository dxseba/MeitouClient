using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.FlowingHair;

/// <summary>Scalp hair, a beard (the original's <c>ScalpHair</c> / <c>FacialHair</c>), or a race's tail in its body mesh.</summary>
enum HairKind
{
    Scalp = 1,
    Beard = 2,
    KhajjitTail = 3,
    CatTail = 4,
}

/// <summary>
/// How one hair, beard or tail mesh flows on one skeleton: the palette copies its hanging vertices are skinned to (the chain's nodes 1..n; node 0
/// is the carrying bone itself), the chain's axis and rest nodes in the mesh's binding space and the farthest a node leaves its rest. Made when the
/// mesh is first prepared for that skeleton (<see cref="HairPlans.For"/>), filled when the mesh is baked (<see cref="HairBake"/>).
/// </summary>
sealed class HairPlan
{
    public required int Id;
    public required HairKind Kind;
    public required int Bones;
    public required int[] Slots;
    public required string[] SlotNames;
    public required Vector3 HeadNub;
    public required object Marker;

    /// <summary>The bone carrying most of the hanging part (the head; for a tail its root's bone), and its name on the plan's skeleton.</summary>
    public int Dominant = -1;
    public string DominantName = "";

    /// <summary>From root to tip (unit, binding space); the root's place along it and the hanging length.</summary>
    public Vector3 Axis = -Vector3.UnitY;
    public float Root, Length;

    /// <summary>The chain's rest nodes (node 0 the root), the head's centre for the face collider, both in the binding space.</summary>
    public Vector3[] Rest = [];
    public Vector3 HeadCentre;
    public float MaxDisplacement;

    /// <summary>The mesh was baked: its hanging vertices are on the copies.</summary>
    public volatile bool Baked;

    /// <summary>Nodes of the chain past its root.</summary>
    public int Nodes => Slots.Length;

    public bool IsTail => Kind is HairKind.KhajjitTail or HairKind.CatTail;

    /// <summary>Whether <paramref name="rig"/> has the bones this plan was made for at the same handles (another skeleton of the same size does not).</summary>
    public bool Matches(SkeletonRig rig)
    {
        if (rig.BoneCount != Bones || NameOf(rig, Dominant) != DominantName) return false;

        for (int i = 0; i < Slots.Length; i++)
        {
            if (NameOf(rig, Slots[i]) != SlotNames[i]) return false;
        }

        return true;
    }

    static string? NameOf(SkeletonRig rig, int handle) => (uint)handle < (uint)rig.ByHandle.Length ? rig.ByHandle[handle]?.Name : null;
}

/// <summary>
/// The plans by mesh and skeleton size, and the original's rules for what flows: hair by mesh name (<c>HairCandidate.h</c> facts), tails by the
/// body mesh of the Khajiit race mod (<c>khajjit_male.mesh</c>, <c>khajjit_female.mesh</c>) and of The Cat Mod (<c>cat.mesh</c>).
/// </summary>
static class HairPlans
{
    /// <summary>Chain nodes past the root; the free bones are shared out scalp first, then the tail, the beard last.</summary>
    const int ScalpNodes = 4, TailNodes = 4, BeardNodes = 3;

    /// <summary>
    /// Bones whose palette copies no other mesh uses (a backpack is baked onto the copies of the bones it is skinned to: the spine, clavicles,
    /// arms, neck and at most the thighs), in the order they are taken.
    /// </summary>
    static readonly string[] FreeBoneWords = ["Nub", "Prop", "Toe", "Foot", "Calf"];

    static readonly ConcurrentDictionary<(string Path, int Bones), HairPlan?> ByMesh = new();
    static readonly ConcurrentDictionary<int, HairPlan> ById = new();
    static int s_next;

    /// <summary>Makes the marker a plan is found by in the mesh cache's key (<c>GearSway.Bake</c>: bones, NaN, plan id, kind).</summary>
    public static Func<int, int, HairKind, object>? MakeMarker;

    /// <summary>The rig the plans need when a mesh is prepared: any loaded rig of that size (kept per size, with a head and without).</summary>
    public static readonly ConcurrentDictionary<(int Bones, bool Head), SkeletonRig> RigsBySize = new();

    /// <summary>
    /// The original's name rule for worn meshes: beard, facial, moustache or mustache is facial hair; hair, scalp, ponytail, mohawk or nutfro
    /// scalp hair; else null.
    /// </summary>
    public static HairKind? Classify(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("beard") || name.Contains("facial") || name.Contains("moustache") || name.Contains("mustache")) return HairKind.Beard;

        if (name.Contains("hair") || name.Contains("scalp") || name.Contains("ponytail") || name.Contains("mohawk") || name.Contains("nutfro"))
            return HairKind.Scalp;

        return null;
    }

    /// <summary>A body mesh whose tail flows (the original's verified tail profiles, by file name); null for any other body.</summary>
    public static HairKind? ClassifyBody(string path) => Path.GetFileName(path).ToLowerInvariant() switch
    {
        "khajjit_male.mesh" or "khajjit_female.mesh" => HairKind.KhajjitTail,
        "cat.mesh" => HairKind.CatTail,
        _ => null,
    };

    /// <summary>The plan of a mesh on a skeleton of <paramref name="bones"/> bones (made once); null when the skeleton has too few free bones.</summary>
    public static HairPlan? For(string path, int bones, HairKind kind, SkeletonRig rig) =>
        ByMesh.GetOrAdd((path.ToLowerInvariant(), bones), _ => Make(kind, rig));

    /// <summary>The plan a mesh of this file was prepared with on this skeleton size; null when none.</summary>
    public static HairPlan? Find(string path, int bones) => ByMesh.TryGetValue((path.ToLowerInvariant(), bones), out var plan) ? plan : null;

    public static HairPlan? Get(int id) => ById.TryGetValue(id, out var plan) ? plan : null;

    static HairPlan? Make(HairKind kind, SkeletonRig rig)
    {
        if (MakeMarker is null || rig.BoneCount is <= 0 or > 128) return null;

        if (Slots(kind, FreeBones(rig)) is not { } slots) return null;

        int id = Interlocked.Increment(ref s_next);
        var plan = new HairPlan
        {
            Id = id,
            Kind = kind,
            Bones = rig.BoneCount,
            Slots = slots,
            SlotNames = [.. slots.Select(s => rig.ByHandle[s]?.Name ?? "")],
            HeadNub = HeadNub(rig),
            Marker = MakeMarker(rig.BoneCount, id, kind),
        };
        ById[id] = plan;
        return plan;
    }

    /// <summary>Scalp: the first free bones; tail: the next (the first on a rig too small for hair as well, an animal's); beard: the last.</summary>
    static int[]? Slots(HairKind kind, List<int> free)
    {
        bool roomy = free.Count >= ScalpNodes + TailNodes + BeardNodes;
        return kind switch
        {
            HairKind.Scalp when free.Count >= ScalpNodes + BeardNodes => [.. free.Take(ScalpNodes)],
            HairKind.Beard when free.Count >= ScalpNodes + BeardNodes => [.. free.Skip(free.Count - BeardNodes)],
            HairKind.KhajjitTail or HairKind.CatTail when roomy => [.. free.Skip(ScalpNodes).Take(TailNodes)],
            HairKind.KhajjitTail or HairKind.CatTail when free.Count >= TailNodes => [.. free.Take(TailNodes)],
            _ => null,
        };
    }

    static Vector3 HeadNub(SkeletonRig rig)
    {
        if (rig.Handle("Bip01 HeadNub") is int n) return rig.BindPosition[n];

        return rig.HeadBone >= 0 ? rig.BindPosition[rig.HeadBone] + new Vector3(0, 2, 0) : Vector3.Zero;
    }

    /// <summary>The bones of <see cref="FreeBoneWords"/>, by word then handle, the head excluded.</summary>
    static List<int> FreeBones(SkeletonRig rig)
    {
        var found = new List<int>();
        foreach (string word in FreeBoneWords)
        {
            for (int h = 0; h < rig.ByHandle.Length; h++)
            {
                if (h == rig.HeadBone || found.Contains(h) || rig.ByHandle[h] is not { } bone) continue;

                if (bone.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) found.Add(h);
            }
        }

        return found;
    }
}
