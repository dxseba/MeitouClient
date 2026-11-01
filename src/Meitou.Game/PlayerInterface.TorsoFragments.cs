using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    /// <summary>The rendered torso half under a ray: 0 for the owner's negative half, 1 for the positive half.</summary>
    public Func<CharacterId, Vector3, Vector3, int>? TorsoSideHit { get; set; }

    int PickTorsoSide(CharacterId id, Vector2 pixel)
    {
        if (BodyFragmentLabelAt(pixel) is { Interactive: true } label && label.Owner == id) return label.TorsoSide;
        return Ray(pixel) is { } ray && session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id)?.TorsoBisection is { IsValid: true }
            ? TorsoSideHit?.Invoke(id, ray.Origin, ray.Direction) == 1 ? 1 : 0 : 0;
    }

    static CharacterId FragmentCarrier(CharacterSnapshot target, int side) => side == 1 ? target.TorsoPositiveCarriedBy : target.CarriedBy;

    static bool CarriesFragment(CharacterSnapshot carrier, CharacterId body, int side) =>
        carrier.Carrying == body && (carrier.CarryingTorsoSide == 1) == (side == 1);

    /// <summary>Picking either half keeps a single corpse inventory, with a separate carry link for each half.</summary>
    List<(string Caption, Action Give)> TorsoFragmentVerbs(CharacterSnapshot target, int side)
    {
        var verbs = new List<(string, Action)>();
        if (target.CanOpenInventory) verbs.Add(InventoryVerb(target, side));
        var holders = Selected().Where(c => CarriesFragment(c, target.Id, side)).Select(c => c.Id).ToArray();
        if (holders.Length > 0)
            verbs.Add(("Put Down", () => Say(new PutDownOrder(holders) { Tick = session.World.Tick })));
        else if (FragmentCarrier(target, side).IsNone)
            verbs.Add(("Pick Up", () => Say(new PickUpOrder([], target.Id) { Tick = session.World.Tick, TorsoSide = side })));
        return verbs;
    }

    /// <summary>A native screenshot scene exercises the rendered pick and the same Pick Up verb as the user.</summary>
    internal bool ScenePickUpTorso(CharacterId id, int side, IReadOnlyList<Vector3> points)
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } target) return false;
        foreach (var point in points)
        {
            if (Project(point) is not { } pixel || PickWhereAt(pixel, _ => true)?.Id != id || PickTorsoSide(id, pixel) != side) continue;
            var verb = Verbs(target, side).FirstOrDefault(v => v.Caption == "Pick Up");
            if (verb.Give is null) return false;
            Console.WriteLine($"torso pick    half {side} at pixel {pixel.X:0}, {pixel.Y:0}");
            verb.Give();
            return true;
        }
        return false;
    }

    public int HoverTorsoSide => Point() is { Target: { } id } p ? PickTorsoSide(id, p.Mouse) : 0;
}
