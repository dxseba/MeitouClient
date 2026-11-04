using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Game;

sealed partial class GameHost
{
    // Current posed contacts are available only after character preparation; bowel uploads share that frame's batch.
    void UpdatePosedGuts()
    {
        if (!GoreEnabled) return;
        var geometry = config.AdvancedRagdolls ? gpu.Debris?.BowelRig() : null;
        foreach (var (id, fall) in gutFalls)
        {
            if (!fall.Section) continue;
            if (fall.Positioned && !GutEmergence.AttachedAt(fall.KeepAttached, fall.Age))
            {
                gutPhysics?.ReleaseAttachment(fall.RigKey);
                fall.AttachmentBroken = true;
                continue;
            }
            var contact = gpu.Characters?.CutContact(TorsoDrawKey(id, 0));
            if (contact is null) continue;
            if (!fall.Positioned) PlaceSectionGut(fall, contact.Value, geometry);
            if (fall.AttachmentBroken) continue;
            var anchor = GutEmergence.Anchor(contact.Value, fall.InsideDepth, fall.Age);
            if (fall.Physical)
            {
                if (gutPhysics?.MoveAttachment(fall.RigKey, anchor) != true) fall.AttachmentBroken = true;
            }
            else
            {
                var pin = fall.AttachmentLocal;
                fall.Position = anchor - Vector3.Transform(pin, fall.Rotation);
            }
        }
    }

    void PlaceSectionGut(GutFall fall, CharacterCutContact contact, BowelRigGeometry? geometry)
    {
        fall.Positioned = true;
        var tangent = fall.Velocity - contact.Normal * Vector3.Dot(fall.Velocity, contact.Normal);
        if (tangent.LengthSquared() < 0.01f)
            tangent = Vector3.Cross(contact.Normal, MathF.Abs(contact.Normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        fall.Velocity = fall.Age >= GutEmergence.FeedAge ? Vector3.Zero : tangent * 0.8f + contact.Normal * 0.35f;
        fall.InsideDepth = Math.Clamp(fall.HalfExtent.Length() * 0.3f, 0.4f, 0.8f);
        var pin = gpu.Debris?.BowelRig()?.RestNodes[0] ?? -Vector3.UnitY * fall.HalfExtent.Y;
        fall.AttachmentLocal = pin;
        var towardsCentre = -pin;
        fall.Rotation = TorsoTurn(towardsCentre, Vector3.Normalize(contact.Normal * 0.35f + tangent));
        fall.Position = GutEmergence.Anchor(contact, fall.InsideDepth, fall.Age) - Vector3.Transform(pin, fall.Rotation);
        if (!GutEmergence.AttachedAt(fall.KeepAttached, fall.Age))
        {
            fall.Position = contact.Position with { Y = fall.Floor + GutSupportHeight(fall) };
            fall.Resting = true;
            fall.AttachmentBroken = true;
        }
        if (geometry is null) return;
        gutPhysics ??= new BowelRagdollScene();
        fall.Physical = gutPhysics.Add(fall.RigKey, geometry, fall.Position, fall.Rotation,
            fall.Resting ? Vector3.Zero : fall.Velocity, fall.Resting,
            pinNode: fall.AttachmentBroken ? -1 : 0, preserveAttachment: fall.KeepAttached);
    }
}
