using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;

namespace Meitou.Nitoryu;

/// <summary>
/// A pair rule of an add-on (the original's C++ API <c>addPairRule</c>): asked for a fighter's main and second weapon; true lets the pair fight
/// with two blades whatever their kinds (the only way a heavy weapon or polearm pairs), false refuses it, null leaves it to the rules. Called in
/// the serial update and from the training dummies' step: it must be quick, pure and thread-safe.
/// </summary>
public delegate bool? NitoryuPairRule(WeaponData main, WeaponData second);

/// <summary>
/// Events of the dual fighters (the original's API events), raised serially in the tick, after the engine's own work for the blow or block.
/// Listeners may read the world and keep their own state; they must not change the world (queue commands instead).
/// </summary>
public interface INitoryuListener
{
    /// <summary>The second blade landed a blow.</summary>
    void SecondBladeHit(World world, CharacterId attacker, CharacterId victim) { }

    /// <summary>The second blade parried a blow aimed at the defender.</summary>
    void Parried(World world, CharacterId defender, CharacterId attacker) { }

    /// <summary>A special (finisher, opener or the X cut) landed; <paramref name="technique"/> is its COMBAT_TECHNIQUE string id.</summary>
    void Special(World world, CharacterId fighter, string technique) { }

    /// <summary>A dual fighter's rank changed (0 to 5).</summary>
    void RankChanged(World world, CharacterId fighter, int from, int to) { }
}

/// <summary>
/// Nitoryu for add-ons (the original's <c>NitoryuApi.h</c>, as a C# interface): an add-on declares the dependency <c>dxseba.nitoryu</c> in its
/// <c>meitou.json</c> and calls <c>context.Dependency&lt;INitoryuApi&gt;("dxseba.nitoryu")</c> in its <c>Initialize</c>. Reads answer from the
/// state the last serial update settled; registrations are taken only during <c>Initialize</c>, by the caller's mod id, and dropped when that mod
/// fails to initialize.
/// </summary>
public interface INitoryuApi
{
    /// <summary>Whether the character fights with two blades now.</summary>
    bool IsDual(World world, CharacterId character);

    /// <summary>The character's rank (0 to 5) while dual, else 0.</summary>
    int Rank(World world, CharacterId character);

    /// <summary>The character's Path (0 to 100) while dual, else 0.</summary>
    float Path(World world, CharacterId character);

    /// <summary>The character's Dual Wielding as it counts in the fight (armour and overload applied) while dual, else 0.</summary>
    float DualWielding(World world, CharacterId character);

    /// <summary>Adds an event listener for the add-on <paramref name="owner"/>.</summary>
    void AddListener(string owner, INitoryuListener listener);

    /// <summary>Adds a pair rule for the add-on <paramref name="owner"/> (asked in registration order; the first answer wins).</summary>
    void AddPairRule(string owner, NitoryuPairRule rule);
}

/// <summary>The add-ons' registrations, by owner.</summary>
static class Addons
{
    static readonly List<(string Owner, INitoryuListener Listener)> Listeners = [];
    static readonly List<(string Owner, NitoryuPairRule Rule)> Rules = [];

    public static bool Enabled { get; set; } = true;

    public static void Add(string owner, INitoryuListener listener) => Listeners.Add((owner, listener));

    public static void Add(string owner, NitoryuPairRule rule) => Rules.Add((owner, rule));

    public static void Drop(string owner)
    {
        Listeners.RemoveAll(l => l.Owner == owner);
        Rules.RemoveAll(r => r.Owner == owner);
    }

    /// <summary>The first rule's answer for the pair, else null.</summary>
    public static bool? Rule(WeaponData main, WeaponData second)
    {
        if (!Enabled) return null;

        foreach (var (owner, rule) in Rules)
        {
            try
            {
                if (rule(main, second) is { } verdict) return verdict;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"mod       dxseba.nitoryu: pair rule of {owner} failed: {e.Message}");
            }
        }

        return null;
    }

    public static void Raise(Action<INitoryuListener> call)
    {
        if (!Enabled) return;

        foreach (var (owner, listener) in Listeners)
        {
            try
            {
                call(listener);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"mod       dxseba.nitoryu: listener of {owner} failed: {e.Message}");
            }
        }
    }
}
