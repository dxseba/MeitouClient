using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>
/// Religion (the original's [Religion], every ReligionTickHours 24): state faiths seeded from the factions (the Holy Nation's Okranism, the
/// Shek's Kral, the hives'), towns converting slowly to their owner's faith, persecution, schisms and heresies of weak faiths, cults founded by
/// NPCs (cooldown 150 days, NPCCultChance 4 %), and the religious war's +4 in the declaration chance.
/// </summary>
sealed partial class WorldSim
{
    /// <summary>The faith a faction starts with, from its name (port's mapping; the original reads [FactionReligion] overrides).</summary>
    string SeedFaith(World world, string faction)
    {
        string name = Name(world, faction);
        (string id, string shown, bool militant) = name switch
        {
            _ when name.Contains("Holy", StringComparison.OrdinalIgnoreCase) || name.Contains("Okran", StringComparison.OrdinalIgnoreCase) =>
                ("okranism", "Okranism", true),
            _ when name.Contains("Shek", StringComparison.OrdinalIgnoreCase) => ("kral", "Path of Kral", true),
            _ when name.Contains("Hive", StringComparison.OrdinalIgnoreCase) => ("hive", "The Queen's Song", false),
            _ when name.Contains("Flotsam", StringComparison.OrdinalIgnoreCase) => ("narko", "Narko's Heresy", false),
            _ => ("secular", "secular", false),
        };
        if (id != "secular" && _state.FindFaith(id) is null) _state.Faiths.Add(new Faith { Id = id, Name = shown, Militant = militant });
        return id;
    }

    /// <summary>A faith's shown name.</summary>
    string FaithName(string id) => _state.FindFaith(id)?.Name ?? tr(id);

    /// <summary>Whether a war between the two would be holy: different state faiths, one militant.</summary>
    bool HolyWar(Nation a, Nation b) =>
        a.Religion != b.Religion && (_state.FindFaith(a.Religion)?.Militant == true || _state.FindFaith(b.Religion)?.Militant == true);

    /// <summary>A day's religion: conversion, persecution, schisms and heresies, cults.</summary>
    void Religion(World world)
    {
        foreach (var n in _state.Nations)
        {
            foreach (int site in TownsOf(world, n.Faction))
            {
                var t = Town(world, site);
                if (t.Religion == n.Religion) continue;

                // Conversion takes the town about a month (3 % a day); a militant state persecutes the minority (unrest).
                if (Chance(world, 3, 70))
                {
                    t.Religion = n.Religion;
                    Say(world, Fmt("{0} converts to {1}", TownName(world, site), FaithName(n.Religion)), popup: false);
                }
                else if (_state.FindFaith(n.Religion)?.Militant == true)
                {
                    t.Unrest = Math.Min(100, t.Unrest + 2);
                }
            }
        }

        foreach (var f in _state.Faiths.ToList()) Schism(world, f);
        Cult(world);
    }

    /// <summary>A faith whose authority fell below 30 splits (Schisms, 5 %); authority drifts back toward 60.</summary>
    void Schism(World world, Faith f)
    {
        int unrest = _state.Towns.Where(t => t.Religion == f.Id).Sum(t => t.Unrest);
        f.Authority = Math.Clamp(f.Authority + Math.Sign(60 - f.Authority) - unrest / 400, 0, 100);
        if (f.Authority >= 30 || !Chance(world, 5, 71)) return;

        var split = new Faith { Id = f.Id + ".reformed" + _state.NextId++, Name = tr("Reformed") + " " + f.Name, Authority = 50 };
        _state.Faiths.Add(split);
        f.Authority += 20;
        var towns = _state.Towns.Where(t => t.Religion == f.Id && t.Unrest > 20).ToList();
        foreach (var t in towns) t.Religion = split.Id;
        Say(world, Fmt("{0} split from {1} after rejecting the authority of the central faith.", split.Name, f.Name), popup: true);
    }

    /// <summary>An NPC founds a cult in an unhappy town (cooldown 150 days, 4 %); it may raise an emergent faction.</summary>
    void Cult(World world)
    {
        if (world.GameHours - _state.LastCult < 150 * 24 || !Chance(world, 4, 72)) return;

        var t = _state.Towns.Where(x => x.Unrest >= 20).OrderByDescending(x => x.Unrest).ThenBy(x => x.Site).FirstOrDefault();
        if (t is null) return;

        _state.LastCult = world.GameHours;
        string founder = First[Below(world, First.Length, 73)] + Last[Below(world, Last.Length, 74)];
        var cult = new Faith { Id = "cult" + _state.NextId++, Name = Fmt("Cult of {0}", founder), Authority = 40, Founder = founder };
        _state.Faiths.Add(cult);
        t.Religion = cult.Id;
        Say(world, Fmt("{0} gathered followers and proclaimed {1}{2}. Authorities watch the cult with suspicion.", founder, cult.Name, ""),
            popup: true);
        if (Chance(world, 30, 75)) Emerge(world, t.Site, "cult");
    }
}
