using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>
/// Knockout weapon drop: a character newly down (knocked out, crippled on the ground or playing dead: the original's prone states KO, CRIPPLED
/// and PLAYING_DEAD) may drop the weapon it was wielding (<see cref="DisarmRules.KnockoutDropChance"/>).
/// </summary>
sealed partial class DisarmSystem
{
    HashSet<CharacterId> _down = [];
    bool _primed;

    /// <summary>
    /// Compares this slow tick's downed characters with the last; each new one rolls once. The first pass only records, so a loaded game's
    /// downed characters do not roll.
    /// </summary>
    void WatchKnockouts(World world)
    {
        var table = world.Characters;
        var now = new HashSet<CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical.Dead: false } cold || !DisarmRules.Down(cold)) continue;

            var id = table.IdOf(slot);
            now.Add(id);
            if (_primed && !_down.Contains(id)) KnockedOut(world, slot);
        }

        _down = now;
        _primed = true;
    }

    void KnockedOut(World world, int slot)
    {
        var cold = world.Characters.Cold(slot)!;
        if (!settings.KnockoutDrop || !DisarmRules.Targetable(cold, settings) || DisarmRules.Wielded(cold) is not { } weapon) return;

        int chance = DisarmRules.KnockoutDropChance(cold, settings.KnockoutDropMultiplier);
        ulong key = Rng.Hash(world.Seed, Rng.Key(world.Characters.IdOf(slot)) ^ 0x4b4f, purpose, (ulong)world.Tick);
        if (Rng.Float(key) * 100 >= chance) return;

        Remove(world, slot, weapon);
        Rearmed(cold);
        if (settings.Rearm) Track(world.Characters.IdOf(slot), world.Tick);
    }
}
