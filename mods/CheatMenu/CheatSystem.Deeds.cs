using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Editing;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

namespace Meitou.CheatMenu;

/// <summary>
/// The one-shot cheats the system applies itself (those the game's debug commands do not cover), and Chaos and the raids.
/// </summary>
internal sealed partial class CheatSystem
{
    /// <summary>The original's relation bounds and its "everyone hates you" and "at war" value.</summary>
    const float Hated = -100, Allied = 100;

    /// <summary>A raid's spawn ring round the camera (engine choice: the original makes the squad at the camera).</summary>
    const float RaidNear = 150, RaidFar = 300;

    bool Do(World world, DoDeed deed)
    {
        switch (deed.Which)
        {
            case Deed.SetMoney: world.Player.Money = Math.Max(0, (long)deed.Number); break;
            case Deed.FillHunger: ForEach(world, deed.Targets, (_, _, m) => m.Hunger = MedicalState.MaxHunger); break;
            case Deed.FreeSlaves: FreeSlaves(world); break;
            case Deed.ToggleHungerOf: ToggleFed(deed.Targets); break;
            case Deed.RemoveLimbs: ForEach(world, deed.Targets, (s, c, m) => RemoveLimbs(world, s, c, m)); break;
            case Deed.Despawn: Despawn(world, deed.Targets); break;
            case Deed.RevealTowns: RevealTowns(world); break;
            case Deed.RandomSkills: RandomSkills(world); break;
            case Deed.AllAtWar: AllAtWar(world); break;
            case Deed.EveryoneHates: Send(world, new DebugSetRelation(world.Player.Faction, null, Hated) { Tick = world.Tick + 1 }); break;
            case Deed.RenameCharacter: Rename(world, deed); break;
            case Deed.RenameSquad: RenameSquad(world, deed); break;
            case Deed.AnimalNextAge: NextAge(world, deed.Targets); break;
            case Deed.Clone: Clone(world, deed.Targets); break;
            case Deed.Recruit or Deed.RecruitNewSquad: Recruit(world, deed.Targets, deed.Which == Deed.RecruitNewSquad); break;
            case Deed.FinishBuildings: FinishNear(world); break;
            case Deed.SpawnSquad: SpawnAtCamera(world, deed.Text); break;
            case Deed.StepRelation: StepRelation(world, (int)deed.Number, deed.Text == "+" ? 25 : -25); break;
        }

        return true;
    }

    /// <summary>A faction's relation to the player's moved by <paramref name="step"/>, held to -100..100, both ways (the game's command).</summary>
    void StepRelation(World world, int faction, float step)
    {
        int player = world.Player.Faction;
        if (player < 0 || faction < 0 || faction == player) return;

        float now = data.Relations.Get(player, faction);
        Send(world, new DebugSetRelation(player, faction, Math.Clamp(now + step, Hated, Allied)) { Tick = world.Tick + 1 });
    }

    static void ForEach(World world, IReadOnlyList<CharacterId> ids, Action<int, CharacterCold, MedicalState> act)
    {
        foreach (var id in ids)
        {
            if (world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { Medical: { Dead: false } m } cold)
                act(slot, cold, m);
        }
    }

    void ToggleFed(IReadOnlyList<CharacterId> ids)
    {
        foreach (var id in ids)
        {
            if (!_fedOnly.Remove(id)) _fedOnly.Add(id);
        }
    }

    /// <summary>The original's Remove limbs: every arm and leg still the body's own becomes a stump (as a cut-off), the gear refreshed.</summary>
    void RemoveLimbs(World world, int slot, CharacterCold cold, MedicalState medical)
    {
        if (cold.Race is not { } race) return;

        var ctx = new MedicalContext(data.Bodies.Constants, data.Bodies.Options, race)
        {
            Toughness = cold.Stats?.ScaledToughness ?? 0,
            Strength = cold.Stats?.Strength ?? 50,
            HasStats = cold.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(world.Characters.IdOf(slot)),
        };
        bool cut = false;
        for (int i = 0; i < medical.Parts.Count; i++)
        {
            var part = medical.Parts[i];
            if (part.Template.Type is not (Meitou.Data.Gameplay.Bodies.BodyPartType.Arm or Meitou.Data.Gameplay.Bodies.BodyPartType.Leg)) continue;
            if (part.Limb != LimbState.Original) continue;

            medical.Sever(i, ctx);
            cut = true;
        }

        if (cut) CharacterRefresh.Refresh(data, cold, CharacterRefreshKind.Equipment);
    }

