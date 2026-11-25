using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Locks;

namespace Meitou.SlaverMod;

/// <summary>
/// Free, punish and reward (the original's menu rows), and the hourly share of the daily rules: submission while watched, caged or alone, and
/// the escape roll.
/// </summary>
sealed partial class SlaverSystem
{
    const float ActReach = 100;

    void Act(World world, SlaveOrder order)
    {
        var table = world.Characters;
        ulong lasting = Lasting(world, order.Slave);
        if (order.Act == SlaveAct.Reprogram)
        {
            Reprogram(world, order);
            return;
        }

        if (!table.TryResolveNext(order.Slave, out int s) || table.Cold(s) is not { } c || !PlayerSlave(c)) return;
        if (!table.TryResolveNext(order.Owner, out int o) || table.Cold(o) is not { IsPlayer: true } owner || PlayerSlave(owner)) return;

        if (order.Act is SlaveAct.Cage or SlaveAct.LetOut)
        {
            if (order.Act == SlaveAct.Cage) SendToCage(world, s, lasting);
            else LetOut(world, s, lasting);
            return;
        }

        if (Distance(world, o, s) > ActReach)
        {
            world.Post("Too far away.");
            return;
        }

        var record = _state.Slaves.GetValueOrDefault(lasting) ?? (_state.Slaves[lasting] = new SlaveRecord { Submission = settings.SubmissionStart });
        switch (order.Act)
        {
            case SlaveAct.Free: Free(world, s, c, lasting, order.Owner); break;
            case SlaveAct.Punish when settings.PunishEnabled: Punish(world, c, record, order); break;
            case SlaveAct.Reward when settings.RewardEnabled: Reward(world, c, record, order); break;
        }
    }

    /// <summary>+PunishSubmission (x the race's Punish); each one within the cooldown gives half, then a third, ...</summary>
    void Punish(World world, CharacterCold c, SlaveRecord record, SlaveOrder order)
    {
        double days = (world.GameHours - record.PunishedAt) / 24;
        record.Punishments = days < settings.PunishCooldownDays ? record.Punishments + 1 : 1;
        record.PunishedAt = world.GameHours;
        Raise(record, settings.PunishSubmission * Race(c).Punish / record.Punishments, 100);
        int limb = (int)(Roll() * 4) % 4;
        c.Medical?.PunchDamage(limb, 0, settings.PunishDamage * record.Punishments, population.Bodies.Constants);
        Say(world, order.Owner, "PunishMaster");
        Say(world, order.Slave, "PunishSlave");
    }

    /// <summary>+RewardSubmission for RewardCost cats, once per cooldown.</summary>
    void Reward(World world, CharacterCold c, SlaveRecord record, SlaveOrder order)
    {
        if ((world.GameHours - record.RewardedAt) / 24 < settings.RewardCooldownDays)
        {
            world.Post($"{c.Name} was rewarded recently.");
            return;
        }

        if (world.Player.Money < (long)settings.RewardCost)
        {
            world.Post("Not enough cats.");
            return;
        }

        world.Player.Money -= (long)settings.RewardCost;
        record.RewardedAt = world.GameHours;
        Raise(record, settings.RewardSubmission * Race(c).Gain, 100);
        Say(world, order.Owner, "RewardMaster");
        Say(world, order.Slave, "RewardSlave");
    }

    static void Raise(SlaveRecord record, float by, float cap)
    {
        if (by > 0 && record.Submission >= cap) return;

        record.Submission = Math.Clamp(by > 0 ? Math.Min(record.Submission + by, cap) : record.Submission + by, 0, 100);
    }

    /// <summary>
    /// A game hour went by: each slave's submission changes by a 24th of its daily rate and it rolls a 24th of its daily escape chance.
    /// </summary>
    void Hourly(World world)
    {
        foreach (var (lasting, record) in _state.Slaves.ToList())
        {
            if (!Resolve(world, lasting, out int s, out var c) || !PlayerSlave(c) || c.Medical is { Dead: true })
            {
                _state.Slaves.Remove(lasting);
                continue;
            }

            bool watched = Watched(world, s, c);
            Submit(c, record, watched);
            if (settings.EscapeEnabled && Escapes(c, record, watched)) Escape(world, s, c, lasting, record);
        }
    }

    void Submit(CharacterCold c, SlaveRecord record, bool watched)
    {
        var race = Race(c);
        if (c.IsImprisoned) Raise(record, settings.CageDailyGain * race.Gain / 24, settings.CageSubmissionCap);

        if (watched) Raise(record, settings.SubmissionDailyGain * race.Gain / 24, race.Cap >= 0 ? race.Cap : settings.SubmissionGainCap);
        else Raise(record, -settings.SubmissionUnsupervisedLoss * race.Loss / 24, 100);
    }

    /// <summary>Watched: caged, or a free, awake member of the player's faction within the supervision range.</summary>
    bool Watched(World world, int s, CharacterCold c)
    {
        if (c.IsImprisoned) return true;

        var table = world.Characters;
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (i == s || table.Cold(i) is not { IsPlayer: true, Medical.Incapacitated: false } m || PlayerSlave(m)) continue;
            if (Distance(world, i, s) <= settings.SupervisionRange) return true;
        }

        return false;
    }

    /// <summary>
    /// Daily chance EscapeDailyChance x (1 - submission / EscapeMaxSubmission), x the watched and shackled multipliers and the race's Escape;
    /// never while caged, chained or down.
    /// </summary>
    bool Escapes(CharacterCold c, SlaveRecord record, bool watched)
    {
        if (record.Submission >= settings.EscapeMaxSubmission || c.IsImprisoned || LockSystem.Chained(c)) return false;
        if (c.Medical is { Incapacitated: true }) return false;

        float daily = settings.EscapeDailyChance * (1 - record.Submission / settings.EscapeMaxSubmission) * Race(c).Escape;
        if (watched) daily *= settings.EscapeSupervisedMult;
        if (LockSystem.WornShackles(c) is not null) daily *= settings.EscapeShackledMult;

        return Roll() < daily / 24;
    }

    /// <summary>
    /// The slave runs: an escaped slave (state 2, the player still its owner) in a squad of the faction it was taken from; the shackles come off
    /// only for the strong or the skilled.
    /// </summary>
    void Escape(World world, int s, CharacterCold c, ulong lasting, SlaveRecord record)
    {
        _state.Slaves.Remove(lasting);
        _state.Runaways[lasting] = record;
        var stats = c.Stats;
        bool slips = stats is not null
            && (stats.Strength >= settings.ShackleBreakStrength || stats[StatsEnumerated.Lockpicking] >= settings.ShackleBreakLockpicking);
        if (slips && LockSystem.WornShackles(c)?.Lock is { } l) l.Broken = true;

        var me = world.Characters.IdOf(s);
        Say(world, me, "EscapeSlave");
        GoHome(world, s, c, record.Origin);
        FixtureRegistry.TagEscaped(world, c);
        world.Post($"{c.Name} has escaped!");
    }
}
