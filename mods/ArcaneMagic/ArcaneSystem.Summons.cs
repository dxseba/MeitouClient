using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The Summon (README "Facts"): Summonathans made beside the caster (at caster + (2 + 3i, 0, 2i)), up to the cap less those alive, staying 150
/// seconds, then removed. A player's join its squad; an NPC Magus's are of its faction, in a squad of their own. Each is marked
/// <c>AM_temporary_summon</c> = 1 in its STATS record, as the original marks it.
/// </summary>
sealed partial class ArcaneSystem
{
    /// <summary>The original's STATS key marking a summoned creature.</summary>
    const string TemporaryKey = "AM_temporary_summon";

    readonly List<(CharacterId Caster, int Squad, int Count)> _summonOrders = [];

    /// <summary>Living summons: the creature, its caster and the seconds it has left.</summary>
    readonly List<(CharacterId Creature, CharacterId Caster, float Left)> _summons = [];

    int SummonsOf(CharacterId caster) => _summons.Count(s => s.Caster == caster);

    /// <summary>The summon orders of the last tick's casts, made in the serial input step (where the population's spawns are made).</summary>
    void RunSummonOrders(World world)
    {
        foreach (var (caster, squad, count) in _summonOrders)
        {
            if (_population is null || !world.Characters.TryResolveNext(caster, out int slot) || world.Characters.Cold(slot) is not { } cold) continue;

            var at = world.Characters.Previous[slot].Position;
            for (int i = 0; i < count; i++)
            {
                var place = new Vector2(at.X + i * 3 + 2, at.Z + i * 2);
                var made = cold.IsPlayer
                    ? _population.SpawnIntoPlayerSquad(world, squad, ArcaneRules.Summonathan, place, new SpawnOptions(IgnoreCaps: true))
                    : _population.SpawnSandbox(world, ArcaneRules.Summonathan, place, player: false, yaw: world.Characters.Previous[slot].Yaw,
                        faction: cold.Faction);
                if (made.IsNone) continue;

                world.LastingIds.GetOrGive(made);
                world.SaveFields.SetFloat(made, CharacterSaveRecord.Stats, TemporaryKey, 1);
                _summons.Add((made, caster, ArcaneRules.SummonLife));
                Visuals.Enqueue(new Visual(ArcaneRules.Flash, new Vector3(place.X, at.Y, place.Y), CharacterId.None, 2));
            }
        }

        _summonOrders.Clear();
    }

    void ExpireSummons(World world, float dt)
    {
        for (int k = 0; k < _summons.Count; k++)
        {
            var s = _summons[k];
            s.Left -= dt;
            _summons[k] = s;
            if (s.Left > 0 && world.Characters.IsAlive(s.Creature)) continue;

            _summons.RemoveAt(k--);
            world.Characters.Remove(s.Creature);
        }
    }

    /// <summary>
    /// After a load: a creature marked <c>AM_temporary_summon</c> that the port's save data does not list (a save made without the port, or by the
    /// original) is removed at once, its time unknown.
    /// </summary>
    void DropStraySummons(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            var id = table.IdOf(slot);
            if (!table.Previous[slot].Alive || _summons.Any(s => s.Creature == id)) continue;

            if (world.SaveFields.Float(id, CharacterSaveRecord.Stats, TemporaryKey) is > 0) _summons.Add((id, CharacterId.None, 0));
        }
    }

    void HashSummons(ref StateHasher h)
    {
        foreach (var s in _summons)
        {
            h.Add(s.Creature);
            h.Add(s.Left);
        }

        h.Add(_summonOrders.Count);
    }
}
