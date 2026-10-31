using System.Numerics;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Simulation.Wounds;

/// <summary>Torso regions; <see cref="Back"/> collects torso hits from behind. Ported from Kenshi Gore Lab's <c>TorsoWounds</c>.</summary>
public enum WoundRegion { Unknown = -1, Chest, Abdomen, Back }

/// <summary>One wound. <see cref="Segment"/>: L upper arm 0, L forearm 1, R upper arm 2, R forearm 3, L thigh 4, L calf 5, R thigh 6, R calf 7; -1 on the torso.</summary>
public readonly record struct Wound(int Segment, float T, Vector3 Side, WoundShape Shape, float Dose, int Hits, int Age);

/// <summary>The visual wounds of one character: one per torso region, and up to <see cref="LimbSlots"/> on the limbs, a near hit deepening a wound and the oldest giving way.</summary>
public sealed class WoundBody
{
    public const int LimbSlots = 8;
    readonly Wound?[] torso = new Wound?[3];
    readonly Wound?[] limbs = new Wound?[LimbSlots];
    int clock;

    public ReadOnlySpan<Wound?> Torso => torso;
    public ReadOnlySpan<Wound?> Limbs => limbs;

    /// <summary>A deep belly wound, the condition for exposed guts.</summary>
    public bool DeepAbdomen => torso[(int)WoundRegion.Abdomen] is { Dose: >= 1.2f, Hits: >= 3 };

    public bool ApplyTorso(WoundRegion region, WoundInput input)
    {
        if (region == WoundRegion.Unknown) return false;
        var old = torso[(int)region];
        var r = WoundPolicy.Evaluate(input with { LocalDose = old?.Dose ?? 0, LocalHits = old?.Hits ?? 0 });
        if (!r.Accepted) return false;
        torso[(int)region] = new Wound(-1, 0.5f, default, r.Shape, r.Dose, r.Hits, ++clock);
        return true;
    }

    public bool ApplyLimb(int segment, float t, Vector3 side, float tilt, WoundInput input)
    {
        if (segment is < 0 or > 7 || !float.IsFinite(t)) return false;
        int target = -1;
        for (int i = 0; i < LimbSlots && target < 0; i++)
            if (limbs[i] is { } w && w.Segment == segment && MathF.Abs(w.T - t) < 0.18f && Vector3.Dot(w.Side, side) > 0.5f) target = i;
        bool merge = target >= 0;
        if (!merge) target = Array.FindIndex(limbs, w => w is null);
        if (target < 0)
        {
            target = 0;
            for (int i = 1; i < LimbSlots; i++) if (limbs[i]!.Value.Age < limbs[target]!.Value.Age) target = i;
        }
        var old = merge ? limbs[target] : null;
        var r = WoundPolicy.Evaluate(input with { LocalDose = old?.Dose ?? 0, LocalHits = old?.Hits ?? 0 });
        if (!r.Accepted) return false;
        // A merged wound keeps its drawn axis; a new one gets a tilt so limb cuts do not all lie on four axes.
        var shape = r.Shape;
        if (old is { } o) shape = shape with { Cosine = o.Shape.Cosine, Sine = o.Shape.Sine };
        else
        {
            float c = MathF.Cos(tilt), s = MathF.Sin(tilt);
            shape = shape with { Cosine = shape.Cosine * c - shape.Sine * s, Sine = shape.Cosine * s + shape.Sine * c };
        }
        limbs[target] = new Wound(segment, old?.T ?? Math.Clamp(t, 0.25f, 0.75f), old?.Side ?? side, shape, r.Dose, r.Hits, ++clock);
        return true;
    }
}

/// <summary>Turns <see cref="CombatEvent"/>s into each character's <see cref="WoundBody"/>. A reader outside the simulation: it changes no game state, and its hashes are visual only.</summary>
public sealed class WoundTracker
{
    readonly Dictionary<CharacterId, WoundBody> bodies = [];

    public WoundBody? Of(CharacterId id) => bodies.GetValueOrDefault(id);
    public void Remove(CharacterId id) => bodies.Remove(id);

    /// <summary>Takes a <see cref="CombatEventKind.Hit"/>; anything else, and a bolt or execution without a cut direction, is ignored. <paramref name="region"/> is the part's torso region when it has one; <paramref name="maximum"/> the part's maximum health.</summary>
    public bool Apply(in CombatEvent e, WoundRegion region, float maximum)
    {
        if (e.Kind != CombatEventKind.Hit || e.CutDirection < 0) return false;
        var body = bodies.TryGetValue(e.Victim, out var known) ? known : new WoundBody();
        // The victim faces (sin yaw, cos yaw); a blow travelling the same way came from behind.
        var forward = new Vector2(MathF.Sin(e.VictimYaw), MathF.Cos(e.VictimYaw));
        bool behind = Vector2.Dot(e.Direction, forward) > 0;
        // The region picks the chart, so a hit from behind on the Back region is a valid front-of-chart hit.
        var input = new WoundInput(e.CutDirection, WoundAttack.Front, false, true, e.Damage.Cut, e.Damage.Blunt, e.Damage.Pierce, maximum);
        bool applied;
        if (e.LimbSlot is >= 0 and <= 3)
        {
            ulong roll = Rng.Hash((ulong)e.Tick, (ulong)(uint)e.Victim.Slot, default, (ulong)e.Part);
            int segment = e.LimbSlot switch { 0 => 0, 1 => 2, 2 => 4, _ => 6 } + (int)(roll & 1);
            float t = ((roll >> 8) & 0xFFFF) / 65535f;
            float tilt = (((roll >> 24) & 0xFFFF) / 65535f * 2 - 1) * 0.44f;
            var local = Vector2.Normalize(new Vector2(forward.Y * e.Direction.X - forward.X * e.Direction.Y, Vector2.Dot(forward, e.Direction)) + new Vector2(0, 1e-6f));
            applied = body.ApplyLimb(segment, t, new Vector3(-local.X, 0, -local.Y), tilt, input);
        }
        else applied = body.ApplyTorso(region is WoundRegion.Unknown ? region : behind ? WoundRegion.Back : region, input);
        if (applied) bodies[e.Victim] = body;
        return applied;
    }
}
