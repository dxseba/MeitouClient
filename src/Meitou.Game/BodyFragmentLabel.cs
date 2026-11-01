using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Wounds;

namespace Meitou.Game;

enum BodyFragmentLabelKind { Body, TorsoHalf, Detached }

/// <summary>A label keeps its rendered piece identity, even when stacking moves its text away from the mesh.</summary>
readonly record struct BodyFragmentLabel(long Key, CharacterId Owner, string Name, Vector3 Position, BodyFragmentLabelKind Kind, int TorsoSide = 0, int Component = -1)
{
    public bool Interactive => Kind is BodyFragmentLabelKind.Body or BodyFragmentLabelKind.TorsoHalf;
    public bool SamePiece(in BodyFragmentLabel other) => Key == other.Key && Owner == other.Owner && Component == other.Component;
}

static class BodyFragmentNames
{
    public static string Torso(in TorsoBisectionState cut, int side, bool? leftIsPositiveX)
    {
        var n = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float keep = side == 1 ? 1 : -1;
        if (!cut.FullBody || MathF.Abs(n.Y) >= 0.75f && MathF.Abs(n.Y) > MathF.Abs(n.X) * 1.5f && MathF.Abs(n.Y) > MathF.Abs(n.Z) * 1.5f)
            return n.Y * keep > 0 ? "Upper body" : "Lower body";
        if (MathF.Abs(n.Y) < 0.35f && MathF.Abs(n.X) >= 0.80f && leftIsPositiveX is { } positive)
            return (n.X * keep > 0) == positive ? "Left body half" : "Right body half";
        if (MathF.Abs(n.Y) < 0.35f && MathF.Abs(n.Z) >= 0.80f)
            return n.Z * keep > 0 ? "Front body half" : "Back body half";
        return side == 1 ? "Body fragment 2" : "Body fragment 1";
    }

    public static string Part(int part) => part switch
    {
        -1 => "Head", 0 => "Left arm", 1 => "Right arm", 2 => "Left leg", 3 => "Right leg", 7 => "Jaw", _ => "Body fragment",
    };
}
