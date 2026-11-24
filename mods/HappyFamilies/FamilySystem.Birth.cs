using System.Collections.Frozen;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.HappyFamilies;

sealed partial class FamilySystem
{
    /// <summary>
    /// Rings round the mother tried when her own spot is not walkable (this port's; the original spawns at her position).
    /// </summary>
    static readonly float[] Rings = [3, 8, 15, 25];

    /// <summary>
    /// The birth: the couple check again (a refusal is said by the mother, the pregnancy lost), then a child of the <c>HF_Random</c> template of
    /// either parent's race (by its name; else the mother's own CHARACTER) in her squad at her side, named with the father's surname, looking like
    /// both, with a quarter of their summed stats, starting as a child.
    /// </summary>
    void MakeChild(World world, int mother, int father)
    {
        if (CantHaveChild(world, mother, father) is { } reason)
        {
            Say(world, mother, reason);
            return;
        }

        var mum = world.Characters.Cold(mother)!;
        var dad = world.Characters.Cold(father)!;
        string template = ChildTemplate(world, mum, dad);
        if (Spawn(world, mum.SquadId, world.Characters.Next[mother].Position, template) is not { } newborn
            || !world.Characters.TryResolveNext(newborn, out int child))
        {
            context.Log($"no child from {template}: no walkable spot by the mother");
            return;
        }

        Settle(world, mother, father, child);
    }

    string ChildTemplate(World world, Simulation.CharacterCold mum, Simulation.CharacterCold dad)
    {
        var race = Roll(world, 1) > 0.5f ? mum.Race : dad.Race;
        return race is not null && Template(race.Name) is { } id ? id : mum.RecordId;
    }

    /// <summary>
    /// The newborn's name, look, stats and family record.
    /// </summary>
    void Settle(World world, int mother, int father, int child)
    {
        var table = world.Characters;
        var (mum, dad, kid) = (table.Cold(mother)!, table.Cold(father)!, table.Cold(child)!);
        if (Genetics.Surname(dad.Name) is { } surname) kid.Name = kid.Name + " " + surname;

        var lineage = new Lineage
        {
            Mother = Lasting(world, mother), Father = Lasting(world, father), MotherName = mum.Name, FatherName = dad.Name,
            ComeOfAge = Today(world) + Genetics.ChildhoodDays,
            AdultParts = AdultParts(mum, dad, kid),
            ChildSliders = new(Genetics.ChildSlidersOf(kid.Race), StringComparer.Ordinal),
            DrawnGrowth = 0,
        };

        var mumLook = mum.Appearance!.Loadout!.Appearance.Record;
        var dadLook = dad.Appearance!.Loadout!.Appearance.Record;
        ulong key = Lasting(world, child);
        Restyle(world, child, record =>
        {
            Genetics.MixLook(mumLook, dadLook, record, () => Roll(world, key));
            lineage.AdultSliders = Genetics.SetChildSliders(record, lineage.ChildSliders);
        });

        if (kid.Stats is { } stats) Genetics.MixStats(mum.Stats!, dad.Stats!, stats);
        _book.Lineages[key] = lineage;
        context.Log($"{mum.Name} and {dad.Name} had {kid.Name}");
    }

    /// <summary>
    /// Each of the child's body parts' adult maximum: the average of the parents' stored maxima of that part (one parent's when only one has it),
    /// else its own.
    /// </summary>
    static Dictionary<string, float> AdultParts(Simulation.CharacterCold mum, Simulation.CharacterCold dad, Simulation.CharacterCold kid)
    {
        var parts = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var part in kid.Medical!.Parts)
        {
            string id = part.Template.StringId;
            var known = new[] { Stored(mum, id), Stored(dad, id) }.OfType<float>().ToList();
            parts[id] = known.Count > 0 ? known.Average() : part.StoredMaxHp;
        }

