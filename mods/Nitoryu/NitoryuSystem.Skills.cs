using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Gameplay.Construction;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The rank skills that act inside the engine's steps (<see cref="CombatPatches"/>): pressure (rank 3+), the same body part of a twin strike
/// (rank 3+), the left blade onto a flank enemy (rank 4+), the Mugen chain (rank 5), left-hand fighting and the Pair Training Dummies.
/// </summary>
sealed partial class NitoryuSystem
{
    /// <summary>A pair dummy's BUILDING ids (<c>nitoryu pair dummy</c>).</summary>
    HashSet<string>? _dummies;

    ulong Roll(World world, CharacterId who, ulong counter) => Rng.Hash(world.Seed, Rng.Key(who), session.Purpose, counter);

    #region Pressure

    /// <summary>The pressure stacks left on <paramref name="s"/> at <paramref name="now"/> (one fades every <c>decay ms</c>).</summary>
    int Stacks(World world, ref DualFighter s, long now)
    {
        if (s.PressureStacks <= 0) return 0;

        int decay = session.Config.PressureDecayMs;
        int faded = decay > 0 ? (int)(Ms(world, now - s.PressureTick) / decay) : 0;
        return Math.Max(0, s.PressureStacks - faded);
    }

    /// <summary>A block of a dual fighter's hit (rank 3+) piles one stack on the defender, up to the maximum.</summary>
    void Pressed(World world, in CombatDefence defence)
    {
        var config = session.Config;
        if (!config.Pressure || defence.Kind != CombatDefenceKind.Blocked || !_state.IsDual(defence.Attacker.Slot, defence.Attacker)) return;
        if (_state[defence.Attacker.Slot].Tier < config.PressureMinTier || (uint)defence.Defender.Slot >= (uint)_state.Count) return;

        ref var d = ref _state[defence.Defender.Slot];
        int stacks = d.PressureBy == defence.Attacker ? Stacks(world, ref d, defence.Tick) : 0;
        d.PressureStacks = Math.Min(config.PressureMaxStacks, stacks + 1);
        d.PressureBy = defence.Attacker;
        d.PressureTick = defence.Tick;
    }

    /// <summary>A hit the pressured enemy lands on its dual fighter clears its pressure.</summary>
    void Released(in CombatHit hit)
    {
        if ((uint)hit.Attacker.Slot >= (uint)_state.Count) return;

        ref var a = ref _state[hit.Attacker.Slot];
        if (a.PressureBy == hit.Victim) a.PressureStacks = 0;
    }

    /// <summary>The asker's pressure points on its target less the target's on it (the original's <c>whoAttacksYouOrMe</c> score term).</summary>
    public float PressureShift(World world, int asker, int target)
    {
        if (!session.Config.Pressure || (uint)asker >= (uint)_state.Count || (uint)target >= (uint)_state.Count) return 0;

        var table = world.Characters;
        ref var me = ref _state[asker];
        ref var them = ref _state[target];
        float shift = 0;
        if (them.PressureBy == table.IdOf(asker) && me.Dual) shift += Stacks(world, ref them, world.Tick) * session.Config.PressurePoints.At(me.Path);
        if (me.PressureBy == table.IdOf(target) && them.Dual) shift -= Stacks(world, ref me, world.Tick) * session.Config.PressurePoints.At(them.Path);
        return shift;
    }

    /// <summary>The pressure stacks on a character now (for the marks).</summary>
    public int PressureOn(World world, int slot) => (uint)slot < (uint)_state.Count ? Stacks(world, ref _state[slot], world.Tick) : 0;

    #endregion

    #region Twins

