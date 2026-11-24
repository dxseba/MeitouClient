using System.Collections.Frozen;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Editing;
using Meitou.Simulation.Population;

namespace Meitou.HappyFamilies;

/// <summary>
/// What the family window shows for a player character: its name, its parents' names (gone: no longer in the world) and its children's names.
/// </summary>
sealed record FamilyView(string Name, string? Father, bool FatherGone, string? Mother, bool MotherGone, IReadOnlyList<string> Children);

/// <summary>
/// A growing child's adult part maxima and the game day it comes of age (NaN: grown), for the part filter.
/// </summary>
sealed record PartPlan(FrozenDictionary<string, float> Adult, double ComeOfAge);

/// <summary>
/// The families: every <see cref="Cadence"/> ticks, in the serial slow step, each living human player character grows if it is a child, else a
/// woman's pregnancy advances or, in bed, she may conceive with a man in another bed of the same bed building; a pregnancy's end spawns the child.
/// Saved with the game (<see cref="IModSaveData"/>); growing children's body parts are scaled by <see cref="IPartMaxFilter"/>.
/// </summary>
sealed partial class FamilySystem(ModContext context) : IModSaveData, IWorldReady, IPartMaxFilter, IStateHashed
{
    /// <summary>
    /// Ticks between passes (the original runs every 40th character update).
    /// </summary>
    const int Cadence = 30;

    readonly FamilyBook _book = new();
    readonly PopulationData _data = context.Population;
    ulong _rolls;

    volatile FrozenDictionary<CharacterId, PartPlan> _parts = FrozenDictionary<CharacterId, PartPlan>.Empty;

    /// <summary>
    /// The player characters' family views for the window; null before the first pass.
    /// </summary>
    public static volatile FrozenDictionary<CharacterId, FamilyView>? Views;

    public void Write(Stream stream) => _book.Write(stream);

    public void Read(Stream stream)
    {
        _book.Read(stream);
        _readOwn = true;
    }

    /// <summary>
    /// At load, a save without this port's data gives the original's links (<see cref="ImportOriginal"/>); then the views.
    /// </summary>
    public void Ready(World world, bool fromSave)
    {
        if (fromSave && !_readOwn) ImportOriginal(world);
        Publish(world);
    }

    /// <summary>
    /// The family state (it decides tries and births) and the roll counter, in key order.
    /// </summary>
    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_rolls);
        _book.Hash(ref hasher);
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % Cadence != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (Human(world, slot) is { IsPlayer: true } cold) Pass(world, slot, cold);
        }

        Publish(world);
    }

    /// <summary>
    /// One character's pass, in the original's order: a child only grows; a woman's pregnancy advances; else, awake in bed, she may conceive.
    /// </summary>
    void Pass(World world, int slot, CharacterCold cold)
    {
        ulong id = Lasting(world, slot);
        if (Grow(world, slot, id)) return;
        if (!cold.Appearance!.Female) return;
        if (Advance(world, slot, id)) return;
        if (cold.Medical!.Unconscious) return;

        TryConceive(world, slot, id);
    }

    public float Max(World world, in PartMaxAsk ask)
    {
        if (!_parts.TryGetValue(ask.Character, out var plan) || !plan.Adult.TryGetValue(ask.Record, out float adult)) return ask.Max;

        return Genetics.PartMax(adult, Growth(world, plan.ComeOfAge));
    }

    #region Helpers

    static double Today(World world) => world.GameHours / 24;

    /// <summary>
    /// Growth 0 (newborn) to 1 (adult) of a child coming of age on <paramref name="comeOfAge"/>; 1 when NaN.
    /// </summary>
    static float Growth(World world, double comeOfAge) => double.IsNaN(comeOfAge)
        ? 1
        : (float)Math.Clamp(1 - (comeOfAge - Today(world)) / Genetics.ChildhoodDays, 0, 1);

    /// <summary>
    /// A living human (not an animal) with a look and a body, else null.
    /// </summary>
    static CharacterCold? Human(World world, int slot) =>
        world.Characters.Next[slot].Alive && world.Characters.Cold(slot) is
            { Appearance: { Animal: null, Loadout: not null }, Medical.Dead: false, Stats: not null } cold
            ? cold
            : null;

    static ulong Lasting(World world, int slot) => world.LastingIds.GetOrGive(world.Characters.IdOf(slot)).Value;

    /// <summary>
    /// The slot of the character with lasting id <paramref name="id"/> in the world now (dead or alive), else -1.
    /// </summary>
    static int SlotOf(World world, ulong id)
    {
        var found = world.LastingIds.Find(new LastingId(id));
        return id != 0 && world.Characters.TryResolveNext(found, out int slot) ? slot : -1;
    }

    /// <summary>
    /// A number in [0, 1) from the world seed, the mod's purpose and a running counter (deterministic).
    /// </summary>
    float Roll(World world, ulong key) => Rng.Float(Rng.Hash(world.Seed, key, context.RngPurpose, ((ulong)world.Tick << 16) + _rolls++));

    void Say(World world, int slot, string english) => world.Bark(world.Characters.IdOf(slot), context.Tr(english));

    /// <summary>
    /// Changes the character's look through the editor's path (a recruit's edit, race and sex kept): a copy of its look record, edited, drawn again.
    /// </summary>
    void Restyle(World world, int slot, Action<FcsRecord> edit)
    {
        var cold = world.Characters.Cold(slot)!;
        var record = CharacterDraft.Copy(cold.Appearance!.Loadout!.Appearance.Record);
        edit(record);
        CharacterEditing.Apply(world, _data, new EditCharacter(world.Characters.IdOf(slot), record, cold.Name, EditKind.Recruit) { Tick = world.Tick });
    }

    #endregion
}