    /// <summary>Despawn: the NPCs leave the world at once (the player's own are kept); removals wait for this step's slow world.</summary>
    void Despawn(World world, IReadOnlyList<CharacterId> ids)
    {
        foreach (var id in ids)
        {
            if (world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { IsPlayer: false }) _leaving.Add(id);
        }
    }

    readonly List<CharacterId> _leaving = [];

    /// <summary>Takes the despawned characters out (slow world, where removals are allowed).</summary>
    void Leave(World world)
    {
        foreach (var id in _leaving) world.Characters.Remove(id);
        _leaving.Clear();
    }

    /// <summary>Reveal all towns: every placed town discovered and explored on the map.</summary>
    void RevealTowns(World world)
    {
        for (int i = 0; i < data.Sites.Count; i++) world.Player.Know(i, TownKnowledge.Discovered | TownKnowledge.Explored);
    }

    /// <summary>Random skills for every NPC: each stat of each NPC from 1 to 100, from the world's seed (the port's rolls).</summary>
    void RandomSkills(World world)
    {
        var purpose = CheatMenuMod.Purpose;
        foreach (var id in Living(world, c => !c.IsPlayer))
        {
            var values = new Dictionary<Meitou.Data.Gameplay.Bodies.StatsEnumerated, float>();
            foreach (var stat in AllStats)
            {
                ulong roll = Rng.Hash(world.Seed, (ulong)Rng.Key(id), purpose, (ulong)world.Tick * 64 + (ulong)stat);
                values[stat] = 1 + roll % 100;
            }

            Send(world, new DebugSetStatValues([id], values, new Dictionary<string, float>()) { Tick = world.Tick + 1 });
        }
    }

    /// <summary>All factions at war: every pair of factions at -100, as the original's double loop.</summary>
    void AllAtWar(World world)
    {
        for (int a = 0; a < data.Factions.Count; a++) Send(world, new DebugSetRelation(a, null, Hated) { Tick = world.Tick + 1 });
    }

    /// <summary>Chaos: every faction pair at a random relation from -100 to 100, every 60 s while on.</summary>
    void Chaos(World world)
    {
        int n = data.Factions.Count;
        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                ulong roll = Rng.Hash(world.Seed, (ulong)(a * n + b), CheatMenuMod.Purpose, (ulong)world.Tick);
                Send(world, new DebugSetRelation(a, b, Hated + roll % 201) { Tick = world.Tick + 1 });
            }
        }
    }

    /// <summary>A raid: a random squad template that has a faction, spawned round the camera's point (the original's every 180 s).</summary>
    void Raid(World world)
    {
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return;

        var templates = data.Db.OfType(FcsRecordType.SquadTemplate).Where(t => data.FactionOfTemplate(t.StringId) >= 0).ToList();
        if (templates.Count == 0) return;

        var pick = templates[(int)(Rng.Hash(world.Seed, 0, CheatMenuMod.Purpose, (ulong)world.Tick) % (ulong)templates.Count)];
        SpawnSquad(world, population, pick.StringId, _camera);
    }

    /// <summary>Spawns a squad template at a point, homed at the nearest town; false when the game refuses it.</summary>
    internal bool SpawnSquad(World world, PopulationSystem population, string template, Vector3 at)
    {
        int faction = data.FactionOfTemplate(template);
        if (faction < 0 || data.Sites.Count == 0) return false;

        var home = data.Sites.MinBy(s => Vector3.DistanceSquared(s.Position, at))!;
        int platoon = population.SpawnPlatoon(world, faction, template, home.Index, new Vector2(at.X, at.Z), RaidNear, RaidFar);
        world.Post(platoon >= 0 ? $"CheatMenu: squad {template}" : $"CheatMenu: squad {template} not spawned");
        return platoon >= 0;
    }

    void Rename(World world, DoDeed deed)
    {
        foreach (var id in deed.Targets)
        {
            if (world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { IsPlayer: true })
                Send(world, new EditCharacter(id, null, deed.Text, EditKind.Recruit) { Tick = world.Tick + 1 });
        }
    }

    static void RenameSquad(World world, DoDeed deed)
    {
        if (deed.Targets.Count == 0 || !world.Characters.TryResolveNext(deed.Targets[0], out int slot)) return;
        if (world.Characters.Cold(slot) is not { IsPlayer: true, SquadId: >= 0 } cold) return;

        Send(world, new RenameSquadCommand(cold.SquadId, deed.Text) { Tick = world.Tick + 1 });
    }

    /// <summary>Animal: next age stage (the original steps the age through its stages; the port by a quarter, from 0 to 1.5).</summary>
    static void NextAge(World world, IReadOnlyList<CharacterId> ids)
    {
        foreach (var id in ids)
        {
            if (!world.Characters.TryResolveNext(id, out int slot)) continue;
            if (world.Characters.Cold(slot) is not { Animal: not null, Stats: { } stats }) continue;

            float age = stats.Scale >= 1.5f ? 0.25f : MathF.Min(1.5f, stats.Scale + 0.25f);
            Send(world, new DebugAnimalAge([id], age) { Tick = world.Tick + 1 });
        }
    }
}

