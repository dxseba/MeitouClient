using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Game.Rendering.Frame;

namespace Meitou.FlowingHair;

/// <summary>
/// Bakes a mesh onto its plan's copies: each hanging vertex takes a place along the chain by how far past the root it lies on the plan's axis, and
/// its weight on the carrying bone is split between the two nearest nodes' copies (node 0 is the bone itself); its other weights stay as the file
/// has them. Hair hangs below a root line (scalp: the head's centre; a beard: a quarter of its height below its top); a tail hangs from its top
/// 6 % (the original's tail root band). The copies equal the carrying bone unless the chain moves them, so at rest the mesh is drawn as the file
/// has it.
/// </summary>
static class HairBake
{
    /// <summary>The shortest hanging length (mesh units) worth a chain: short hair stays rigid.</summary>
    const float ScalpMinLength = 1.0f, BeardMinLength = 0.6f, TailMinLength = 0.1f;

    /// <summary>A tail's root band: its top 6 % (the original's <c>TailDynamics</c> and <c>khajjitTailPins</c>).</summary>
    const float TailRootBand = 0.06f;

    public static void Bake(HairPlan plan, IReadOnlyList<ModelPart> parts, SkeletonRig? rig)
    {
        bool[][]? mask = plan.IsTail ? TailProfile.Select(plan.Kind, parts) : null;
        if (plan.IsTail && mask is null) return;

        int dominant = DominantBone(parts, plan.Bones, mask);
        if (dominant < 0 || UsesSlots(parts, plan)) return;

        if (!plan.IsTail) plan.Axis = -Vector3.UnitY;
        else plan.Axis = plan.Kind == HairKind.CatTail ? -Vector3.UnitZ : -Vector3.UnitY;

        if (!Extent(parts, mask, dominant, plan.Axis, out float near, out float far)) return;

        float root = RootOf(plan, rig, dominant, near, far);
        float length = far - root;
        if (length < MinLength(plan.Kind)) return;

        (plan.Root, plan.Length, plan.Dominant) = (root, length, dominant);
        plan.Rest = RestNodes(parts, plan, mask);
        plan.HeadCentre = rig is not null ? (rig.BindPosition[dominant] + plan.HeadNub) / 2 : plan.Rest[0];
        plan.MaxDisplacement = Math.Clamp(length * (plan.Kind == HairKind.Beard ? 0.5f : 0.25f), 0.25f, 1.5f);
        plan.DominantName = rig is not null && (uint)dominant < (uint)rig.ByHandle.Length ? rig.ByHandle[dominant]?.Name ?? "" : "";

        Rewrite(parts, plan, mask);
        plan.Baked = true;
    }

    static float MinLength(HairKind kind) => kind switch
    {
        HairKind.Scalp => ScalpMinLength,
        HairKind.Beard => BeardMinLength,
        _ => TailMinLength,
    };

    /// <summary>Whether vertex <paramref name="i"/> of part <paramref name="p"/> hangs: in the tail's mask, or mostly on the carrying bone.</summary>
    static bool Hangs(bool[][]? mask, int p, int i, in Vertex v, int dominant) =>
        mask is not null ? mask[p][i] : WeightOn(in v, dominant) >= 0.5f;

