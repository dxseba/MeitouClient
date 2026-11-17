using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>A character to mark: its snapshot, state and bounty, relation and flat distance from the camera's centre.</summary>
readonly record struct VitalTarget(CharacterSnapshot Character, Vital Vital, Relation Relation, float Distance);

/// <summary>
/// Projects the markers: a postfix on the interface's <c>BindUi</c> (which owns the camera, <c>PlayerInterface.Project</c>) writes one binding per
/// marker, "x|y|state|relation|tier", for <see cref="VitalMarkersUi"/> to draw.
/// </summary>
static class VitalMarkers
{
    public const string Key = "vitalsense.marker.";

    /// <summary>World units above a character's root where the marker sits (the original's 420 cm live anchor).</summary>
    const float Anchor = 42;

    /// <summary>The upper bounds of the bounty tiers (trivial to elite); above the last is legendary.</summary>
    static readonly int[] TierMax = [1999, 4999, 9999, 19999, 39999, 74999];

    static FieldInfo? s_sessionField;
    static FieldInfo? s_altField;
    static MethodInfo? s_project;

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = SessionField(type);
        s_altField = AccessTools.Field(type, "AltHeld") ?? throw new MissingFieldException(type.FullName, "AltHeld");
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");

        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(VitalMarkers), nameof(Bound)));
    }

    internal static FieldInfo SessionField(Type player) =>
        player.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(player.FullName, "WorldSession");

    internal static WorldSession? Session(object player) => s_sessionField?.GetValue(player) as WorldSession;

    internal static bool AltHeld(object player) => s_altField?.GetValue(player) is true;

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        var settings = VitalSenseMod.Settings;
        if (!settings.Enabled || Session(__instance) is not { } session) return;
        if (!settings.AlwaysOn && !AltHeld(__instance)) return;

        int slot = 0;
        foreach (var target in Targets(session, session.Camera.Current.Target, settings.Budget))
        {
            if (Text(__instance, target) is { } text) __result.Text[Key + slot++] = text;
        }
    }

    /// <summary>The characters to mark round <paramref name="centre"/>, nearest first, at most <paramref name="budget"/>.</summary>
    internal static List<VitalTarget> Targets(WorldSession session, Vector3 centre, int budget)
    {
        var settings = VitalSenseMod.Settings;
        if (session.World.Systems.OfType<VitalSystem>().FirstOrDefault() is not { } system || system.Vitals.Count == 0) return [];

        var vitals = system.Vitals;
        var list = new List<VitalTarget>();
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (c.Appearance is null || !vitals.TryGetValue(c.Id, out var vital) || !Shown(vital, settings)) continue;

            float distance = Vector2.Distance(new(c.Position.X, c.Position.Z), new(centre.X, centre.Z));
            if (distance <= settings.MaxDistance) list.Add(new(c, vital, RelationOf(c, system.Factions), distance));
        }

        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return list.Count > budget ? list.GetRange(0, budget) : list;
    }

    static bool Shown(Vital vital, VitalSettings settings)
    {
        bool state = settings.ShowText && settings.Shows(vital.State);
        bool bounty = settings.ShowBounty && vital.Bounty > 0 && (vital.State != VitalState.None || settings.BountyOnAll);
        return state || bounty;
    }

    static Relation RelationOf(CharacterSnapshot c, FrozenDictionary<int, Relation> factions)
    {
        if (c.IsPlayer) return Relation.Squad;
        if (c.HostileToPlayer) return Relation.Enemy;
        return factions.GetValueOrDefault(c.Faction);
    }

    /// <summary>The marker's binding, or null when its point is behind the camera.</summary>
    static string? Text(object player, VitalTarget target)
    {
        var head = target.Character.Position + new Vector3(0, Anchor, 0);
        if (s_project!.Invoke(player, [head]) is not Vector2 at) return null;

        var settings = VitalSenseMod.Settings;
        int state = settings.ShowText && settings.Shows(target.Vital.State) ? (int)target.Vital.State : 0;
        int tier = settings.ShowBounty ? Tier(target.Vital.Bounty) : -1;
        return string.Create(CultureInfo.InvariantCulture, $"{at.X:0}|{at.Y:0}|{state}|{(int)target.Relation}|{tier}");
    }

    /// <summary>The bounty's tier, 0 trivial to 6 legendary; -1 for none.</summary>
    static int Tier(int bounty)
    {
        if (bounty <= 0) return -1;

        int tier = Array.FindIndex(TierMax, max => bounty <= max);
        return tier < 0 ? TierMax.Length : tier;
    }
}