/// <summary>The cheats that make or move characters and buildings.</summary>
internal sealed partial class CheatSystem
{
    /// <summary>The original's building sweep radius round the camera (<b>Verified</b>, 300).</summary>
    const float NearCamera = 300;

    static PopulationSystem? Population(World world) => world.Systems.OfType<PopulationSystem>().FirstOrDefault();

    /// <summary>Clone: a new character of each selected player character's record in its squad, at the camera's point (not a copy of it).</summary>
    void Clone(World world, IReadOnlyList<CharacterId> ids)
    {
        if (Population(world) is not { } population) return;

        foreach (var id in ids)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true } cold) continue;

            var made = population.SpawnIntoPlayerSquad(world, cold.SquadId, cold.RecordId, new Vector2(_camera.X, _camera.Z),
                new SpawnOptions(1, IgnoreCaps: true));
            world.Post(made.IsNone ? $"CheatMenu: {cold.Name} not cloned" : $"CheatMenu: {cold.Name} cloned");
        }
    }

    /// <summary>Recruit: the NPCs join the active squad (or one new squad), with no dialogue line needed, as the original's direct recruit.</summary>
    void Recruit(World world, IReadOnlyList<CharacterId> ids, bool newSquad)
    {
        if (Population(world) is not { } population || ids.Count == 0) return;

        int squad = world.Player.Squad;
        if (newSquad && world.Characters.TryResolveNext(ids[0], out int first))
        {
            var at = world.Characters.Next[first].Position;
            squad = SquadManagement.NewSquad(world, data.Db, at, world.Player.FactionName ?? "Nameless").Id;
        }

        int joined = ids.Count(id => population.JoinPlayerSquad(world, id, squad));
        world.Post($"CheatMenu: recruited {joined} of {ids.Count}");
    }

    /// <summary>Finish buildings near camera: every unfinished site within 300 of the camera's point completes, as a last builder's step.</summary>
    void FinishNear(World world)
    {
        if (world.Systems.OfType<ConstructionSystem>().FirstOrDefault() is not { } construction) return;

        int done = 0;
        foreach (var site in world.Buildings.All.ToList())
        {
            if (site.Complete || Vector3.Distance(site.Position, _camera) > NearCamera) continue;

            site.Progress = site.Target;
            if (Finisher.Finish(world, construction, site)) done++;
        }

        world.Post($"CheatMenu: finished {done} buildings");
    }

    void SpawnAtCamera(World world, string template)
    {
        if (Population(world) is { } population) SpawnSquad(world, population, template, _camera);
    }
}
