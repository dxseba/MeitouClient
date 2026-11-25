using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>The move a dual fighter's choice took, null for the game's own pick.</summary>
readonly record struct MovePick(Move? Move, bool Chain = false);

/// <summary>
/// A dual fighter's attack choice (README "The ranks"; FCS.md "nitoryu move"), all rolls drawn from the fighter, its attack number and the mod's
/// purpose:
/// <list type="number">
/// <item>below rank 1 the game's pick stands;</item>
/// <item>out of the game's reach: an opener (rank 3+, Dual Wielding 50, the enemy <c>min gap</c> to <c>max gap</c> beyond reach, its <c>roll</c>,
/// higher <c>chance</c> first), else a strike that steps in (the big chop), each on its own cooldown;</item>
/// <item>the X cut and any finisher (rank 3+, Dual Wielding 50) on a staggering enemy, or after the fighter's block at <c>cross riposte
/// chance</c>, at most once in 3 s;</item>
/// <item>the second blade's turn at the off-hand share by Path (always for a riposte after a block, rank 3+, at the counter chance): a twin strike
/// at the twin share (rank 3+, x1.3 for equal blades) off the double-hit cooldown (5 s, Mugen 3.5 to 2 s), else a strike with the second blade;</item>
/// <item>else, with only my strikes, a main-blade strike of the pool at the main move share; else the game's pick.</item>
/// </list>
/// Within a pool the weight is the game's (closeness of the gap to the technique's distance, times <c>chance</c>), base strikes at x0.5 from rank
/// 3; a technique needs Path at least its <c>min skill</c>, its clip when <c>nitoryu needs clip</c> is set, and is not the fighter's last one.
/// </summary>
readonly ref struct MovePicker(NitoryuSystem system, World world, int slot, ulong key, int seq, int now)
{
    const int FinisherCooldownMs = 3000;

    NitoryuConfig Config => system.Session.Config;

    ref DualFighter State => ref system.State[slot];

    float Roll(int n) => Rng.Float(Rng.Hash(world.Seed, key, system.Session.Purpose, (ulong)(uint)seq << 8 | (uint)n));

    float Ms(long ticks) => ticks * world.TickSeconds * 1000;

    public MovePick Choose(in AttackQuery q, int vanilla, ref float maxReach)
    {
        ref var s = ref State;
        if (s.Tier < 1 || system.Moves.All.Count == 0) return default;

        float reach = maxReach;
        float far = FarReach(q, s.Tier, reach);
        if (far > maxReach) maxReach = far;
        if (q.Gap > reach)
        {
            // Nothing to open or step in with from here: the game's reach, so the fighter walks in.
            var from = Far(q, reach);
            if (from is null) maxReach = reach;
            return new MovePick(from);
        }

        if (Chained(q) is { } chained) return new MovePick(chained, Chain: true);
        if (Finisher(q) is { } finisher) return new MovePick(finisher);
        if (OffTurn(ref s)) return new MovePick(Second(q, ref s));
        if (system.Session.Settings.OnlyMyStrikes && Config.OnlyMyStrikes && Roll(3) < Config.MainMoveShare) return new MovePick(MainStrike(q));

        return default;
    }

    /// <summary>The second blade's turn: always for a riposte (rank 3+, within 1.5 s of a block, the counter chance), else the off-hand share.</summary>
    bool OffTurn(ref DualFighter s)
    {
        bool riposte = Config.Counter && s.Tier >= Config.CounterMinTier && Ms(now - s.LastBlock) <= Config.CounterWindowMs;
        if (riposte && Roll(4) < Config.CounterChance.At(s.Path)) return true;

        return Roll(1) < Config.OffHandShare.At(s.Path);
    }

    Move? Second(in AttackQuery q, ref DualFighter s)
    {
        float twin = Config.TwinShare.At(s.Path) * (Config.PairBonuses && s.Traits.Equal ? Config.PairEqualTwinShare : 1);
        if (s.Tier >= 3 && DoubleReady(ref s) && Roll(2) < twin && Weighted(q, m => m.Kind == MoveKind.Twin && !m.XCut, 5) is { } t) return t;

        return Weighted(q, m => m.Kind == MoveKind.Strike && m.UsesSecondBlade, 6);
    }

    /// <summary>A Kensei's chained twin (Mugen): a twin that landed may pull the next one within the chain window, whatever the cooldown.</summary>
    Move? Chained(in AttackQuery q)
    {
        ref var s = ref State;
        if (s.Tier < Config.MugenMinTier || now >= s.ChainUntil) return null;

        return Weighted(q, m => m.Kind == MoveKind.Twin && !m.XCut, 12, chain: true);
    }

    Move? MainStrike(in AttackQuery q) => Weighted(q, m => m.Kind == MoveKind.Strike && !m.UsesSecondBlade, 7);

    Move? Finisher(in AttackQuery q)
    {
        ref var s = ref State;
        if (!Specials(ref s) || !DoubleReady(ref s) || Ms(now - s.LastFinisher) < FinisherCooldownMs) return null;

        bool afterBlock = Config.CounterWindowMs > 0 && Ms(now - s.LastBlock) <= Config.CrossRiposteWindowMs && Roll(8) < Config.CrossRiposteChance;
        if (!q.TargetStaggered && !afterBlock) return null;

        return Weighted(q, m => m.Kind == MoveKind.Finisher || m.XCut, 9);
    }

    /// <summary>
    /// Out of the game's reach: an opener whose gap window holds the enemy (higher <c>chance</c> first, its <c>roll</c> drawn per tick), else a
    /// strike that steps in from there.
    /// </summary>
    Move? Far(in AttackQuery q, float reach)
    {
        ref var s = ref State;
        float beyond = q.Gap - reach;
        if (Specials(ref s) && DoubleReady(ref s))
        {
            foreach (var m in system.Moves.All.Where(m => m.Kind == MoveKind.Opener).OrderByDescending(m => m.Tech.Chance))
            {
                if (!Usable(m, ref s) || beyond < m.MinGap || beyond > m.MaxGap) continue;
                if (Rng.Float(Rng.Hash(world.Seed, key, system.Session.Purpose, (ulong)(uint)now << 8 | 10)) < m.Roll) return m;

                return null;
            }
        }

        bool twins = s.Tier >= 3;
        float half = q.WeaponLength * 0.5f;
        return Weighted(q, m => (m.Kind == MoveKind.Strike || twins && m.Kind == MoveKind.Twin) && m.Tech.AttackDistance + half > reach, 11);
    }

    /// <summary>The longest reach the fighter may strike from now: the ready openers' windows and stepping strikes.</summary>
    float FarReach(in AttackQuery q, int tier, float reach)
    {
        ref var s = ref State;
        float best = reach;
        foreach (var m in system.Moves.All)
        {
            if (!Usable(m, ref s)) continue;

            if (m.Kind == MoveKind.Opener)
            {
                if (Specials(ref s) && DoubleReady(ref s)) best = MathF.Max(best, reach + m.MaxGap);
            }
            else if (m.Kind == MoveKind.Strike || m.Kind == MoveKind.Twin && tier >= 3 && DoubleReady(ref s))
            {
                best = MathF.Max(best, ReachOf(m, q));
            }
        }

        return best;
    }

    static float ReachOf(Move m, in AttackQuery q) => m.Tech.AttackDistance + q.WeaponLength * 0.5f;

    bool Specials(ref DualFighter s) => s.Tier >= 3 && s.Effective >= Config.SpecialMinAmbidexterity;

    bool DoubleReady(ref DualFighter s)
    {
        float cooldown = Config.Mugen && s.Tier >= Config.MugenMinTier ? Config.MugenCooldown.At(s.Path) : Config.DoubleHitCooldownMs;
        return Ms(now - s.LastDouble) >= cooldown;
    }

    /// <summary>Path at least its <c>min skill</c>, its clip when it needs one, off its own cooldown, not the last move (no repeat).</summary>
    bool Usable(Move m, ref DualFighter s, bool chain = false)
    {
        if (s.Path < m.Tech.MinSkill || s.Path > m.Tech.MaxSkill) return false;
        if (m.NeedsClip && (!system.Session.Settings.Animations || !PoseLayer.HasTrack(m.Track))) return false;
        if (Config.NoRepeat && m.Index == s.LastMove) return false;
        if (m.CooldownMs > 0 && s.Cooldowns is { } cd && cd.TryGetValue(m.Index, out long last) && Ms(now - last) < m.CooldownMs) return false;

        return chain || !m.DoubleHit || DoubleReady(ref s);
    }

    Move? Weighted(in AttackQuery q, Func<Move, bool> pool, int rollIndex, bool chain = false)
    {
        ref var s = ref State;
        var moves = system.Moves.All;
        Span<float> weights = stackalloc float[moves.Count];
        float total = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            var m = moves[i];
            weights[i] = pool(m) && Usable(m, ref s, chain) ? Weight(m, q, s.Tier) : 0;
            total += weights[i];
        }

        if (total <= 0) return null;

        float target = Roll(rollIndex) * total;
        Move? last = null;
        for (int i = 0; i < moves.Count; i++)
        {
            if (weights[i] <= 0) continue;

            last = moves[i];
            target -= weights[i];
            if (target < 0) return moves[i];
        }

        return last;
    }

    /// <summary>The game's weight (<c>TechniqueChooser</c> pass): the gap's closeness to the technique's distance, times <c>chance</c>.</summary>
    float Weight(Move m, in AttackQuery q, int tier)
    {
        var t = m.Tech;
        float half = q.WeaponLength * 0.5f;
        if (q.Gap > t.AttackDistance + half) return 0;

        float actual = q.Gap + 3;
        float ideal = q.Scale * (t.AttackDistance + half * 0.5f) + 3;
        float w = (ideal <= actual ? ideal / actual : actual / ideal) * t.Chance;
        if (m.BaseStrike && tier >= 3) w *= Config.BaseStrikeWeightHigh;
        return MathF.Max(w, 0);
    }

    /// <summary>The pick became a swing: the attack number its blows carry, the move's cooldowns.</summary>
    public static void Commit(NitoryuSystem system, World world, int slot, MovePick pick, int attackSeq, int technique, int now)
    {
        ref var s = ref system.State[slot];
        s.PickSeq = attackSeq;
        s.PickTech = pick.Move is { } m && m.Index == technique ? m.Index : -1;
        s.LastMove = technique;
        if (pick.Move is not { } move || move.Index != technique) return;

        if (move.DoubleHit) s.LastDouble = now;
        if (pick.Chain) s.ChainUntil = long.MinValue / 2;
        if (move.Kind == MoveKind.Finisher || move.XCut) s.LastFinisher = now;
        if (move.CooldownMs > 0) (s.Cooldowns ??= [])[move.Index] = now;
    }
}
