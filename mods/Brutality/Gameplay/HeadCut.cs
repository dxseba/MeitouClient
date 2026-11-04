using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using System.Numerics;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public enum HeadCutMode { NeckDetach = 0, AttachedSplit = 1, DetachedSplit = 2 }

/// <summary>A fixed plane in normalized head bounds: q = (bind point - bounds centre) / half extent. Positive is the falling side.</summary>
public readonly record struct HeadCutState(Vector4 Plane, HeadCutMode Mode)
{
    public bool IsValid
    {
        get
        {
            if (Mode is < HeadCutMode.NeckDetach or > HeadCutMode.DetachedSplit
                || !float.IsFinite(Plane.X) || !float.IsFinite(Plane.Y) || !float.IsFinite(Plane.Z) || !float.IsFinite(Plane.W)) return false;
            float length = new Vector3(Plane.X, Plane.Y, Plane.Z).LengthSquared();
            float radius = MathF.Abs(Plane.X) + MathF.Abs(Plane.Y) + MathF.Abs(Plane.Z);
            return MathF.Abs(length - 1) <= .001f && MathF.Abs(Plane.W) < radius;
        }
    }

    internal void Hash(ref StateHasher hash)
    {
        hash.Add(0x48435554);
        hash.Add((int)Mode);
        hash.Add(Plane.X);
        hash.Add(Plane.Y);
        hash.Add(Plane.Z);
        hash.Add(Plane.W);
    }
}

/// <summary>Deterministic visual choices for a qualifying destroyed head; this policy neither applies damage nor decides death.</summary>
public static class HeadCutPolicy
{
    /// <summary>Separate from every combat and severe-wound roll, so selecting a head shape changes no hit or chance.</summary>
    public static ulong VisualSeedFor(ulong worldSeed, CharacterId victim, CharacterId attacker, ulong counter) =>
        Rng.Hash(worldSeed, Rng.Key(victim) ^ Rng.Mix(Rng.Key(attacker)), GoreRandom.HeadCutVisualPurpose, counter);

    public static HeadCutState? Candidate(float brutality, CombatWoundMechanism mechanism, int cutDirection, float cut,
        ulong visualSeed, Vector2 travel = default, float victimYaw = 0)
    {
        if (!BrutalityPolicy.CanDecapitate(brutality, mechanism) || !float.IsFinite(cut) || cut <= 0) return null;
        float choice = Rng.Float(visualSeed), tilt = Rng.Float(Rng.Mix(visualSeed));
        float offset = Rng.Float(Rng.Mix(visualSeed ^ 0x484541444F464653UL));
        var across = Across(travel, victimYaw, visualSeed);

        if (cutDirection is 1 or 7)
        {
            bool complete = cut >= 70 && choice < .20f;
            float rise = choice < .55f ? 0 : .25f + tilt * .45f;
            return Plane(new(across.X, rise, across.Y), (offset - .5f) * .35f,
                complete ? HeadCutMode.DetachedSplit : HeadCutMode.AttachedSplit);
        }

        if (cutDirection is 2 or 3 or 9 && choice >= .45f)
        {
            float side = cutDirection == 3 ? -1 : 1;
            var normal = new Vector3(across.X * side * (.25f + tilt * .65f), 1, across.Y * side * (.25f + tilt * .65f));
            return Plane(normal, -.05f - offset * .30f, HeadCutMode.AttachedSplit);
        }

        // A low neck plane includes continuous oblique angles; executions and unsupported technique axes retain whole-head detachment.
        float neckTilt = .04f + tilt * .20f;
        return Plane(new(across.X * neckTilt, 1, across.Y * neckTilt), .84f + offset * .10f, HeadCutMode.NeckDetach);
    }

    /// <summary>Explicit debug shape; invalid names return no candidate. This does not grant real skill or alter medical death.</summary>
    public static HeadCutState? Preview(string style, int cutDirection = 1, ulong visualSeed = 0, Vector2 travel = default, float victimYaw = 0)
    {
        var across = Across(travel, victimYaw, visualSeed);
        return style.ToLowerInvariant() switch
        {
            "auto" => Candidate(100, CombatWoundMechanism.Slash, cutDirection, 300, visualSeed, travel, victimYaw),
            "neck" => Plane(new(across.X * .16f, 1, across.Y * .16f), .90f, HeadCutMode.NeckDetach),
            "transverse" => Plane(Vector3.UnitY, -.10f, HeadCutMode.AttachedSplit),
            "oblique" => Plane(new(across.X * .7f, .7f, across.Y * .7f), 0, HeadCutMode.AttachedSplit),
            "vertical" => Plane(new(across.X, 0, across.Y), 0, HeadCutMode.AttachedSplit),
            "split" => Plane(new(across.X, 0, across.Y), 0, HeadCutMode.DetachedSplit),
            _ => null,
        };
    }

    static HeadCutState Plane(Vector3 normal, float offset, HeadCutMode mode)
    {
        float length = normal.Length();
        return new(new(normal / length, offset / length), mode);
    }

    static Vector2 Across(Vector2 travel, float victimYaw, ulong seed)
    {
        float length = travel.LengthSquared();
        if (!float.IsFinite(length) || length < 1e-8f || !float.IsFinite(victimYaw)) travel = -Vector2.UnitY;
        else
        {
            travel /= MathF.Sqrt(length);
            float sine = MathF.Sin(victimYaw), cosine = MathF.Cos(victimYaw);
            travel = new(cosine * travel.X - sine * travel.Y, sine * travel.X + cosine * travel.Y);
        }

        float angle = (Rng.Float(Rng.Mix(seed ^ 0x48454144414E474CUL)) * 2 - 1) * MathF.PI / 12;
        var across = new Vector2(travel.Y, -travel.X);
        return new(across.X * MathF.Cos(angle) - across.Y * MathF.Sin(angle),
            across.X * MathF.Sin(angle) + across.Y * MathF.Cos(angle));
    }
}