        return parts;
    }

    static float? Stored(Simulation.CharacterCold cold, string part) =>
        cold.Medical?.Parts.FirstOrDefault(p => p.Template.StringId == part) is { } found ? found.StoredMaxHp : null;

    /// <summary>
    /// The child at the mother's spot, else on the first walkable point of the rings round it; past the size caps (the couple check has room).
    /// </summary>
    static CharacterId? Spawn(World world, int squad, Vector3 at, string template)
    {
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return null;

        var options = new SpawnOptions(IgnoreCaps: true);
        var id = population.SpawnIntoPlayerSquad(world, squad, template, new Vector2(at.X, at.Z), options);
        if (id != CharacterId.None) return id;

        foreach (float radius in Rings)
        {
            for (int k = 0; k < 8; k++)
            {
                float angle = k * MathF.PI / 4;
                var point = new Vector2(at.X + radius * MathF.Cos(angle), at.Z + radius * MathF.Sin(angle));
                id = population.SpawnIntoPlayerSquad(world, squad, template, point, options);
                if (id != CharacterId.None) return id;
            }
        }

        return null;
    }

    #region Growing

    /// <summary>
    /// A child's growth step: its sliders between child and adult values by how far it is to coming of age (drawn again each 1% of growth), and at
    /// the end an adult. True while it was a child at the start of the pass.
    /// </summary>
    bool Grow(World world, int slot, ulong id)
    {
        if (!_book.Lineages.TryGetValue(id, out var lineage) || !lineage.Growing) return false;

        float t = Growth(world, lineage.ComeOfAge);
        if (float.IsNaN(lineage.DrawnGrowth) || t - lineage.DrawnGrowth >= 0.01f || t >= 1)
        {
            lineage.DrawnGrowth = t;
            Restyle(world, slot, record => Genetics.Grow(record, lineage, t));
        }

        if (t >= 1)
        {
            lineage.ComeOfAge = double.NaN;
            lineage.ChildSliders.Clear();
            lineage.AdultSliders.Clear();
        }

        return true;
    }

    #endregion

    #region Publishing

    /// <summary>
    /// The part plans of the children in the world and the player characters' family views; the parents' names are kept up to date while they are
    /// in the world.
    /// </summary>
    void Publish(World world)
    {
        var parts = new Dictionary<CharacterId, PartPlan>();
        foreach (var (id, lineage) in _book.Lineages)
        {
            int slot = SlotOf(world, id);
            if (slot >= 0) parts[world.Characters.IdOf(slot)] = new(lineage.AdultParts.ToFrozenDictionary(StringComparer.Ordinal), lineage.ComeOfAge);
            RefreshNames(world, lineage);
        }

        _parts = parts.ToFrozenDictionary();
        Views = PlayerViews(world);
    }

    static void RefreshNames(World world, Lineage lineage)
    {
        if (SlotOf(world, lineage.Mother) is >= 0 and var m) lineage.MotherName = world.Characters.Cold(m)!.Name;
        if (SlotOf(world, lineage.Father) is >= 0 and var f) lineage.FatherName = world.Characters.Cold(f)!.Name;
    }

    FrozenDictionary<CharacterId, FamilyView> PlayerViews(World world)
    {
        var table = world.Characters;
        var players = new List<(int Slot, ulong Id)>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (Human(world, slot) is { IsPlayer: true, LastingId: var lasting }) players.Add((slot, lasting.Value));
        }

        var views = new Dictionary<CharacterId, FamilyView>();
        foreach (var (slot, id) in players)
        {
            var children = players.Where(p => id != 0 && _book.Lineages.TryGetValue(p.Id, out var l) && (l.Mother == id || l.Father == id))
                .Select(p => table.Cold(p.Slot)!.Name).ToList();
            views[table.IdOf(slot)] = View(world, table.Cold(slot)!.Name, id, children);
        }

        return views.ToFrozenDictionary();
    }

    FamilyView View(World world, string name, ulong id, IReadOnlyList<string> children)
    {
        if (id == 0 || !_book.Lineages.TryGetValue(id, out var l)) return new(name, null, false, null, false, children);

        return new(name, l.FatherName, SlotOf(world, l.Father) < 0, l.MotherName, SlotOf(world, l.Mother) < 0, children);
    }

    #endregion
}
