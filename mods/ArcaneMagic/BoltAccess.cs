using System.Collections;
using System.Numerics;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The combat system's internal bolts, reached by reflection (the bolt type <c>Meitou.Simulation.Combat.Projectile</c> is internal): its list
/// (<c>CombatSystem._projectiles</c>, a <c>ProjectileFlight</c> with its <c>_projectiles</c> list and <c>NextId</c>) for the missile volley's extra
/// shards, and <c>CombatSystem.BoltHit</c>, the bolt's whole hit (hit filters, armour, toughness, wounds, knock-downs, XP, relation events, deaths,
/// hit observers), for the spells' hits. All calls are made in the combat system's serial steps. Bound once; a nightly that renames a member
/// leaves <see cref="Ready"/> false (logged by the caller).
/// </summary>
static class BoltAccess
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static Type? s_bolt;
    static FieldInfo? s_flight, s_list;
    static MethodInfo? s_nextId, s_hit;
    static readonly Dictionary<string, FieldInfo> Fields = [];

    public static bool Ready { get; private set; }

    /// <summary>Binds the members; false naming the first one missing.</summary>
    public static bool Bind(out string missing)
    {
        var combat = typeof(CombatSystem);
        s_bolt = combat.Assembly.GetType("Meitou.Simulation.Combat.Projectile");
        var flightType = combat.Assembly.GetType("Meitou.Simulation.Combat.ProjectileFlight");
        s_flight = combat.GetField("_projectiles", Any);
        s_list = flightType?.GetField("_projectiles", Any);
        s_nextId = flightType?.GetMethod("NextId", Any, Type.EmptyTypes);
        s_hit = combat.GetMethod("BoltHit", Any);
        missing = s_bolt is null ? "Projectile" : s_flight is null ? "CombatSystem._projectiles" : s_list is null ? "ProjectileFlight._projectiles"
            : s_nextId is null ? "ProjectileFlight.NextId" : s_hit is null ? "CombatSystem.BoltHit" : "";
        if (missing.Length > 0) return false;

        foreach (string name in (string[])["Id", "Shooter", "ShooterGeneration", "Target", "TargetGeneration", "ShooterFaction", "ShooterIsPlayer",
                     "ShooterSkill", "Position", "Direction", "Speed", "RangeLeft", "Damage", "Passed", "Mesh"])
        {
            if (s_bolt!.GetField(name, Any) is not { } field)
            {
                missing = $"Projectile.{name}";
                return false;
            }

            Fields[name] = field;
        }

        Ready = true;
        return true;
    }

    /// <summary>The combat system's bolt list.</summary>
    static IList List(CombatSystem combat) => (IList)s_list!.GetValue(s_flight!.GetValue(combat))!;

    /// <summary>Bolts in the list now (flying and stuck).</summary>
    public static int Count(CombatSystem combat) => Ready ? List(combat).Count : 0;

    static T Get<T>(object bolt, string field) => (T)Fields[field].GetValue(bolt)!;

    static void Set(object bolt, string field, object value) => Fields[field].SetValue(bolt, value);

    /// <summary>The shooter slot and the direction of the bolt at <paramref name="index"/>.</summary>
    public static (int Shooter, Vector3 Direction) Read(CombatSystem combat, int index)
    {
        object bolt = List(combat)[index]!;
        return (Get<int>(bolt, "Shooter"), Get<Vector3>(bolt, "Direction"));
    }

    /// <summary>Multiplies the damage of the bolt at <paramref name="index"/>.</summary>
    public static void ScaleDamage(CombatSystem combat, int index, float factor)
    {
        var list = List(combat);
        object bolt = list[index]!;
        Set(bolt, "Damage", Get<float>(bolt, "Damage") * factor);
        list[index] = bolt;
    }

    /// <summary>A copy of the bolt at <paramref name="index"/> with its own number, flying along <paramref name="direction"/>.</summary>
    public static void AddShard(CombatSystem combat, int index, Vector3 direction)
    {
        var list = List(combat);
        object shard = CopyOf(list[index]!);
        Set(shard, "Id", s_nextId!.Invoke(s_flight!.GetValue(combat), null)!);
        Set(shard, "Direction", direction);
        list.Add(shard);
    }

    static object CopyOf(object boxed)
    {
        object copy = Activator.CreateInstance(s_bolt!)!;
        foreach (var field in s_bolt!.GetFields(Any)) field.SetValue(copy, field.GetValue(boxed));
        return copy;
    }

    /// <summary>
    /// A spell's hit on the character in <paramref name="victim"/>, through the bolt's hit as a bolt of number <paramref name="id"/> (no mesh, so
    /// nothing sticks) from <paramref name="caster"/>; the packet itself is set by the port's hit filter for that number. False when not bound.
    /// </summary>
    public static bool Hit(CombatSystem combat, World world, int victim, in SpellHit hit)
    {
        if (!Ready) return false;

        object bolt = Activator.CreateInstance(s_bolt!)!;
        Set(bolt, "Id", hit.Id);
        Set(bolt, "Shooter", hit.Caster.Slot);
        Set(bolt, "ShooterGeneration", hit.Caster.Generation);
        Set(bolt, "Target", victim);
        Set(bolt, "TargetGeneration", world.Characters.Previous[victim].Generation);
        Set(bolt, "ShooterFaction", hit.Faction);
        Set(bolt, "ShooterIsPlayer", hit.PlayerCaster);
        Set(bolt, "ShooterSkill", hit.Skill);
        Set(bolt, "Position", hit.From);
        Set(bolt, "Direction", hit.Direction);
        Set(bolt, "Damage", hit.Packet.Cut + hit.Packet.Blunt + hit.Packet.Pierce);
        Set(bolt, "Passed", -1);
        Set(bolt, "Mesh", "");
        s_hit!.Invoke(combat, [world, bolt, victim, hit.Intended]);
        return true;
    }
}

/// <summary>A spell's blow on one character: the bolt number its filter knows it by, who cast it, from where, and the packet.</summary>
readonly record struct SpellHit(int Id, CharacterId Caster, int Faction, bool PlayerCaster, float Skill, Vector3 From, Vector3 Direction,
    DamagePacket Packet, bool Intended);