    /// <summary>The hanging vertices' nearest and farthest places along the axis.</summary>
    static bool Extent(IReadOnlyList<ModelPart> parts, bool[][]? mask, int dominant, Vector3 axis, out float near, out float far)
    {
        (near, far) = (float.MaxValue, float.MinValue);
        for (int p = 0; p < parts.Count; p++)
        {
            var vertices = parts[p].Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!Hangs(mask, p, i, in vertices[i], dominant)) continue;

                float s = Vector3.Dot(vertices[i].Position, axis);
                (near, far) = (MathF.Min(near, s), MathF.Max(far, s));
            }
        }

        return near <= far;
    }

    /// <summary>The root's place along the axis (axis -Y: minus the height).</summary>
    static float RootOf(HairPlan plan, SkeletonRig? rig, int dominant, float near, float far)
    {
        if (plan.IsTail) return near + TailRootBand * (far - near);

        if (plan.Kind == HairKind.Beard) return near + 0.25f * (far - near);

        float head = rig is not null ? rig.BindPosition[dominant].Y : -near - 2;
        return MathF.Max(near, -(head + plan.HeadNub.Y) / 2);
    }

    /// <summary>The bone holding most of the hanging vertices' skin weight; -1 when they are unskinned.</summary>
    static int DominantBone(IReadOnlyList<ModelPart> parts, int bones, bool[][]? mask)
    {
        var total = new double[256];
        for (int p = 0; p < parts.Count; p++)
        {
            var vertices = parts[p].Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (mask is not null && !mask[p][i]) continue;

                for (int k = 0; k < 4; k++)
                {
                    if (vertices[i].Weights[k] > 0) total[(vertices[i].Bones >> (8 * k)) & 0xFF] += vertices[i].Weights[k];
                }
            }
        }

        int best = -1;
        for (int b = 0; b < bones; b++)
        {
            if (total[b] > 0 && (best < 0 || total[b] > total[best])) best = b;
        }

        return best;
    }

    static bool UsesSlots(IReadOnlyList<ModelPart> parts, HairPlan plan)
    {
        foreach (var part in parts)
        {
            foreach (ref readonly var v in part.Vertices.AsSpan())
            {
                for (int k = 0; k < 4; k++)
                {
                    if (!(v.Weights[k] > 0)) continue;

                    int bone = (int)((v.Bones >> (8 * k)) & 0xFF);
                    if (bone >= plan.Bones || plan.Slots.Contains(bone)) return true;
                }
            }
        }

        return false;
    }

    static float WeightOn(in Vertex v, int bone)
    {
        float w = 0;
        for (int k = 0; k < 4; k++)
        {
            if (((v.Bones >> (8 * k)) & 0xFF) == bone && v.Weights[k] > 0) w += v.Weights[k];
        }

        return w;
    }

    /// <summary>A vertex's place along the chain: 0 at or before the root, the chain's node count at the far end.</summary>
    static float Along(HairPlan plan, Vector3 position) =>
        plan.Nodes * Math.Clamp((Vector3.Dot(position, plan.Axis) - plan.Root) / plan.Length, 0, 1);

    /// <summary>Node j at its place on the axis, across at the centre of the hanging vertices near it (all of them when none is).</summary>
    static Vector3[] RestNodes(IReadOnlyList<ModelPart> parts, HairPlan plan, bool[][]? mask)
    {
        int n = plan.Nodes;
        var sum = new Vector3[n + 1];
        var count = new int[n + 1];
        var all = Vector3.Zero;
        int allCount = 0;
        for (int p = 0; p < parts.Count; p++)
        {
            var vertices = parts[p].Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!Hangs(mask, p, i, in vertices[i], plan.Dominant)) continue;

                var position = vertices[i].Position;
                var across = position - plan.Axis * Vector3.Dot(position, plan.Axis);
                int bin = (int)MathF.Round(Along(plan, position));
                (sum[bin], count[bin], all, allCount) = (sum[bin] + across, count[bin] + 1, all + across, allCount + 1);
            }
        }

        var centre = allCount > 0 ? all / allCount : Vector3.Zero;
        var rest = new Vector3[n + 1];
        for (int j = 0; j <= n; j++)
        {
            var across = count[j] > 0 ? sum[j] / count[j] : centre;
            rest[j] = across + plan.Axis * (plan.Root + plan.Length * j / n);
        }

        return rest;
    }

    /// <summary>Splits each hanging vertex's carrying-bone weight between the copies of the two chain nodes around it.</summary>
    static void Rewrite(IReadOnlyList<ModelPart> parts, HairPlan plan, bool[][]? mask)
    {
        Span<(int Bone, float Weight)> slots = stackalloc (int, float)[8];
        for (int p = 0; p < parts.Count; p++)
        {
            var vertices = parts[p].Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                ref var v = ref vertices[i];
                if (mask is not null && !mask[p][i]) continue;

                float u = Along(plan, v.Position);
                if (u <= 0) continue;

                int count = Split(in v, plan, u, slots);
                if (count > 0) Write(ref v, slots[..count]);
            }
        }
    }

    static int Split(in Vertex v, HairPlan plan, float u, Span<(int Bone, float Weight)> slots)
    {
        int k = Math.Min((int)u, plan.Nodes - 1);
        float f = u - k;
        int n = 0;
        bool carried = false;
        for (int s = 0; s < 4; s++)
        {
            float w = v.Weights[s];
            if (!(w > 0)) continue;

            int bone = (int)((v.Bones >> (8 * s)) & 0xFF);
            if (bone != plan.Dominant)
            {
                n = Add(slots, n, bone, w);
                continue;
            }

            carried = true;
            n = Add(slots, n, NodeBone(plan, k), w * (1 - f));
            n = Add(slots, n, NodeBone(plan, k + 1), w * f);
        }

        return carried ? n : 0;
    }

    static int NodeBone(HairPlan plan, int node) => node == 0 ? plan.Dominant : plan.Slots[node - 1] + plan.Bones;

    static int Add(Span<(int Bone, float Weight)> slots, int n, int bone, float weight)
    {
        if (!(weight > 0)) return n;

        for (int i = 0; i < n; i++)
        {
            if (slots[i].Bone != bone) continue;

            slots[i].Weight += weight;
            return n;
        }

        slots[n] = (bone, weight);
        return n + 1;
    }

    /// <summary>The four heaviest slots, normalised.</summary>
    static void Write(ref Vertex v, Span<(int Bone, float Weight)> slots)
    {
        slots.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
        int used = Math.Min(4, slots.Length);
        float total = 0;
        for (int i = 0; i < used; i++) total += slots[i].Weight;

        (v.Bones, v.Weights) = (0, Vector4.Zero);
        for (int i = 0; i < used; i++)
        {
            v.Bones |= (uint)slots[i].Bone << (8 * i);
            v.Weights[i] = slots[i].Weight / total;
        }
    }
}
