using System.Numerics;
using System.Text.Json.Serialization;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public readonly record struct BodyWoundEntry(int Region, Wound Mark);
public sealed record BodyWoundState(IReadOnlyList<BodyWoundEntry> Entries, int BellyHits = 0)
{
    public static BodyWoundState Empty { get; } = new(Array.Empty<BodyWoundEntry>());
    [JsonIgnore] public bool HasAny => Entries.Count > 0;
    [JsonIgnore] public bool IsValid => Entries is not null && Entries.Count <= WoundBody.LimbSlots + 3 && BellyHits >= 0
        && Entries.All(Valid) && Entries.Where(entry => entry.Region >= 0).Select(entry => entry.Region).Distinct().Count() == Entries.Count(entry => entry.Region >= 0);
    static bool Valid(BodyWoundEntry entry)
    {
        var wound = entry.Mark; var shape = wound.Shape;
        bool Finite(float value) => float.IsFinite(value);
        return entry.Region is >= -1 and <= 2 && (entry.Region == -1 ? WoundSegments.IsValid(wound.Segment) : wound.Segment == -1)
            && Finite(wound.T) && wound.T is >= 0 and <= 1 && float.IsFinite(wound.Side.LengthSquared())
            && (entry.Region >= 0 || wound.Side.LengthSquared() is >= .5f and <= 1.5f)
            && Finite(shape.Cosine) && Finite(shape.Sine) && MathF.Abs(shape.Cosine * shape.Cosine + shape.Sine * shape.Sine - 1) <= .01f
            && Finite(shape.Aspect) && shape.Aspect is >= .08f and <= 1 && Finite(shape.Depth) && shape.Depth is >= 0 and <= 1
            && shape.Kind is >= WoundKind.None and <= WoundKind.Crush
            && Finite(wound.Dose) && wound.Dose is > 0 and <= WoundPolicy.MaxDose && wound.Hits is >= 1 and <= WoundPolicy.MaxHits
            && wound.Age >= 0 && Finite(wound.Size) && wound.Size is >= .1f and <= 4 && wound.Born >= 0;
    }
    public ulong StateKey()
    {
        if (Entries.Count == 0 && BellyHits == 0) return 0;
        var hash = new StateHasher(); hash.Add(BellyHits); hash.Add(Entries.Count);
        foreach (var entry in Entries)
        {
            var wound = entry.Mark; var shape = wound.Shape;
            hash.Add(entry.Region); hash.Add(wound.Segment); hash.Add(wound.T); hash.Add(wound.Side);
            hash.Add(shape.Cosine); hash.Add(shape.Sine); hash.Add(shape.Aspect); hash.Add(shape.Depth); hash.Add((int)shape.Kind); hash.Add(shape.WeaponCategory);
            hash.Add(wound.Dose); hash.Add(wound.Hits); hash.Add(wound.Age); hash.Add(wound.Size); hash.Add(wound.Born);
        }
        return hash.Value;
    }
}

public sealed partial class WoundBody
{
    public BodyWoundState Snapshot()
    {
        if (snapshot is not null) return snapshot;
        var entries = new List<BodyWoundEntry>();
        for (int region = 0; region < torso.Length; region++) if (torso[region] is { } wound) entries.Add(new(region, wound));
        foreach (var wound in limbs) if (wound is { } mark) entries.Add(new(-1, mark));
        return snapshot = entries.Count == 0 && BellyHits == 0 ? BodyWoundState.Empty : new(Array.AsReadOnly(entries.ToArray()), BellyHits);
    }
    public static WoundBody From(BodyWoundState? saved)
    {
        var body = new WoundBody();
        if (saved is not { IsValid: true }) return body;
        foreach (var entry in saved.Entries) body.Restore(entry.Region, entry.Mark);
        body.BellyHits = saved.BellyHits; return body;
    }

    /// <summary>The pinned event-placement formulas adapted to the immutable post-armour observer payload.</summary>
    public bool Apply(in CombatHit hit, BodyPartTemplate part, float maximum, CombatWoundMechanism mechanism, int weaponCategory) =>
        Apply(hit, part, maximum, mechanism, weaponCategory, detailedSites: false);

