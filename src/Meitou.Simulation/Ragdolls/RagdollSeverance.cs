using System.Numerics;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Ragdolls;

/// <summary>Partitions ordinary head and limb losses into the remaining body and a free articulated part.</summary>
public static class RagdollSeverance
{
    /// <summary>Only Head/Jaw actors are sliced; an attached negative half keeps its original neck connection.</summary>
    public static (RagdollDefinition? Remaining, RagdollDefinition? Positive, RagdollDefinition? Negative) SplitHead(
        RagdollDefinition source, Vector4 plane, HeadCutMode mode)
    {
        float length = new Vector3(plane.X, plane.Y, plane.Z).Length();
        if (!float.IsFinite(length) || length < 1e-6f || !float.IsFinite(plane.W)) return (null, null, null);
        plane /= length;
        bool attached = mode == HeadCutMode.AttachedSplit;
        var remaining = RagdollCut.Filter(source,
            body => !Matches(body.Name, -1) ? body : attached ? RagdollCut.Clip(body, -plane) : null,
            moveAnchor: joint =>
            {
                var a = source.Bodies[joint.A];
                var b = source.Bodies[joint.B];
                if (!attached || !Matches(a.Name, -1) && !Matches(b.Name, -1)) return null;
                return IntoHeadHalf(source, joint, -plane);
            });
        var head = RagdollCut.Filter(source, body => Matches(body.Name, -1) ? body : null);
        var positive = Half(plane);
        var negative = mode == HeadCutMode.DetachedSplit ? Half(-plane) : null;
        return (remaining, positive, negative);

        RagdollDefinition? Half(Vector4 retained) => head is null ? null : RagdollCut.Filter(head,
            body => RagdollCut.Clip(body, retained), moveAnchor: joint => IntoHeadHalf(head, joint, retained));
    }

    static Vector3? IntoHeadHalf(RagdollDefinition source, RagdollJoint joint, Vector4 retained)
    {
        var a = source.Bodies[joint.A].Bind.Apply(joint.OffsetA);
        var b = source.Bodies[joint.B].Bind.Apply(joint.OffsetB);
        if (Vector4.Dot(retained, new(a, 1)) >= .0001f && Vector4.Dot(retained, new(b, 1)) >= .0001f) return null;
        var anchor = (a + b) * .5f;
        float distance = Vector4.Dot(retained, new(anchor, 1));
        return anchor + new Vector3(retained.X, retained.Y, retained.Z) * MathF.Max(0, .0001f - distance);
    }

    /// <summary>
    /// Part -1 is the head, 0/1 the left/right arm, 2/3 the left/right leg. The neck stays with the chest.
    /// Height zero removes the whole named limb; a positive bind-space height removes only its lower portion.
    /// Both definitions retain original bone handles. Invalid parts or heights return two null definitions.
    /// </summary>
    public static (RagdollDefinition? Remaining, RagdollDefinition? Detached) Split(RagdollDefinition source, int part, float height = 0)
    {
        if (part is < -1 or > 3 || !float.IsFinite(height) || height < 0) return (null, null);
        bool sliced = part >= 0 && height > 0;
        var above = new Vector4(0, 1, 0, -height);
        var below = -above;
        var remaining = RagdollCut.Filter(source, body => !Matches(body.Name, part) ? body : sliced ? RagdollCut.Clip(body, above) : null,
            joint => !sliced || Inside(source, joint, part, height, 1));
        var detached = RagdollCut.Filter(source, body => !Matches(body.Name, part) ? null : sliced ? RagdollCut.Clip(body, below) : body,
            joint => !sliced || Inside(source, joint, part, height, -1));
        return (remaining, detached);
    }

    static bool Inside(RagdollDefinition source, RagdollJoint joint, int part, float height, int side)
    {
        var a = source.Bodies[joint.A];
        var b = source.Bodies[joint.B];
        // The cut is local to its limb: unrelated spine, head and other-limb joints are not intersected.
        if (!Matches(a.Name, part) && !Matches(b.Name, part)) return true;
        return (a.Bind.Apply(joint.OffsetA).Y - height) * side >= -0.0001f
            && (b.Bind.Apply(joint.OffsetB).Y - height) * side >= -0.0001f;
    }

    static bool Matches(string name, int part)
    {
        if (part == -1) return name is "Bip01 Head" or "Bip01 Jaw";
        string side = part % 2 == 0 ? " L " : " R ";
        if (!name.Contains(side, StringComparison.Ordinal)) return false;
        return part < 2
            ? name.Contains("UpperArm", StringComparison.Ordinal) || name.Contains("Forearm", StringComparison.Ordinal) || name.Contains("Hand", StringComparison.Ordinal) || name.Contains("Finger", StringComparison.Ordinal)
            : name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal) || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal);
    }
}
