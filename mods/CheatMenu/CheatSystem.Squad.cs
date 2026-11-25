using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;

namespace Meitou.CheatMenu;

/// <summary>Moves one stat's own experience multiplier (XP per skill) to its next value.</summary>
public sealed record CycleSkillXp(StatsEnumerated Stat) : SimCommand, IPausedCommand;

/// <summary>
/// The squad cheats that need more than a filter: XP per skill (the original's per-stat multipliers, the larger of it and Skill XP counting),
/// Free slaves, the player's inventories for Weightless items and Infinite consumables (<see cref="SquadPatches"/>), and the switches kept with
/// the save (the original keeps them while the game runs, across loads; the port keeps them with the save, as the mod's save data).
/// </summary>
internal sealed partial class CheatSystem : IModSaveData, IWorldReady
{
    /// <summary>Stat numbers 0 to 38; 0 is no stat.</summary>
    internal const int SkillSlots = (int)StatsEnumerated.End;

    /// <summary>The per-stat values (<b>Verified</b>: the Skill XP list 1, 2, 5, 10, 50, 100 for each of stats 1 to 38 but 14).</summary>
    internal static readonly float[] SkillValues = [1, 2, 5, 10, 50, 100];

    readonly int[] _skillTier = new int[SkillSlots];

    /// <summary>The player's characters' inventories (rebuilt each tick in the slow world), for the inventory patches.</summary>
    volatile HashSet<Inventory> _playerInventories = new(ReferenceEqualityComparer.Instance);

    /// <summary>The system of the running world, for patches that get no world (inventories); set by the factory.</summary>
    internal static volatile CheatSystem? Current;

    float SkillValue(StatsEnumerated stat) => (int)stat is > 0 and < SkillSlots ? SkillValues[_skillTier[(int)stat]] : 1;

    bool CycleSkill(StatsEnumerated stat)
    {
        int i = (int)stat;
        if (i is <= 0 or >= SkillSlots) return false;

        _skillTier[i] = (_skillTier[i] + 1) % SkillValues.Length;
        return true;
    }

    /// <summary>Whether the inventory is a player character's and <paramref name="which"/> is on.</summary>
    internal bool PlayerInventory(Inventory? inventory, Toggle which) => On(which) && inventory is not null && _playerInventories.Contains(inventory);

    void KeepInventories(World world)
    {
        if (!On(Toggle.Weightless) && !On(Toggle.InfiniteConsumables))
        {
            if (_playerInventories.Count > 0) _playerInventories = new(ReferenceEqualityComparer.Instance);
            return;
        }

        var set = new HashSet<Inventory>(ReferenceEqualityComparer.Instance);
        var table = world.Characters;
        for (int s = 0; s < table.HighWater; s++)
        {
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true, Inventory: { } inventory }) set.Add(inventory);
        }

        _playerInventories = set;
    }

    /// <summary>
    /// Free slaves, remove shackles: every player character's slave state cleared and its worn shackles' lock broken (the game's own step).
    /// </summary>
    static void FreeSlaves(World world)
    {
        var table = world.Characters;
        for (int s = 0; s < table.HighWater; s++)
        {
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true } cold) FixtureRegistry.FreeSlave(world, s, cold);
        }
    }

    // ----- save -----

    const int SaveVersion = 2;

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(SaveVersion);
        WriteInts(w, [.. _on.Select(b => b ? 1 : 0)]);
        WriteInts(w, _tier);
        WriteInts(w, _skillTier);
        WriteInts(w, [.. TakenTowns]);
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = r.ReadInt32();
        if (version is < 1 or > SaveVersion) return;

        var on = ReadInts(r);
        for (int i = 0; i < Math.Min(on.Length, _on.Length); i++) _on[i] = on[i] != 0;
        var tiers = ReadInts(r);
        for (int i = 0; i < Math.Min(tiers.Length, _tier.Length); i++) _tier[i] = Math.Clamp(tiers[i], 0, Tiers.Values((Tier)i).Length - 1);
        var skills = ReadInts(r);
        for (int i = 0; i < Math.Min(skills.Length, _skillTier.Length); i++) _skillTier[i] = Math.Clamp(skills[i], 0, SkillValues.Length - 1);
        if (version >= 2)
        {
            foreach (int site in ReadInts(r)) TakenTowns.Add(site);
        }
    }

    static void WriteInts(BinaryWriter w, int[] values)
    {
        w.Write(values.Length);
        foreach (int v in values) w.Write(v);
    }

    static int[] ReadInts(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n is < 0 or > 1024) throw new InvalidDataException("bad cheat menu save data");

        var values = new int[n];
        for (int i = 0; i < n; i++) values[i] = r.ReadInt32();
        return values;
    }

    /// <summary>After a load the kept rules go back into the constants and the window sees the kept switches.</summary>
    public void Ready(World world, bool fromSave)
    {
        rules.Apply(world, this);
        Publish();
    }
}