    internal bool Apply(in CombatHit hit, BodyPartTemplate part, float maximum, CombatWoundMechanism mechanism, int weaponCategory,
        bool detailedSites)
    {
        if (hit.Source != CombatHitSource.Melee || hit.CutDirection < 0 || !hit.VictimWasAlive) return false;
        Now = hit.Tick;
        int cut = hit.CutDirection switch { 7 => 1, 9 => 3, var direction => direction };
        var forward = new Vector2(MathF.Sin(hit.VictimYaw), MathF.Cos(hit.VictimYaw));
        var direction2 = new Vector2(hit.Travel.X, hit.Travel.Z);
        bool behind = Vector2.Dot(direction2, forward) > 0;
        bool crush = mechanism == CombatWoundMechanism.Crush, thrust = mechanism == CombatWoundMechanism.Thrust;
        if (thrust) cut = 4; else if (crush && cut == 4) cut = 0;
        float cutting = crush ? 0 : hit.Damage.Cut > 0 ? hit.Damage.Cut + hit.Damage.Blunt * .5f : 0;
        float blunt = crush ? hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce : hit.Damage.Blunt;
        float received = crush ? blunt : hit.Damage.Cut + hit.Damage.Pierce;
        float size = WoundPolicy.VisualSize(received, maximum, mechanism, weaponCategory);
        var input = new WoundInput(cut, WoundAttack.Front, false, true, cutting, blunt, crush ? 0 : hit.Damage.Pierce,
            maximum * .3f, Size: size, WeaponCategory: weaponCategory);
        if (part.LimbSlot is >= 0 and <= 3 and var limb)
        {
            ulong roll = Rng.Hash((ulong)hit.Tick, (ulong)(uint)hit.Victim.Slot, default, (ulong)hit.Part);
            int segment = (limb switch { 0 => 0, 1 => 2, 2 => 4, _ => 6 }) + (int)(roll & 1);
            float t = ((roll >> 8) & 0xFFFF) / 65535f, tilt = (((roll >> 24) & 0xFFFF) / 65535f * 2 - 1) * .44f;
            if (detailedSites && !crush && segment is 1 or 3 && (roll >> 40 & 3) == 0)
            {
                bool distal = (roll >> 42 & 3) == 0;
                segment = limb == 0
                    ? distal ? WoundSegments.LeftDistalHand : WoundSegments.LeftHand
                    : distal ? WoundSegments.RightDistalHand : WoundSegments.RightHand;
            }
            return ApplyLimb(segment, t, ContactSide(forward, direction2), tilt, input);
        }
        var region = part.StringId == "101-gamedata.quack" ? WoundRegion.Chest : part.StringId == GorePartIds.Stomach ? WoundRegion.Abdomen : WoundRegion.Unknown;
        if (region == WoundRegion.Unknown)
        {
            ulong roll = Rng.Hash((ulong)hit.Tick, (ulong)(uint)hit.Victim.Slot, default, (ulong)hit.Part);
            float t = .3f + ((roll >> 8) & 0xFF) / 255f * .5f;
            if (detailedSites && !crush && part.Type == BodyPartType.Head && !behind && (roll & 3) != 0)
            {
                int segment = cut == 5 || (roll & 3) == 3 ? WoundSegments.Jaw : WoundSegments.Cheek;
                var side = ContactSide(forward, direction2);
                if (MathF.Abs(side.X) < .18f)
                    side = Vector3.Normalize(new((roll & 4) == 0 ? -.43f : .43f, 0, side.Z));
                return ApplyLimb(segment, t, side, 0, input);
            }
            return ApplyLimb(8, t, new(0, 0, behind ? -1 : 1), 0, input);
        }
        ulong placement = Rng.Hash((ulong)hit.Tick, (ulong)(uint)hit.Victim.Slot, default, (ulong)hit.Part + 77);
        float across = ((placement >> 8) & 0xFFFF) / 65535f * 2 - 1, height = ((placement >> 24) & 0xFFFF) / 65535f;
        float along = region == WoundRegion.Chest ? .5f + height * .42f : .12f + height * .42f;
        bool applied = ApplyLimb(9, along, Vector3.Normalize(new(across * 2.5f, 0, behind ? -1 : 1)), 0, input);
        if (applied && region == WoundRegion.Abdomen && !crush && cutting + hit.Damage.Pierce >= 12.6f) BellyHits++;
        return applied;
    }

    static Vector3 ContactSide(Vector2 forward, Vector2 travel)
    {
        var local = new Vector2(forward.X * travel.Y - forward.Y * travel.X, -Vector2.Dot(forward, travel));
        return local.LengthSquared() > 1e-12f ? Vector3.Normalize(new(local.X, 0, local.Y)) : Vector3.UnitZ;
    }
}