    /// <summary>
    /// A twin's first impact notes its part; a landed twin of a Kensei may chain the next twin (25 % at Path 90, 50 % at 100), which then waits
    /// <c>mugen chain window ms</c>.
    /// </summary>
    void TwinLanded(World world, in CombatHit hit)
    {
        ref var s = ref _state[hit.Attacker.Slot];
        if (s.PickSeq != hit.Sequence || Moves.Of(s.PickTech) is not { DoubleHit: true }) return;

        if (s.TwinSeq != hit.Sequence)
        {
            if (Moves.Of(s.PickTech) is { } special && (special.Kind is MoveKind.Finisher or MoveKind.Opener || special.XCut))
            {
                var (who, tech) = (hit.Attacker, special.Tech.StringId);
                Addons.Raise(l => l.Special(world, who, tech));
            }

            (s.TwinSeq, s.TwinPart, s.TwinVictim, s.TwinTick) = (hit.Sequence, hit.Part, hit.Victim, hit.Tick);
            var config = session.Config;
            bool chain = config.Mugen && s.Tier >= config.MugenMinTier
                && Rng.Float(Roll(world, hit.Attacker, (ulong)(uint)hit.Sequence << 8 | 20)) < config.MugenChain.At(s.Path);
            if (chain) s.ChainUntil = hit.Tick + (long)(config.MugenChainWindowMs / 1000f / world.TickSeconds);
        }
    }

    /// <summary>
    /// The part a twin's second impact goes for: the first impact's, on the same victim within <c>scissor window ms</c>, at the scissor chance by
    /// Path (80 % at 60, always from 75); -1 for the game's roll.
    /// </summary>
    public int SamePart(World world, in Effect e)
    {
        var config = session.Config;
        if (!config.Scissor || !_state.IsDual(e.Source)) return -1;

        ref var s = ref _state[e.Source];
        int seq = (int)e.Amount;
        if (s.Tier < 3 || s.TwinSeq != seq || s.PickSeq != seq || s.TwinPart < 0 || s.TwinVictim.Slot != e.Target) return -1;
        if (Ms(world, world.Tick - s.TwinTick) > config.ScissorWindowMs) return -1;

        float roll = Rng.Float(Roll(world, s.Id, (ulong)(uint)seq << 8 | (uint)(e.Arg & 0xff) | 0x40000000UL));
        return roll < config.ScissorChance.At(s.Path) ? s.TwinPart : -1;
    }

    /// <summary>
    /// The flank enemy the second blade's impact of a twin or the X cut lands on (rank 4+): one who attacked the fighter within <c>split
    /// attacker ms</c>, not its target, alive and on its feet, within <c>split arc deg</c> in front and <c>split reach</c> x the second blade's
    /// half length; at the split chance by Path. Null: the blow stays on the target.
    /// </summary>
    public int? Flanker(World world, in Effect e)
    {
        var config = session.Config;
        if (!config.Split || !_state.IsDual(e.Source)) return null;

        ref var s = ref _state[e.Source];
        int seq = (int)e.Amount;
        if (s.Tier < config.SplitMinTier || s.PickSeq != seq || s.Off is null || Moves.Of(s.PickTech) is not { DoubleHit: true } move) return null;
        if (move.Tech.LimbOf(e.Arg) != 0) return null;

        int flank = FlankOf(world, ref s, e.Source, e.Target);
        if (flank < 0) return null;

        float roll = Rng.Float(Roll(world, s.Id, (ulong)(uint)seq << 8 | (uint)(e.Arg & 0xff) | 0x20000000UL));
        return roll < config.SplitChance.At(s.Path) ? flank : null;
    }

    int FlankOf(World world, ref DualFighter s, int me, int target)
    {
        long window = (long)(session.Config.SplitAttackerMs / 1000f / world.TickSeconds);
        foreach (var (who, tick) in new[] { (s.Attacker0, s.Attacker0Tick), (s.Attacker1, s.Attacker1Tick) })
        {
            if (who.IsNone || who.Slot == target || world.Tick - tick > window || !world.Characters.IsAlive(who)) continue;
            if (world.Characters.Cold(who.Slot) is not { Medical: { Incapacitated: false } } || !InFront(world, me, who.Slot)) continue;

            float reach = session.Config.SplitReach * s.Off!.Data.Length * 0.5f;
            if (CombatPatches.Gap(world, me, who.Slot) <= reach) return who.Slot;
        }

        return -1;
    }

