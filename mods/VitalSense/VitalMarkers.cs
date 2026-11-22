using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>
/// A character to mark: its snapshot, state and bounty, relation and flat distance from the camera's centre.
/// </summary>
readonly record struct VitalTarget(CharacterSnapshot Character, Vital Vital, Relation Relation, float Distance);

/// <summary>
/// Projects the markers: a postfix on the interface's <c>BindUi</c> (which owns the camera, <c>PlayerInterface.Project</c>) writes one binding per
/// marker, "x|y|state|relation|tier", for <see cref="VitalMarkersUi"/> to draw, and renews the body tint (<see cref="VitalTint"/>).
/// </summary>
static class VitalMarkers
{
    public const string Key = "vitalsense.marker.";

    /// <summary>
    /// World units (1 = 10 cm) above a downed or dead character's root where its state marker sits (the DLL's projection offset, 2.0).
    /// </summary>
    const float DownAnchor = 2;

    /// <summary>
    /// The upper bounds of the bounty tiers (trivial to elite, <c>bounty_tier_*_max</c>); above the last is legendary.
    /// </summary>
    static readonly int[] TierMax = [1999, 4999, 9999, 19999, 39999, 74999];

    static FieldInfo? s_sessionField;
    static FieldInfo? s_altField;
    static MethodInfo? s_project;
    static ModKeyAction? s_key;

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = SessionField(type);
        s_altField = AccessTools.Field(type, "AltHeld") ?? throw new MissingFieldException(type.FullName, "AltHeld");
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");

        // The original's highlight_key is ALT by default; a mod may not default to Alt, so the game's Highlight (Alt) also shows them.
        s_key = context.AddKeyAction("highlight", "Vital Sense highlight", _ => { });

        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(VitalMarkers), nameof(Bound)));
    }

    internal static FieldInfo SessionField(Type player) =>
        player.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(player.FullName, "WorldSession");

    internal static WorldSession? Session(object player) => s_sessionField?.GetValue(player) as WorldSession;

    internal static bool AltHeld(object player) => s_altField?.GetValue(player) is true;

    /// <summary>
    /// Whether the markers show now: always on, the game's Highlight key or the mod's own key held.
    /// </summary>
    internal static bool Active(object player) => VitalSenseMod.Settings.AlwaysOn || AltHeld(player) || s_key?.Held == true;

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        var settings = VitalSenseMod.Settings;
        if (!settings.Enabled || Session(__instance) is not { } session || !Active(__instance))
        {
            VitalTint.Clear();
            return;
        }

        var targets = Targets(session, session.Camera.Current.Target, settings.Budget);
        int slot = 0;
        foreach (var target in targets)
        {
            if (Text(__instance, target) is { } text) __result.Text[Key + slot++] = text;
        }

        VitalTint.Renew(session, targets);
    }

    /// <summary>
    /// The characters to mark round <paramref name="centre"/>, nearest first, at most <paramref name="budget"/>.
    /// </summary>
    internal static List<VitalTarget> Targets(WorldSession session, Vector3 centre, int budget)
    {
        var settings = VitalSenseMod.Settings;
        if (System(session) is not { } system || system.Vitals.Count == 0) return [];

        var vitals = system.Vitals;
        var list = new List<VitalTarget>();
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (c.Appearance is null || !vitals.TryGetValue(c.Id, out var vital) || !Shown(vital, settings)) continue;

            float distance = Flat(c.Position, centre);
            if (distance <= settings.MaxDistance) list.Add(new(c, vital, RelationOf(c, vital, system.Factions), distance));
        }

        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return list.Count > budget ? list.GetRange(0, budget) : list;
    }

    internal static VitalSystem? System(WorldSession session) => session.World.Systems.OfType<VitalSystem>().FirstOrDefault();

    internal static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    static bool Shown(Vital vital, VitalSettings settings)
    {
        bool state = settings.ShowText && settings.Shows(vital.State);
        bool bounty = settings.ShowBounty && vital.Bounty > 0 && (vital.State != VitalState.None || settings.BountyOnAll);
        bool tint = settings.Tint && (vital.State != VitalState.None || settings.TintBounty && vital.Bounty > 0);
        return state || bounty || tint;
    }

    /// <summary>
    /// The original's relation (<b>Verified</b>, original DLL (decompiled)): one of the player's characters is squad; one an own character is
    /// hostile to is an enemy; an animal is an ally of the player's faction, else neutral; a non-animal is an ally when allied to the player, else
    /// neutral. A medically dead character not the player's takes the enemy colour.
    /// </summary>
    static Relation RelationOf(CharacterSnapshot c, Vital vital, FrozenDictionary<int, Relation> factions)
    {
        if (c.IsPlayer) return Relation.Squad;
        if (vital.MedicallyDead || c.HostileToPlayer) return Relation.Enemy;

        var faction = factions.GetValueOrDefault(c.Faction);
        if (c.Animal) return faction == Relation.Squad ? Relation.Ally : Relation.Neutral;

        return faction switch
        {
            Relation.Squad or Relation.Ally => Relation.Ally,
            Relation.Enemy => Relation.Enemy,
            _ => Relation.Neutral,
        };
    }

    /// <summary>
    /// The marker's binding, or null when its point is behind the camera or nothing of it shows.
    /// </summary>
    static string? Text(object player, VitalTarget target)
    {
        var settings = VitalSenseMod.Settings;
        int state = settings.ShowText && settings.Shows(target.Vital.State) ? (int)target.Vital.State : 0;
        int tier = settings.ShowBounty && (target.Vital.State != VitalState.None || settings.BountyOnAll) ? Tier(target.Vital.Bounty) : -1;
        if (state == 0 && tier < 0) return null;

        var head = target.Character.Position + new Vector3(0, Height(target.Vital.State, settings), 0);
        if (s_project!.Invoke(player, [head]) is not Vector2 at) return null;

        return string.Create(CultureInfo.InvariantCulture, $"{at.X:0}|{at.Y:0}|{state}|{(int)target.Relation}|{tier}");
    }

    /// <summary>
    /// A downed or dead character's marker at its root plus the state offset; a standing one's $ at the bounty height (cm, 10 to a unit).
    /// </summary>
    static float Height(VitalState state, VitalSettings settings) =>
        state == VitalState.None ? settings.BountyOffsetCm / 10f : DownAnchor + settings.StateOffsetCm / 10f;

    /// <summary>
    /// The bounty's tier, 0 trivial to 6 legendary; -1 for none.
    /// </summary>
    internal static int Tier(int bounty)
    {
        if (bounty <= 0) return -1;

        int tier = Array.FindIndex(TierMax, max => bounty <= max);
        return tier < 0 ? TierMax.Length : tier;
    }
}
