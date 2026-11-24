using System.Collections.Frozen;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.QueenBirthing;

/// <summary>
/// Runs the births in the serial slow step: the newborn is spawned into the queen's squad beside her
/// (<c>PopulationSystem.SpawnIntoPlayerSquad</c>), then the queen pays the hunger and is knocked out, and the newborn gets its hunger and knock-out
/// (the original's constants). Publishes the player's queens and their hunger every 30th tick for the menu.
/// </summary>
sealed class BirthSystem(BirthSettings settings, IReadOnlyList<BirthKind> kinds, Action<string> log) : ITickSystem
{
    /// <summary>
    /// The original's <c>QUEEN_KNOCKOUT_TIME</c>, <c>DRONE_KNOCKOUT_TIME</c> (seconds) and <c>DRONE_HUNGER</c> (raw hunger the newborn starts at).
    /// </summary>
    const float QueenKnockout = 10, NewbornKnockout = 600, NewbornHunger = 1.75f;

    /// <summary>
    /// The original spawns the newborn 2 units along +X from the queen; when that point is not walkable this port tries rings round her.
    /// </summary>
    const float BirthOffset = 2;

    /// <summary>
    /// The fallback rings' radii (this port's).
    /// </summary>
    static readonly float[] Rings = [12, 20, 30, 45];

    /// <summary>
    /// The births ordered since the last slow step.
    /// </summary>
    readonly List<GiveBirthOrder> _orders = [];

    /// <summary>
    /// The player's living queens and their hunger; null before the first publish.
    /// </summary>
    public static volatile FrozenDictionary<CharacterId, float>? Queens;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is GiveBirthOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Birth(world, order);
        _orders.Clear();

        if (world.Tick % 30 == 0) Queens = FindQueens(world);
    }

    /// <summary>
    /// The player's living queens, by slot order, with their hunger.
    /// </summary>
    static FrozenDictionary<CharacterId, float> FindQueens(World world)
    {
        var table = world.Characters;
        var queens = new Dictionary<CharacterId, float>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive) continue;
            if (table.Cold(slot) is not { IsPlayer: true, Medical: { } medical } cold || !QueenBirthingMod.IsQueen(cold.Race)) continue;

            queens[table.IdOf(slot)] = medical.Hunger;
        }

        return queens.ToFrozenDictionary();
    }

    /// <summary>
    /// One birth: checked against the queen's hunger now (the menu's is up to 30 ticks old), spawned, then paid.
    /// </summary>
    void Birth(World world, GiveBirthOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Queen, out int slot)) return;
        if (table.Cold(slot) is not { IsPlayer: true, Medical: { Dead: false } medical } queen || !QueenBirthingMod.IsQueen(queen.Race)) return;
        if (kinds.FirstOrDefault(k => k.Index == order.Kind) is not { Template: { } template } kind) return;

        if (medical.Hunger < kind.Cost)
        {
            world.Post($"{queen.Name} is too hungry to give birth.");
            return;
        }

        if (Spawn(world, queen.SquadId, table.Next[slot].Position, template) is not { } newborn)
        {
            world.Post($"{queen.Name} cannot give birth here.");
            log($"QueenBirth: no newborn from {template} (no walkable point, or the squad or faction is full)");
            return;
        }

        medical.Hunger -= kind.Cost;
        medical.KnockOut(QueenKnockout);
        SettleNewborn(world, newborn);
        log($"QueenBirth: spawned from {template} with hunger {NewbornHunger} and knocked out for {NewbornKnockout} seconds");
        world.Post($"{queen.Name} gave birth to a {kind.Label}.");
    }

    /// <summary>
    /// The newborn starts at the original's hunger, knocked out, and is handed to the interface for the editor when that setting is on.
    /// </summary>
    void SettleNewborn(World world, CharacterId newborn)
    {
        var table = world.Characters;
        if (table.TryResolveNext(newborn, out int slot) && table.Cold(slot)?.Medical is { } medical)
        {
            medical.Hunger = NewbornHunger;
            medical.KnockOut(NewbornKnockout);
        }

        if (settings.OpenEditorOnBirth) EditorOpener.Request(newborn);
    }

    /// <summary>
    /// The newborn at the original's point beside the queen, else on the first walkable point of the rings; null when none takes it (the
    /// squad or faction is full, docs: <c>SpawnIntoPlayerSquad</c>).
    /// </summary>
    static CharacterId? Spawn(World world, int squad, Vector3 at, string template)
    {
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return null;

        var first = population.SpawnIntoPlayerSquad(world, squad, template, new Vector2(at.X + BirthOffset, at.Z));
        if (first != CharacterId.None) return first;

        foreach (float radius in Rings)
        {
            for (int k = 0; k < 8; k++)
            {
                float angle = k * MathF.PI / 4;
                var point = new Vector2(at.X + radius * MathF.Cos(angle), at.Z + radius * MathF.Sin(angle));
                var id = population.SpawnIntoPlayerSquad(world, squad, template, point);
                if (id != CharacterId.None) return id;
            }
        }

        return null;
    }
}
