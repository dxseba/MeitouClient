using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.QuestsContracts;

/// <summary>
/// The Shinobi thieves (the original's heists 12, rescues 13, marks 14): the fence stocks a lockable chest of the town with the marked piece; the
/// lookout's scan says whether the player is in the heist's town and which piece is there; the rescue's scan whether one of the guild is in the
/// cells.
/// </summary>
sealed partial class LedgerSystem
{
    static readonly string[] Chests = ["Metal Chest", "Safe", "Secure Wooden Chest"];
    const float ChestReach = 900, RescueReach = 3000, ScanReach = 9000;

    int _scoutHere, _scoutFound, _rescueHere, _rescueFound;

    /// <summary>scouted: the fence has spoken; a chest job's piece goes into a chest now, a courier's is the one he handed over.</summary>
    void Scouted(World world)
    {
        if (_state.Slots.FirstOrDefault(s => s.Kind == 12 && !s.Scouted) is not { } slot) return;

        bool chest = slot.Type == "chest";
        if (!chest) (slot.Scouted, slot.Found) = (true, slot.Found.Length > 0 ? slot.Found : "the piece the fence handed you");
        else if (slot.FoundIndex == 0) StockChest(world, slot);
    }

    /// <summary>The local mark: the town's thieves point at a safe here, stocked at once.</summary>
    void Mark(World world, Slot slot, Who w)
    {
        (slot.Dest, slot.DestName, slot.NearTown, slot.Type) = (TownCode(w.Town), w.Town, w.Town, "chest");
        StockChest(world, slot);
    }

    /// <summary>
    /// The fence stocks a real chest: the lockable chests (Metal Chest, Safe, Secure Wooden Chest) within 900 of the player's character, nearest
    /// first, starting at the one the heists done point at, take the piece of the day (the want list by day); false when none is in reach.
    /// </summary>
    bool StockChest(World world, Slot slot)
    {
        if (slot.Want.Count == 0 || PlayerPosition(world) is not { } at) return false;

        var chests = world.Fixtures.All.Where(f => f is { Kind: FixtureKind.Container, Contents: not null } && IsChest(f.RecordId)
                && Vector2.Distance(new(f.Position.X, f.Position.Z), new(at.X, at.Z)) <= ChestReach)
            .OrderBy(f => Vector3.DistanceSquared(f.Position, at)).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();
        if (chests.Count == 0) return false;

        int wi = Math.Max(0, _day) % slot.Want.Count;
        if (_population.Items.Create(slot.Want[wi]) is not { } item) return false;

        int start = LedgerState.Get(_state.Done, "heist:done", 0) % chests.Count;
        for (int k = 0; k < chests.Count; k++)
        {
            var chest = chests[(start + k) % chests.Count];
            if (!chest.Contents!.Add(item)) continue;

            string name = _population.Db.Find(chest.RecordId)?.Name ?? "chest";
            (slot.Found, slot.FoundIndex, slot.Scouted) = ($"the {name} in {TownName(chest.Position)}", wi + 1, true);
            _state.Event(_day, $"The book says: {slot.Found}");
            return true;
        }

        return false;
    }

    bool IsChest(string record) => _population.Db.Find(record)?.Name is { } n && Chests.Contains(n, StringComparer.OrdinalIgnoreCase);

