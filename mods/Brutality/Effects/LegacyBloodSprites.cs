using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Brutality.Physics;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

/// <summary>An empty renderer created solely for DLL liquid/electrical drawing does not enable the host's optional legacy sprites.</summary>
internal sealed class NativeSpriteEligibility
{
    public bool NativeRequested { get; private set; } = true;
    bool prepared;
    public void MarkModOnly() { NativeRequested = false; prepared = false; }
    public void RequestNative(bool requested) { if (requested) NativeRequested = true; }
    public void PrepareOnce(Action prepare)
    {
        if (!NativeRequested || prepared) return;
        prepare(); prepared = true;
    }
}

internal readonly record struct LegacyBloodBurst(string System, Vector3 Position, Quaternion Orientation,
    float Seconds, float Size, float LifeScale, Vector3? Recolour);

internal static class LegacyBloodSprites
{
    /// <summary>Every legacy blood caller, including native neck pulses and debris landings, shares the same container eligibility.</summary>
    internal static bool AllowNativeBurst(string system, NativeSpriteEligibility? eligibility, bool noParticles)
    {
        bool blood = system.Equals("HackBlood-system", StringComparison.OrdinalIgnoreCase)
            || system.Equals("Bloodsplash01", StringComparison.OrdinalIgnoreCase);
        return !blood || !noParticles && eligibility?.NativeRequested != false;
    }
    [ThreadStatic] static bool suppressNeckSpurt;
    internal static bool NeckSpurtSuppressed => suppressNeckSpurt;
    internal static bool AllowNeckSpurt(CharacterAppearance appearance) => !suppressNeckSpurt && !appearance.Race.GetBool("is robot");

    /// <summary>Pending physical heads keep the native fallback visual, without starting a second neck effect. Restore from a Harmony finalizer.</summary>
    internal static bool BeginHeadCut(bool ownsPhysicalHead, bool publishedGeometry, out bool previousSuppression)
    {
        previousSuppression = suppressNeckSpurt;
        suppressNeckSpurt |= ownsPhysicalHead;
        return !ownsPhysicalHead || !publishedGeometry;
    }
    internal static void EndHeadCut(bool previousSuppression) => suppressNeckSpurt = previousSuppression;

    /// <summary>The pinned host skips its old head/spurt path only after the shown advanced physics snapshot owns a head fragment.</summary>
    internal static bool OwnsShownHead(GorePresentationFrame? frame, CharacterId owner, long shownTick, bool advanced)
    {
        if (!advanced || frame is null || frame.Tick > shownTick || !frame.Characters.TryGetValue(owner, out var actor)
            || actor.Character != owner || actor.State.Head is not { IsValid: true }) return false;
        return actor.Detached.Any(piece => piece.StableKey is 2 or 3 && piece.Bones.AbsolutePositions
            && piece.Bones.Position.Length > 0 && piece.Bones.Rotation.Length == piece.Bones.Position.Length);
    }

    internal static void Emit(in ImpactEffectEvent impact, double born, double now, bool particlesEnabled,
        NativeSpriteEligibility eligibility, Action<LegacyBloodBurst> emit)
    {
        double age = now - born;
        if (!particlesEnabled || !eligibility.NativeRequested || (impact.Effects & ImpactEffectKind.OrganicSpray) == 0
            || !double.IsFinite(age) || age < 0 || age > 1) return;
        var random = new Random(unchecked((int)(impact.Seed ^ impact.Seed >> 32)));
        float force = MathF.Min((impact.Hit.Damage.Cut + impact.Hit.Damage.Pierce) / 20, 1);
        var flat = new Vector2(impact.Hit.Travel.X, impact.Hit.Travel.Z);
        var along = flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
        var from = impact.Hit.VictimPosition + new Vector3(0, 7 + random.NextSingle() * 7, 0);
        Vector3? recolour = impact.BloodColour == 0x800000 ? null : new Vector3(impact.BloodColour >> 16 & 255,
            impact.BloodColour >> 8 & 255, impact.BloodColour & 255) / 128;
        for (int i = 0; i < 1 + (int)(force * 3); i++)
        {
            var spray = Vector3.Normalize(new Vector3(along.X + random.NextSingle() - .5f,
                .3f + random.NextSingle() * .6f, along.Y + random.NextSingle() - .5f));
            var axis = Vector3.Cross(Vector3.UnitZ, spray); float length = axis.Length();
            var rotation = length < 1e-5f ? Vector3.Dot(Vector3.UnitZ, spray) > 0 ? Quaternion.Identity
                : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)
                : Quaternion.CreateFromAxisAngle(axis / length, MathF.Atan2(length, Vector3.Dot(Vector3.UnitZ, spray)));
            emit(new("HackBlood-system", from, rotation, .1f + force * .2f, 1, 1.8f, recolour));
        }
    }
}