    bool InFront(World world, int me, int other)
    {
        var prev = world.Characters.Previous;
        var to = new Vector2(prev[other].Position.X - prev[me].Position.X, prev[other].Position.Z - prev[me].Position.Z);
        if (to.LengthSquared() < 1e-6f) return true;

        var facing = new Vector2(MathF.Sin(prev[me].Yaw), MathF.Cos(prev[me].Yaw));
        float angle = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(to), facing), -1, 1)) * 180 / MathF.PI;
        return angle <= session.Config.SplitArcDegrees * 0.5f;
    }

    /// <summary>Notes who attacked a dual fighter last (the split's flankers).</summary>
    void Attacked(ref DualFighter s, CharacterId attacker, long tick)
    {
        if (s.Attacker0 != attacker) (s.Attacker1, s.Attacker1Tick) = (s.Attacker0, s.Attacker0Tick);
        (s.Attacker0, s.Attacker0Tick) = (attacker, tick);
    }

    #endregion

    #region Left hand and training

    /// <summary>
    /// Left-hand fighting (README "Left-hand fighting"): with the right arm crippled or gone, the left working and Dual Wielding at least 50, a
    /// fighter keeps an allowed one-handed blade; at 50 it deals 70 % damage, attacks and blocks at 85 % and blocks with -10, fading to nothing at
    /// 100. Marked here (serial); the arms step and the filters read it.
    /// </summary>
    void LeftHand(ref DualFighter s, CharacterCold cold)
    {
        var config = session.Config;
        var fighter = cold.Fighter!;
        var medical = cold.Medical!;
        float level = session.Skill.Level(cold.Stats!);
        var blade = fighter.BackWeapon ?? fighter.HipWeapon;
        bool able = config.LeftHand && session.Settings.Enabled && !fighter.IsAnimal && medical.ArmWorks(left: true) && !medical.ArmWorks(left: false)
            && level >= config.LeftHandMinAmbidexterity && blade is not null && session.Pairs.Allowed(blade.Data);
        CombatPatches.MarkLeftHanded(fighter, able);
        s.LeftHand = able && fighter.Weapon is not null && MeleeDrawn(cold);
        float span = 100 - config.LeftHandMinAmbidexterity;
        float t = span > 0 ? Math.Clamp((level - config.LeftHandMinAmbidexterity) / span, 0, 1) : 1;
        s.LeftDamage = config.LeftHandDamageAtMin + (1 - config.LeftHandDamageAtMin) * t;
        s.LeftSpeed = config.LeftHandSpeedAtMin + (1 - config.LeftHandSpeedAtMin) * t;
        s.LeftBlock = s.LeftHand ? config.LeftHandBlockPenaltyAtMin * (1 - t) : 0;
    }

    /// <summary>
    /// A Pair Training Dummy (BUILDING <c>nitoryu pair dummy</c>) trains Dual Wielding up to its <c>output rate</c> (10, 20, 30) for a character
    /// carrying a pair (sheathed is enough), at the training dummies' rate. Runs in the trainee's own step.
    /// </summary>
    public void Train(CharacterCold cold, MachineData data, float dt)
    {
        if (!session.Config.PairDummy || cold is not { Stats: { } stats, Fighter: { } fighter }) return;

        _dummies ??= Pairs.Ticked(session.Data, Data.Fcs.FcsRecordType.Building, "nitoryu pair dummy");
        if (!_dummies.Contains(data.Id) || !Carries(fighter)) return;

        float cap = data.OutputValue;
        if (session.Skill.Level(stats) >= cap) return;

        stats.Gain(session.Skill.Stat, CombatPatches.TrainingPerSecond * dt, cap);
    }

    bool Carries(Fighter fighter) =>
        fighter.BackWeapon is { } back && fighter.HipWeapon is { } hip && session.Pairs.Allowed(back.Data) && session.Pairs.Allowed(hip.Data)
        && (session.Pairs.HasBlade(back.Data) || session.Pairs.HasBlade(hip.Data));

    #endregion
}
