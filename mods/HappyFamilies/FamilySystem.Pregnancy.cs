using Meitou.Simulation;
using Meitou.Simulation.Locks;

namespace Meitou.HappyFamilies;

sealed partial class FamilySystem
{
    /// <summary>
    /// A woman's pregnancy step; true when she is pregnant (the pass ends). A failed try's wait that has run out is dropped first, the pass going on.
    /// While pregnant her stomach slider grows by up to 200 and with a third of the time left she says so; at the end her stomach is put back and the
    /// child is born.
    /// </summary>
    bool Advance(World world, int slot, ulong id)
    {
        double today = Today(world);
        if (_book.NextTry.TryGetValue(id, out double next) && next <= today)
        {
            _book.NextTry.Remove(id);
            return false;
        }

        if (!_book.Pregnancies.TryGetValue(id, out var pregnancy)) return false;

        double left = pregnancy.Ends - today;
        if (left <= 0)
        {
            Deliver(world, slot, id, pregnancy);
            return true;
        }

        if (left <= Genetics.PregnancyDays / 3 && !pregnancy.Said)
        {
            Say(world, slot, "I think I'm pregnant...");
            pregnancy.Said = true;
        }

        float stomach = pregnancy.StomachBefore + (float)(1 - left / Genetics.PregnancyDays) * Genetics.BellyGrowth;
        if (float.IsNaN(pregnancy.DrawnStomach) || MathF.Abs(stomach - pregnancy.DrawnStomach) >= 2) SetStomach(world, slot, pregnancy, stomach);

        return true;
    }

    void SetStomach(World world, int slot, Pregnancy pregnancy, float stomach)
    {
        pregnancy.DrawnStomach = stomach;
        Restyle(world, slot, r => r.Floats["Stomach"] = stomach);
    }

    /// <summary>
    /// The end of a pregnancy: her stomach as before, then the child of the father, who stands in for himself when he is still in the world (dead
    /// or alive), else she does (and the couple check then refuses: she is her own relative, as in the original).
    /// </summary>
    void Deliver(World world, int slot, ulong id, Pregnancy pregnancy)
    {
        _book.Pregnancies.Remove(id);
        SetStomach(world, slot, pregnancy, pregnancy.StomachBefore);

        int father = SlotOf(world, pregnancy.Father);
        bool fatherUsable = father >= 0 && world.Characters.Cold(father) is { Appearance.Loadout: not null, Stats: not null, Medical: not null };
        MakeChild(world, slot, fatherUsable ? father : slot);
    }

    /// <summary>
    /// In a bed: each other occupant of a bed of the same bed building who is a man and passes the couple check is a try, 30% a pregnancy, else no
    /// further try for 0.1 to 0.2 days.
    /// </summary>
    void TryConceive(World world, int slot, ulong id)
    {
        var cold = world.Characters.Cold(slot)!;
        if (cold.InSomething != HeldState.Bed || cold.InWhat is not { } bedId || world.Fixtures.Find(bedId) is not { } bed) return;

        foreach (var other in world.Fixtures.OfBuilding(bed.Building))
        {
            if (other.Kind != FixtureKind.Bed || !world.Characters.TryResolveNext(other.Occupant, out int man) || man == slot) continue;
            if (world.Characters.Cold(man) is not { InSomething: HeldState.Bed } held || held.InWhat != other.Id) continue;
            if (Human(world, man) is not { Appearance.Female: false }) continue;

            Try(world, slot, id, man);
        }
    }

    void Try(World world, int slot, ulong id, int man)
    {
        if (CantHaveChild(world, slot, man) is not null) return;

        if (Roll(world, id) <= Genetics.PregnancyChance)
        {
            Conceive(world, slot, id, man);
            return;
        }

        double wait = Genetics.RetryMin + Roll(world, id) * (Genetics.RetryMax - Genetics.RetryMin);
        _book.NextTry[id] = Today(world) + wait;
    }

    void Conceive(World world, int slot, ulong id, int man)
    {
        if (_book.Pregnancies.ContainsKey(id)) return;

        var look = world.Characters.Cold(slot)!.Appearance!.Loadout!.Appearance;
        _book.Pregnancies[id] = new Pregnancy
        {
            Ends = Today(world) + Genetics.PregnancyDays,
            StomachBefore = look.Get("Stomach", Genetics.StomachDefault),
            Father = Lasting(world, man),
        };
    }

    /// <summary>
    /// Why the couple cannot have a child (the original's words), or null: a failed try too recent, no room in her squad or the faction, a race
    /// without a template, a parent still a child, or a shared ancestor within four generations (a parent counts as their own).
    /// </summary>
    string? CantHaveChild(World world, int mother, int father)
    {
        ulong m = Lasting(world, mother), f = Lasting(world, father);
        if (_book.NextTry.TryGetValue(m, out double next) && Today(world) < next) return "Too soon since I last tried";
        if (!RoomFor(world, world.Characters.Cold(mother)!)) return "No space in squad";
        if (!CanReproduce(world.Characters.Cold(mother)!)) return "Mother cant reproduce";
        if (!CanReproduce(world.Characters.Cold(father)!)) return "Father cant reproduce";
        if (Child(m) || Child(f)) return "Not adult";

        return Ancestors(m, 4).Overlaps(Ancestors(f, 4)) ? "Related" : null;
    }

    bool Child(ulong id) => _book.Lineages.TryGetValue(id, out var l) && l.Growing;

    /// <summary>
    /// Room for one more: her squad and the player's faction each at least two below their caps (the original refuses size + 1 &gt;= cap).
    /// </summary>
    bool RoomFor(World world, CharacterCold mother)
    {
        if (world.Squads.Find(mother.SquadId) is not { } squad || squad.Members.Count + 1 >= _data.Bodies.MaxSquadSize) return false;

        int players = 0;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true }) players++;
        }

        return players + 1 < _data.Bodies.MaxFactionSize;
    }

    /// <summary>
    /// A race of the original's table whose <c>HF_Random&lt;name&gt;</c> CHARACTER exists.
    /// </summary>
    bool CanReproduce(CharacterCold cold) =>
        cold.Race is { } race && Genetics.Races.TryGetValue(race.StringId, out string? name) && Template(name) is not null;

    string? Template(string raceName) =>
        context.Data.OfType(Data.Fcs.FcsRecordType.Character).FirstOrDefault(r => r.Name == Genetics.TemplatePrefix + raceName)?.StringId;

    /// <summary>
    /// The character and its forebears up to <paramref name="generations"/> back, by the lineages kept.
    /// </summary>
    HashSet<ulong> Ancestors(ulong id, int generations, HashSet<ulong>? into = null)
    {
        into ??= [];
        if (id == 0) return into;

        into.Add(id);
        if (generations > 0 && _book.Lineages.TryGetValue(id, out var l))
        {
            Ancestors(l.Father, generations - 1, into);
            Ancestors(l.Mother, generations - 1, into);
        }

        return into;
    }
}
