using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.BodyMorphCap;

/// <summary>
/// The capped body: a postfix on <c>World.FillBody</c> (the publish of a character's drawn body, docs/animation.md "Stat-driven body") that,
/// when the engine published a build, replaces it with one made from the capped stats. The bone shape follows the engine's rule (made once,
/// rebuilt only while the character is of the player's faction), kept here for the capped values; display only, nothing in the world changes.
/// </summary>
static class BodyPatch
{
    /// <summary>The capped bone shape of each character and the settings version it was made under.</summary>
    sealed class Shaped
    {
        public BodyBuild Build;
        public int Version;
    }

    static readonly ConditionalWeakTable<CharacterCold, Shaped> Shapes = new();

    static Action<CharacterSnapshot, BodyBuild?>? s_setBuild;
    static ModContext? s_context;
    static int s_version;
    static int s_faulted;
    static int s_logged;

    const int DebugLines = 20;

    public static void Install(ModContext context)
    {
        s_context = context;
        var setter = AccessTools.PropertySetter(typeof(CharacterSnapshot), nameof(CharacterSnapshot.Build));
        var fill = AccessTools.Method(typeof(World), "FillBody");
        if (setter is null || fill is null)
        {
            context.LogError("World.FillBody or CharacterSnapshot.Build not found: bodies are drawn as without the mod");
            return;
        }

        s_setBuild = AccessTools.MethodDelegate<Action<CharacterSnapshot, BodyBuild?>>(setter);
        context.Harmony.Patch(fill, postfix: new HarmonyMethod(typeof(BodyPatch), nameof(FillBodyPostfix)));
    }

    /// <summary>A changed cap re-makes every capped bone shape at the next publish (the original waits for the body to be rebuilt).</summary>
    public static void SettingsChanged() => Interlocked.Increment(ref s_version);

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void FillBodyPostfix(World __instance, CharacterSnapshot s, int i)
    {
        try
        {
            if (BodyMorphCapMod.Settings.Enabled && s.Build is not null) Cap(__instance, s, i);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"capping a body failed, left as the engine drew it: {e}");
        }
    }
#pragma warning restore IDE1006

    static void Cap(World world, CharacterSnapshot s, int i)
    {
        var cold = world.Characters.Cold(i);
        if (cold is not { Stats: { } stats, Medical: { } fed, Appearance: { Animal: null } look }) return;
        if (cold.IsPlayer && !BodyMorphCapMod.Settings.ApplyToPlayer) return;

        var caps = Caps.Current();
        var live = CharacterShape.BuildFromStats(look.Female, (Stats: stats, Caps: caps), static (p, stat) => p.Caps.Clamp(stat, p.Stats.Stored(stat)),
            fed.Hunger);
        var shape = ShapeOf(world, cold, live);
        s_setBuild!(s, live.WithShapeOf(shape));
        LogClamp(cold, stats, caps);
    }

    /// <summary>
    /// The capped bone shape: made at the first publish and remade while the character is of the player's faction (the engine's
    /// <c>WorldPublish.ShapedBuild</c> rule), or after a setting changed.
    /// </summary>
    static BodyBuild ShapeOf(World world, CharacterCold cold, BodyBuild live)
    {
        int version = Volatile.Read(ref s_version);
        var shaped = Shapes.GetValue(cold, _ => new Shaped { Build = live, Version = version });
        bool player = cold.Faction >= 0 && cold.Faction == world.Player.Faction;
        if (player || shaped.Version != version)
        {
            shaped.Build = live;
            shaped.Version = version;
        }

        return shaped.Build;
    }

    static void LogClamp(CharacterCold cold, CharacterStats stats, Caps caps)
    {
        if (!BodyMorphCapMod.Settings.Debug || !caps.Binds(stats) || Interlocked.Increment(ref s_logged) > DebugLines) return;

        s_context?.Log($"clamped {cold.Name}: strength {stats.Stored(StatsEnumerated.Strength):0} to {caps.Strength:0}, "
            + $"weapon smith {stats.Stored(StatsEnumerated.SmithingWeapon):0}, armour smith {stats.Stored(StatsEnumerated.SmithingArmour):0}, "
            + $"definition cap {caps.Definition:0}");
    }
}

/// <summary>
/// The caps as read once per body: Strength, Weapon Smithing, Armour Smithing (bulk) and one cap for Dexterity, Martial Arts, Swimming and
/// Athletics (definition); every other stat passes as it is (the original clamps these seven <c>CharStats</c> fields only).
/// </summary>
readonly record struct Caps(float Strength, float WeaponSmith, float ArmourSmith, float Definition)
{
    public static Caps Current()
    {
        var s = BodyMorphCapMod.Settings;
        return new(s.Cap, s.CapWeaponSmith, s.CapArmourSmith, s.CapMuscle);
    }

    public float Clamp(StatsEnumerated stat, float value) => stat switch
    {
        StatsEnumerated.Strength => MathF.Min(value, Strength),
        StatsEnumerated.SmithingWeapon => MathF.Min(value, WeaponSmith),
        StatsEnumerated.SmithingArmour => MathF.Min(value, ArmourSmith),
        StatsEnumerated.Dexterity or StatsEnumerated.MartialArts or StatsEnumerated.Swimming or StatsEnumerated.Athletics => MathF.Min(value, Definition),
        _ => value,
    };

    public bool Binds(CharacterStats stats) =>
        stats.Stored(StatsEnumerated.Strength) > Strength || stats.Stored(StatsEnumerated.SmithingWeapon) > WeaponSmith
        || stats.Stored(StatsEnumerated.SmithingArmour) > ArmourSmith;
}
