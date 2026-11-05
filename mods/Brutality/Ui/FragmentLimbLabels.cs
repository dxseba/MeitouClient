using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Ui;

/// <summary>Names actual articulated limb islands within a cut mesh, using the nearest surviving joint to identify the part.</summary>
internal static class FragmentLimbLabels
{
    internal readonly record struct Anchor(int Bone, Vector3 CentreInBone, string Name, bool Root)
    {
        public Vector3? Position(BonePose pose, Matrix4x4 placement) =>
            (uint)Bone < (uint)pose.Set.Length && pose.Set[Bone]
                ? Vector3.Transform(pose.Position[Bone] + Vector3.Transform(CentreInBone, pose.Rotation[Bone]), placement)
                : null;
    }
    readonly record struct Limb(int Slot, int Segment);
    static readonly ConditionalWeakTable<RagdollDefinition, Anchor[]> cached = new();

    internal static IReadOnlyList<Anchor> For(RagdollDefinition definition, int[] components) =>
        cached.GetValue(definition, key => Build(key, components));

    static Anchor[] Build(RagdollDefinition definition, int[] components)
    {
        int root = components[definition.Bodies[definition.Root].Bone];
        var limbs = new Dictionary<int, (Limb Limb, RagdollBody Body)?>();
        foreach (var body in definition.Bodies)
        {
            int component = components[body.Bone];
            var part = Classify(body.Name);
            if (!limbs.TryGetValue(component, out var current))
                limbs.Add(component, part is { } first ? (first, body) : null);
            else if (current is not { } known || part is not { } limb || limb.Slot != known.Limb.Slot)
                limbs[component] = null;
            else if (limb.Segment < known.Limb.Segment)
                limbs[component] = (limb, body);
        }
        var anchors = new List<Anchor>();
        foreach (var (component, part) in limbs)
        {
            if (part is not { } known) continue;
            var (limb, body) = known;
            string name = (limb.Slot % 2 == 0 ? "Left " : "Right ") + ((limb.Slot < 2, limb.Segment) switch
            {
                (true, 0) => "arm", (true, 1) => "forearm", (true, _) => "hand",
                (false, 0) => "leg", (false, 1) => "lower leg", _ => "foot",
            });
            anchors.Add(new(body.Bone, body.BoneInBody.Inverse().Position, name, component == root));
        }
        return anchors.ToArray();
    }

    static Limb? Classify(string name)
    {
        int side = name.Contains(" L ", StringComparison.Ordinal) ? 0 : name.Contains(" R ", StringComparison.Ordinal) ? 1 : -1;
        if (side < 0) return null;
        if (name.Contains("Clavicle", StringComparison.Ordinal) || name.Contains("UpperArm", StringComparison.Ordinal)) return new(side, 0);
        if (name.Contains("Forearm", StringComparison.Ordinal)) return new(side, 1);
        if (name.Contains("Hand", StringComparison.Ordinal) || name.Contains("Finger", StringComparison.Ordinal)) return new(side, 2);
        if (name.Contains("Thigh", StringComparison.Ordinal)) return new(side + 2, 0);
        if (name.Contains("Calf", StringComparison.Ordinal)) return new(side + 2, 1);
        if (name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal)) return new(side + 2, 2);
        return null;
    }
}
