using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// One character's dual-wielding state, by slot. The serial update (<see cref="NitoryuSystem"/>) writes the first group; the combat hooks write
/// the second, each only for the character whose own step they run in (its attack choice, its defence choice, its blows' Commit).
/// </summary>
struct DualFighter
{
    // Set by the serial update.
    public CharacterId Id;
    public bool Dual;
    public bool Npc;
    public float Ambidexterity;
    public float Effective;
    public float Path;
    public int Tier;
    public WeaponInstance? Off;
    public bool OffIsBack;
    public PairTraits Traits;
    public float Clumsy;
    public float Divided;
    /// <summary>Fighting left-handed with a crippled right arm (not <see cref="Dual"/>), and its damage and speed factor (0.7 / 0.85 at DW 50 to 1).</summary>
    public bool LeftHand;
    public float LeftDamage;
    public float LeftSpeed;
    public float LeftBlock;
    /// <summary>The pair's look: drawn now, and the ticks of the last draw and sheathe (the one-shot tracks).</summary>
    public bool PairDrawn;
    public bool PairOut;
    public long DrawTick;
    public long SheatheTick;
    public bool Chiburi;
    public bool OffHip;
    public long LastFightTick;

    // Set by the combat hooks (ticks; long.MinValue: never).
    public int PickSeq;
    public int PickTech;
    public int LastMove;
    public long LastDouble;
    public long LastFinisher;
    public long LastParry;
    public long LastBlock;
    public long FlowUntil;
    public long FlowReadyAt;
    public long Incoming0;
    public long Incoming1;
    public long Incoming2;
    public float PendingXp;
    public float PendingOffXp;
    public bool OffBlowXp;
    public int OffBlowSeq;
    public int OffBlowBlow;
    public long ParryReported;
    public Dictionary<int, long>? Cooldowns;

    // Pressure on this character (as an enemy of a dual fighter), set in Commit.
    public int PressureStacks;
    public CharacterId PressureBy;
    public long PressureTick;

    // The twin strike in flight (scissor), the Mugen chain, the recent attackers (split).
    public int TwinSeq;
    public int TwinPart;
    public CharacterId TwinVictim;
    public long TwinTick;
    public long ChainUntil;
    public CharacterId Attacker0;
    public long Attacker0Tick;
    public CharacterId Attacker1;
    public long Attacker1Tick;

    public void ResetCombat()
    {
        PickSeq = -1;
        PickTech = -1;
        LastMove = -1;
        LastDouble = LastFinisher = LastParry = LastBlock = long.MinValue / 2;
        FlowUntil = FlowReadyAt = long.MinValue / 2;
        Incoming0 = Incoming1 = Incoming2 = long.MinValue / 2;
        Cooldowns = null;
        PressureStacks = 0;
        PressureBy = CharacterId.None;
        TwinSeq = -1;
        TwinVictim = CharacterId.None;
        ChainUntil = PressureTick = TwinTick = long.MinValue / 2;
        Attacker0 = Attacker1 = CharacterId.None;
        Attacker0Tick = Attacker1Tick = DrawTick = SheatheTick = LastFightTick = long.MinValue / 2;
    }
}

/// <summary>The dual-wielding state of one world's characters, by slot (grown in the serial update only).</summary>
sealed class DualState
{
    DualFighter[] _slots = [];

    public int Count => _slots.Length;

    public ref DualFighter this[int slot] => ref _slots[slot];

    /// <summary>The state of <paramref name="slot"/> if it is the character <paramref name="id"/> and dual; else null.</summary>
    public bool IsDual(int slot, CharacterId id) => (uint)slot < (uint)_slots.Length && _slots[slot].Dual && _slots[slot].Id == id;

    /// <summary>Dual, or fighting left-handed, as the character <paramref name="id"/>.</summary>
    public bool IsActive(int slot, CharacterId id) =>
        (uint)slot < (uint)_slots.Length && (_slots[slot].Dual || _slots[slot].LeftHand) && _slots[slot].Id == id;

    public bool IsLeftHanded(int slot, CharacterId id) => (uint)slot < (uint)_slots.Length && _slots[slot].LeftHand && _slots[slot].Id == id;

    public bool IsDual(int slot) => (uint)slot < (uint)_slots.Length && _slots[slot].Dual;

    /// <summary>Makes room for <paramref name="count"/> slots (serial only).</summary>
    public void Grow(int count)
    {
        if (count <= _slots.Length) return;

        int old = _slots.Length;
        Array.Resize(ref _slots, Math.Max(count, old * 2));
        for (int i = old; i < _slots.Length; i++) _slots[i].ResetCombat();
    }
}
