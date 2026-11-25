using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Construction;

namespace Meitou.CityBuilder;

/// <summary>
/// Furniture and signs (City.dll's furnishing): a CITY building gets the interior layout of the vanilla building it copies, named by its people's
/// template's <c>layout interior</c> (a guard post the twin's Barracks), and the sign of its <c>layout exterior</c> from layouts.txt, as finished
/// pieces of the player's faction parented to it. A trade house that changes its shop has its pieces taken down (the selection panel's dismantle,
/// <c>ConfirmDismantle</c>) and the new shop's placed once they are gone.
/// </summary>
internal sealed partial class CitySystem
{
    void Furnish(World world, CityPlace place, BuildSite site, CityBuilding building)
    {
        if (world.Systems.OfType<ConstructionSystem>().FirstOrDefault() is not { } construction) return;

        place.Pieces.Clear();
        place.Furnished = true;
        var (interior, exterior) = LayoutNames(place, building);
        var plans = Plans(site, _layouts.Find(building.Vanilla, interior), outside: false)
            .Concat(Plans(site, _layouts.Find(building.Vanilla, exterior, exterior: true), outside: true)).ToList();
        if (plans.Count == 0) return;

        foreach (var placed in construction.PlaceSites(world, plans, complete: true))
        {
            if (placed is not null) place.Pieces.Add(placed.Id);
        }
    }

    List<PlannedBuilding> Plans(BuildSite site, IReadOnlyList<LayoutPiece> pieces, bool outside)
    {
        var plans = new List<PlannedBuilding>();
        foreach (var piece in pieces)
        {
            if (_db.Find(piece.Record) is not { Type: FcsRecordType.Building }) continue;

            var (at, rotation) = CityLayouts.Place(site.Position, site.Rotation, piece);
            plans.Add(new PlannedBuilding(piece.Record, at, rotation, outside ? 0 : site.Floor, site.Id, Outside: outside));
        }

        return plans;
    }

    (string Interior, string Exterior) LayoutNames(CityPlace place, CityBuilding building)
    {
        var template = building.Kind switch
        {
            CityKind.Tavern => TemplateOf(CityCatalog.Innkeeper),
            CityKind.TradeHouse => TemplateOf(CityCatalog.Shop(building.People, place.Shop)),
            _ => null,
        };
        string interior = building.Kind switch
        {
            CityKind.GuardPost => "Barracks",
            CityKind.Residence => "Residential",
            _ => template?.LayoutInterior ?? "",
        };
        return (interior, template?.LayoutExterior ?? "");
    }

    /// <summary>Whether any piece the mod placed in the building still stands.</summary>
    static bool HasPieces(World world, CityPlace place) => place.Pieces.Any(id => world.Buildings.Find(id) is not null);

    /// <summary>Takes the building's pieces down; the next sweep furnishes it again once they are gone.</summary>
    void Unfurnish(World world, CityPlace place)
    {
        foreach (string piece in place.Pieces)
        {
            if (world.Buildings.Find(piece) is not null) _enqueue(new ConfirmDismantle(piece) { Tick = world.Tick + 1 });
        }

        place.Furnished = false;
    }
}
