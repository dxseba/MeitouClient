using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;

namespace Meitou.SlaverMod;

/// <summary>
/// Enslaving and freeing. The owner walks over and puts the shackles on through the game's own <see cref="PutShacklesOrder"/> (handed a pair
/// first when it carries none; with <c>RequireShackles</c> a shackle kit, or loose shackles, of the owner or a free member nearby is used up);
/// once they are on, the target joins the slave squad as a slave of the player's faction.
/// </summary>
sealed partial class SlaverSystem
{
    static readonly MethodInfo? LeaveMethod = typeof(DialogueSystem).GetMethod("Leave", BindingFlags.Static | BindingFlags.NonPublic,
        [typeof(World), typeof(CharacterId), typeof(CharacterCold)]);

    /// <summary>
    /// Whether <paramref name="c"/> may be enslaved: alive, not the player's, down, caged or chained; another faction's slave only with stealing
    /// allowed; an animal only when allowed.
    /// </summary>
    bool Enslaveable(CharacterCold c, bool alive)
    {
        if (!alive || c.Medical is { Dead: true } || c.IsPlayer) return false;
        if (SquadRegistry.IsAnimal(c) && !settings.AnimalSlaves) return false;
        if (c.SlaveState == 1 && !settings.AllowStealingSlaves) return false;

        return c.IsImprisoned || c.Medical is { Incapacitated: true } || LockSystem.Chained(c) || LockSystem.WornShackles(c) is not null;
    }