    /// <summary>The talker's position, else the first player character's.</summary>
    Vector3? PlayerPosition(World world)
    {
        var table = world.Characters;
        if (table.TryResolveNext(_ctxPlayer, out int p)) return table.Next[p].Position;

        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { IsPlayer: true }) return table.Next[i].Position;
        }

        return null;
    }

    /// <summary>
    /// The heist's scan: is a player character in the town the boss pointed at; for a courier heist, which marked piece lies in one of its
    /// containers (the 1-based index the lookout's lines read), named with its chest.
    /// </summary>
    void HeistTick(World world)
    {
        (_scoutHere, _scoutFound) = (0, 0);
        if (RideSlot(12) is not { } slot || slot.Scouted && slot.Type != "chest") return;

        _scoutHere = PartyIn(world, slot.DestName) ? 1 : 0;
        if (_scoutHere == 0) return;

        if (slot.Type == "chest")
        {
            _scoutFound = slot.FoundIndex;
            return;
        }

        foreach (var f in world.Fixtures.All)
        {
            if (f.Contents is null || TownName(f.Position) != slot.DestName) continue;

            int idx = f.Contents.All().Select(i => slot.Want.IndexOf(i.Record)).FirstOrDefault(i => i >= 0, -1);
            if (idx < 0) continue;

            string chest = _population.Db.Find(f.RecordId)?.Name ?? "chest";
            if (slot.FoundIndex != idx + 1) slot.Found = $"a marked piece, in the {chest}, {slot.DestName}";
            (_scoutFound, slot.FoundIndex) = (idx + 1, idx + 1);
            return;
        }
    }

    /// <summary>
    /// The rescue's scan: a player character in the jail town (by its name, or within 3000 of its place) and whether one of the guild (the
    /// job's target record) is within 9000 of him.
    /// </summary>
    void RescueScout(World world)
    {
        (_rescueHere, _rescueFound) = (0, 0);
        if (RideSlot(13) is not { } slot || PlayerPosition(world) is not { } at) return;

        bool byPlace = _population.Sites.FirstOrDefault(s => s.Town.Name == slot.DestName) is { } site
            && Vector2.Distance(new(site.Position.X, site.Position.Z), new(at.X, at.Z)) < RescueReach;
        _rescueHere = PartyIn(world, slot.DestName) || byPlace ? 1 : 0;
        if (_rescueHere == 0 || slot.Target != 0) return;
        if (slot.Targets.Count == 0)
        {
            _rescueFound = 1;
            return;
        }

        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { Medical.Dead: false } c && c.RecordId == slot.Targets[0]
                && Vector3.Distance(table.Next[i].Position, at) <= ScanReach)
            {
                Cage(world, slot, table.IdOf(i), c);
                _rescueFound = 1;
                return;
            }
        }

        if (!slot.Scouted) StockCell(world, slot, at);
    }

    /// <summary>
    /// The man in the cells (the original's <c>StockACell</c>): none of the guild in reach, so one is made behind the bars of the nearest free cage
    /// within 9000, once per rescue: the "Thief Prisoner" squad template of the Shinobi Thieves spawned at the cage, then caged as it loads. No
    /// free cage: nothing is put down, and the fence says he escaped.
    /// </summary>
    void StockCell(World world, Slot slot, Vector3 at)
    {
        var table = world.Characters;
        var spot = world.Fixtures.CageSpots(table).Where(c => c.Occupant.IsNone && Vector3.Distance(c.Position, at) <= ScanReach)
            .OrderBy(c => Vector3.DistanceSquared(c.Position, at)).FirstOrDefault();
        int faction = FactionByName("Shinobi Thieves"), site = _population.TownAt(at);
        var population = world.Systems.OfType<PopulationSystem>().FirstOrDefault();
        if (spot is null || faction < 0 || site < 0 || population is null) return;

        int platoon = population.SpawnPlatoon(world, faction, PrisonerTemplate, site, new(spot.Position.X, spot.Position.Z), 0, 40, maxMembers: 1);
        if (platoon < 0) return;

        (slot.Scouted, slot.CellPlatoon, slot.Cell) = (true, platoon, spot.Id);
    }

    /// <summary>The spawned prisoner goes behind the bars once his squad has loaded.</summary>
    void Cage(World world, Slot slot, CharacterId id, CharacterCold c)
    {
        if (slot.Cell.Length == 0 || c.IsImprisoned || world.Fixtures.Find(slot.Cell) is not { } cage) return;

        if (world.Fixtures.Imprison(world, id, cage)) slot.Cell = "";
    }

    /// <summary>The original's prisoner squad template, "Thief Prisoner" (in QuestsAndContracts.mod).</summary>
    const string PrisonerTemplate = "4334-QuestsAndContracts.mod";

    bool PartyIn(World world, string town)
    {
        if (town.Length == 0) return false;

        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { IsPlayer: true } && TownName(table.Next[i].Position) == town) return true;
        }

        return false;
    }
}
