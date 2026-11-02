using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class GameHost
{
    CharacterId torsoSceneOther = CharacterId.None;

    Vector3? StageTorsoCarry()
    {
        if (!IsSandbox || !g.SandboxBisect || g.Sandbox < 3)
            throw new ArgumentException("--scene torso-carry needs --sandbox 3 --sandbox-bisect");
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe)?.TorsoBisection is null)
            AdvanceScene((int)Math.Max(320 - session.World.Tick, 1));
        var corpse = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        // Extra sandbox bystanders share the player's faction but are not normally controllable.
        torsoSceneOther = session.CurrentSnapshot.Characters.First(c => c.Id != sandboxHero && c.Id != sandboxFoe).Id;
        session.World.Characters.Cold(torsoSceneOther.Slot)!.IsPlayer = true;
        session.World.Commands.Enqueue(new DebugTeleport([sandboxHero], corpse.Position + new Vector3(6, 0, -5)) { Tick = session.World.Tick });
        session.World.Commands.Enqueue(new DebugTeleport([torsoSceneOther], (corpse.TorsoPositivePosition ?? corpse.Position) + new Vector3(6, 0, 5)) { Tick = session.World.Tick });
        AdvanceScene(2);
        return TorsoCarryFocus();
    }

    Vector3? TorsoCarryFocus()
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe) is not { } corpse) return null;
        return (corpse.Position + (corpse.TorsoPositivePosition ?? corpse.Position)) * 0.5f
            + new Vector3(0, 4, 0);
    }

    readonly record struct TorsoGripMarker(int Side, Vector3 Centre, Vector3 Contact, Vector3 Support, float SkinGap);

    IEnumerable<TorsoGripMarker> TorsoGripMarkers()
    {
        if (gpu.Characters is not { } renderer
            || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe) is not { TorsoBisection: { } cut } body) yield break;
        foreach (int side in new[] { 0, 1 })
        {
            var holder = side > 0 ? body.TorsoPositiveCarriedBy : body.CarriedBy;
            if (holder.IsNone) continue;
            long carrierKey = DrawKey(holder);
            if (renderer.BonePosition(carrierKey, "Bip01 L UpperArm") is not { } shoulder
                || renderer.BonePosition(carrierKey, "Bip01 L Forearm") is not { } elbow) continue;
            CharacterRenderer.CarryArmSupport? arm = null;
            var support = renderer.UpperArmSupportPoint(carrierKey, shoulder, elbow, CarryPose.UpperArmProbe(shoulder, elbow), ref arm)
                ?? CarryPose.BicepsSupport(shoulder, elbow);
            long pieceKey = TorsoDrawKey(body.Id, side);
            int? component = null;
            var pose = side > 0 ? body.TorsoRagdoll?.Positive : body.TorsoRagdoll?.Negative;
            if (body.Appearance is { } look && renderer.Rigs.For(look) is { } setup && setup.Rig.Order.Length > 0
                && pose?.Components is { } groups && setup.Rig.Order[0] < groups.Length) component = groups[setup.Rig.Order[0]];
            var retained = new Meitou.Rendering.CharacterTorsoCut(cut.Plane, side > 0 ? 1 : -1, cut.FullBody);
            if (renderer.TorsoGripCentre(pieceKey, retained, support, component) is not { } geometry) continue;
            var surface = renderer.ClosestTorsoSurfacePoint(pieceKey, retained, support, component);
            var contact = surface ?? geometry.Contact;
            if (session.World.Systems.OfType<Meitou.Simulation.Ragdolls.RagdollSystem>().FirstOrDefault() is { } physics
                && physics.TryTorsoCarryContact(session.World, body.Id, side > 0 ? 1 : -1, out var cachedContact, out _)) contact = cachedContact;
            if (torsoFalls.TryGetValue(body.Id, out var fall) && fall.CarryReferences.TryGetValue(side > 0 ? 1 : -1, out var held) && held.Reference is { } reference)
                contact = reference.RetainedPoint + reference.Translation;
            yield return new(side, geometry.Centre, contact, support,
                surface is { } at ? Vector3.Distance(support, at) : float.PositiveInfinity);
        }
    }

    void ReportTorsoGripContact(int frame)
    {
        foreach (var marker in TorsoGripMarkers())
            Console.WriteLine($"carry grip   frame {frame} side {marker.Side}: arm-to-retained-skin {marker.SkinGap:F4}, centre {marker.Centre}, central contact {marker.Contact}, arm support {marker.Support}, contact-to-arm {Vector3.Distance(marker.Contact, marker.Support):F4}");
    }

    void DrawTorsoGripMarkers(DebugOverlay overlay, int width, int height)
    {
        var view = camera.View * camera.Projection(width / (float)Math.Max(height, 1), camera.Near, camera.ViewDistance);
        Vector2? Screen(Vector3 world)
        {
            var clip = Vector4.Transform(new Vector4(world, 1), view);
            if (clip.W <= 0.1f) return null;
            return new((clip.X / clip.W * 0.5f + 0.5f) * width, (0.5f - clip.Y / clip.W * 0.5f) * height);
        }
        foreach (var marker in TorsoGripMarkers())
        {
            if (Screen(marker.Centre) is not { } centre || Screen(marker.Contact) is not { } contact || Screen(marker.Support) is not { } support) continue;
            var cyan = new Vector4(0.1f, 0.95f, 1, 1); var yellow = new Vector4(1, 0.85f, 0.1f, 1); var green = new Vector4(0.2f, 1, 0.25f, 1);
            overlay.Line(centre.X, centre.Y, contact.X, contact.Y, 1.5f, cyan);
            overlay.Line(contact.X, contact.Y, support.X, support.Y, 1.5f, yellow);
            overlay.Rect(centre.X - 3, centre.Y - 3, centre.X + 3, centre.Y + 3, cyan);
            overlay.Rect(contact.X - 3, contact.Y - 3, contact.X + 3, contact.Y + 3, yellow);
            overlay.Line(support.X - 5, support.Y, support.X + 5, support.Y, 2, green);
            overlay.Line(support.X, support.Y - 5, support.X, support.Y + 5, 2, green);
            overlay.Text($"{marker.Side}: centre", centre.X + 6, centre.Y - 16, cyan);
            overlay.Text("middle grip", contact.X + 6, contact.Y, yellow);
            overlay.Text("arm", support.X + 6, support.Y + 15, green);
        }
    }

    void StepTorsoCarryScene(int frame)
    {
        var world = session.World;
        if (frame is 1 or 2)
        {
            var carrier = frame == 1 ? sandboxHero : torsoSceneOther;
            int side = frame == 1 ? 1 : 0;
            world.Commands.Enqueue(new SelectCommand([carrier]) { Tick = world.Tick });
            AdvanceScene(1);
            var corpse = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
            var cut = corpse.TorsoBisection!.Value;
            var points = gpu.Characters?.TorsoCutBounds(TorsoDrawKey(sandboxFoe, side), new(cut.Plane, side == 1 ? 1 : -1, cut.FullBody)) ?? [];
            if (!player.ScenePickUpTorso(sandboxFoe, side, points))
                throw new InvalidOperationException($"The rendered torso half {side} could not be picked through its real context verb.");
            AdvanceScene(120);
            if (frame == 1)
            {
                var at = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero).Position;
                world.Commands.Enqueue(new MoveOrder([sandboxHero], at + new Vector3(12, 0, -10)) { Tick = world.Tick });
                AdvanceScene(100);   // Keep the first holder from occluding the second rendered pickup.
            }
            if (frame == 2)
            {
                var at = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero).Position;
                world.Commands.Enqueue(new MoveOrder([sandboxHero], at + new Vector3(-12, 0, -8)) { Tick = world.Tick });
                world.Commands.Enqueue(new MoveOrder([torsoSceneOther], at + new Vector3(12, 0, 8)) { Tick = world.Tick });
                AdvanceScene(60);   // Exercise turning and walking with both independently held halves.
            }
        }
        else if (frame is 3 or 4)
        {
            var carrier = frame == 3 ? sandboxHero : torsoSceneOther;
            world.Commands.Enqueue(new PutDownOrder([carrier]) { Tick = world.Tick });
            AdvanceScene(120);
        }
        var body = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        var positiveHolder = frame is 1 or 2 ? sandboxHero : CharacterId.None;
        var negativeHolder = frame is 2 or 3 ? torsoSceneOther : CharacterId.None;
        if (body.TorsoPositiveCarriedBy != positiveHolder || body.CarriedBy != negativeHolder)
            throw new InvalidOperationException($"Independent torso carry state mismatch at frame {frame}.");
        Console.WriteLine($"torso carry   frame {frame}: positive holder {body.TorsoPositiveCarriedBy}, negative holder {body.CarriedBy}; positive {body.TorsoPositivePosition}, negative {body.Position}");
    }
}