    void Begin(World world, EnslaveOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int t) || table.Cold(t) is not { } target || !Enslaveable(target, table.Next[t].Alive))
        {
            world.Post("Cannot enslave: must be unconscious, chained or caged");
            return;
        }

        if (!table.TryResolveNext(order.Owner, out int o) || table.Cold(o) is not { IsPlayer: true } owner || PlayerSlave(owner)) return;

        if (settings.RequireShackles && !UseKit(world, o))
        {
            world.Post("Cannot enslave: no shackle kit: buy one from a slave trader");
            return;
        }

        ulong lasting = Lasting(world, order.Target);
        _state.Pending[lasting] = (Lasting(world, order.Owner), world.Tick + (long)(120 / world.TickSeconds));
        if (LockSystem.WornShackles(target) is not null) return;   // already shackled: enslaved by the pending check

        if (LockSystem.CarriedShackles(owner) is null && (settings.GiveShackles || settings.RequireShackles)) GiveShackles(owner);

        world.Defer(new PutShacklesOrder([order.Owner], order.Target) { Tick = world.Tick + 1 });
    }

    void GiveShackles(CharacterCold owner)
    {
        if (population.Items.Create(settings.ShackleItem) is not { Lock: not null } item) return;

        item.Lock.Locked = false;
        owner.Inventory?.Add(item);
    }

    /// <summary>
    /// Uses up one shackle kit carried by the owner or a free member within the supervision range (loose shackles count and are kept for the
    /// put-on). False when there is none.
    /// </summary>
    bool UseKit(World world, int owner)
    {
        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { IsPlayer: true, Inventory: { } inv } m || PlayerSlave(m)) continue;
            if (i != owner && Distance(world, i, owner) > settings.SupervisionRange) continue;

            if (LockSystem.CarriedShackles(m) is not null) return true;
            if (inv.All().FirstOrDefault(it => it.Record == settings.ShackleKitItem) is { } kit && inv.RemoveOne(kit)) return true;
        }

        return false;
    }

    /// <summary>The enslavements under way: shackles on means enslaved; past the time limit they are dropped.</summary>
    void FinishPending(World world)
    {
        foreach (var (target, (owner, until)) in _state.Pending.ToList())
        {
            if (!Resolve(world, target, out int t, out var c) || world.Tick > until)
            {
                _state.Pending.Remove(target);
                continue;
            }

            if (LockSystem.WornShackles(c) is null) continue;

            _state.Pending.Remove(target);
            Enslave(world, t, c, target, Resolve(world, owner, out int o, out _) ? world.Characters.IdOf(o) : CharacterId.None);
        }
    }

    void Enslave(World world, int slot, CharacterCold c, ulong lasting, CharacterId owner)
    {
        bool prisoner = c.IsImprisoned || LockSystem.Chained(c);
        bool recaptured = _state.Runaways.TryGetValue(lasting, out var ran);
        string origin = recaptured && ran!.Origin.Length > 0 ? ran.Origin : FactionId(c.Faction);
        if (!MakeSlave(world, slot, c, lasting, StartSubmission(c, prisoner, lasting), origin)) return;

        if (!owner.IsNone) Say(world, owner, recaptured ? "RecaptureMaster" : "EnslaveMaster");
        if (c.Medical is not { Incapacitated: true }) Say(world, world.Characters.IdOf(slot), "EnslaveSlave");

        world.Post($"{c.Name} is now a slave of your faction.");
    }

    /// <summary>
    /// The character joins the slave squad (the recruit path, <c>PopulationSystem.JoinPlayerSquad</c>) and becomes a slave of the player's
    /// faction obeying that squad. False when the game refuses it (the squad or faction caps).
    /// </summary>
    bool MakeSlave(World world, int slot, CharacterCold c, ulong lasting, float submission, string origin)
    {
        if (spawner is null || SlaveSquad(world, slot) is not { } squad) return false;

        if (!spawner.JoinPlayerSquad(world, world.Characters.IdOf(slot), squad.Id))
        {
            world.Post($"The game refused to recruit {c.Name}");
            return false;
        }

        FixtureRegistry.Enslave(world, c, world.Player.Faction, squad.Id);
        _state.Slaves[lasting] = new SlaveRecord { Submission = submission, Origin = origin };
        _state.Runaways.Remove(lasting);
        _state.Rebels.Remove(lasting);
        return true;
    }

    float StartSubmission(CharacterCold c, bool prisoner, ulong lasting)
    {
        if (_state.Runaways.TryGetValue(lasting, out var ran)) return Math.Max(0, ran.Submission - settings.EscapeRecapturePenalty);

        float start = Race(c).Start ?? settings.SubmissionStart;
        return Math.Clamp(start + (prisoner ? settings.SubmissionPrisonerBonus : 0), 0, 100);
    }

    /// <summary>The player's squad named after the setting with room, else a new one of that name where the slave lies.</summary>
    Squad? SlaveSquad(World world, int slot)
    {
        int max = population.Bodies.MaxSquadSize;
        var squad = SquadManagement.PlayerSquads(world).FirstOrDefault(s => s.Name == settings.SquadName && s.Members.Count < max);
        if (squad is not null) return squad;

        var relations = population.Relations;
        string faction = relations.Contains(world.Player.Faction) ? relations.Factions[world.Player.Faction].Name : "Nameless";
        squad = SquadManagement.NewSquad(world, population.Db, world.Characters.Next[slot].Position, faction);
        squad.Name = settings.SquadName;
        return squad;
    }

    /// <summary>
    /// Freeing: the shackles' lock breaks and the slave state goes (the game's DA 53); with kits one kit goes back to the owner; one at or above
    /// the stay submission stays in the player's faction, the others leave for the faction they were taken from (the escape faction when unknown).
    /// </summary>
    void Free(World world, int slot, CharacterCold c, ulong lasting, CharacterId owner)
    {
        var record = _state.Slaves.GetValueOrDefault(lasting);
        _state.Slaves.Remove(lasting);
        FixtureRegistry.FreeSlave(world, slot, c);
        Say(world, owner, "FreeMaster");
        if (settings.RequireShackles) GiveKit(world, owner);

        if (record is not null && record.Submission >= settings.FreeStayMinSubmission)
        {
            Say(world, world.Characters.IdOf(slot), "FreeSlaveStays");
            world.Post($"{c.Name} was freed and stays in your faction ({record.Submission:0}).");
            return;
        }

        Say(world, world.Characters.IdOf(slot), "FreeSlave");
        world.Post($"{c.Name} was freed and goes back to {FactionName(record?.Origin ?? "")}.");
        GoHome(world, slot, c, record?.Origin ?? "");
    }

    void GiveKit(World world, CharacterId owner)
    {
        if (world.Characters.TryResolveNext(owner, out int o) && world.Characters.Cold(o)?.Inventory is { } inv
            && population.Items.Create(settings.ShackleKitItem) is { } kit)
            inv.Add(kit);
    }

    string FactionName(string id) => population.Relations.IndexOf(id) is int f and >= 0 ? population.Relations.Factions[f].Name : "their own";

    /// <summary>
    /// The character leaves the player for a new squad of <paramref name="origin"/> (the escape faction when unknown) where it stands.
    /// </summary>
    void GoHome(World world, int slot, CharacterCold c, string origin)
    {
        int faction = population.Relations.IndexOf(origin);
        if (faction < 0 || faction == world.Player.Faction) faction = population.Relations.IndexOf(settings.EscapeFaction);
        if (faction >= 0) JoinNpcSquad(world, slot, c, faction, NewNpcSquad(world, slot, faction, FreedTemplate));
    }

    Squad NewNpcSquad(World world, int slot, int faction, string template)
    {
        var at = world.Characters.Next[slot].Position;
        var squad = new Squad
        {
            Id = world.Squads.NextId(), TemplateId = template, Faction = faction, Position = at, HomeCentre = new(at.X, at.Z), HomeRadius = 2000,
        };
        world.Squads.Add(squad);
        return squad;
    }

    /// <summary>
    /// The character leaves the player's squad for an NPC squad of <paramref name="faction"/> (as the game's own squad split does).
    /// </summary>
    void JoinNpcSquad(World world, int slot, CharacterCold c, int faction, Squad squad)
    {
        if (LeaveMethod is null) return;

        var me = world.Characters.IdOf(slot);
        LeaveMethod.Invoke(null, [world, me, c]);
        c.IsPlayer = false;
        c.Faction = faction;
        c.Ai = null;
        c.Role = (int)(squad.Leader.IsNone ? SquadRole.Leader : SquadRole.Squad1);
        c.SquadId = squad.Id;
        squad.Members.Add(me);
        if (squad.Leader.IsNone) squad.Leader = me;
    }

    /// <summary>The original's <c>FreedSquadTemplate</c> / <c>EscapeSquadTemplate</c> default: "UC escaped slave".</summary>
    const string FreedTemplate = "54294-rebirth.mod";
}
