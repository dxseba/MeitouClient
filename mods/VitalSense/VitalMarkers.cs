using System.Collections.Frozen;
using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>
/// A character to mark: its snapshot, state and bounty, relation and flat distance from the camera's centre.
/// </summary>
readonly record struct VitalTarget(CharacterSnapshot Character, Vital Vital, Relation Relation, float Distance);

/// <summary>
/// One marker on screen: its projected point, state (None: a bounty-only $), relation and bounty tier (-1 none).
/// </summary>
readonly record struct Marker(Vector2 At, VitalState State, Relation Relation, int Tier);

/// <summary>
/// Projects the markers once a frame (<c>AddFrame</c>: the snapshot, camera and projection on screen) for <see cref="VitalMarkersUi"/> to draw,
/// and renews the body tint (<see cref="VitalTint"/>).
/// </summary>
static class VitalMarkers
{
    /// <summary>
    /// World units (1 = 10 cm) above a downed or dead character's root where its state marker sits (the DLL's projection offset, 2.0).
    /// </summary>
    const float DownAnchor = 2;

    static ModKeyAction? s_key;

    /// <summary>
    /// The markers of the last frame (host thread).
    /// </summary>
    internal static IReadOnlyList<Marker> Current { get; private set; } = [];

    public static void Install(ModContext context)
    {
        // The original's highlight_key is ALT by default; a mod may not default to Alt, so the game's Highlight (Alt) also shows them.
        s_key = context.AddKeyAction("highlight", "Vital Sense highlight", _ => { });
        context.AddFrame(Frame);
    }

    /// <summary>
    /// Whether the markers show now: always on, the game's Highlight key or the mod's own key held.
    /// </summary>
    static bool Active(ModFrame frame) => VitalSenseMod.Settings.AlwaysOn || frame.Highlight || s_key?.Held == true;

    static void Frame(ModFrame frame)
    {
        var settings = VitalSenseMod.Settings;
        if (!settings.Enabled || !Active(frame) || VitalSenseMod.Running is not { } system)
        {
            Current = [];
            VitalTint.Clear();
            return;
        }

        var targets = Targets(system, frame.Snapshot, frame.CameraTarget, settings.Budget);
        var markers = new List<Marker>(targets.Count);
        foreach (var target in targets)
        {
            if (Project(frame, target) is { } marker) markers.Add(marker);
        }

        Current = markers;
        VitalTint.Renew(frame, targets);
    }

    /// <summary>
    /// The characters to mark round <paramref name="centre"/>, nearest first, at most <paramref name="budget"/>.
    /// </summary>
    static List<VitalTarget> Targets(VitalSystem system, WorldSnapshot snapshot, Vector3 centre, int budget)
    {
        var settings = VitalSenseMod.Settings;
        if (system.Vitals.Count == 0) return [];

        var vitals = system.Vitals;
        var list = new List<VitalTarget>();
        foreach (var c in snapshot.Characters)
        {
            if (c.Appearance is null || !vitals.TryGetValue(c.Id, out var vital) || !Shown(vital, settings)) continue;

            float distance = Flat(c.Position, centre);
            if (distance <= settings.MaxDistance) list.Add(new(c, vital, RelationOf(c, vital, system.Factions), distance));
        }

        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return list.Count > budget ? list.GetRange(0, budget) : list;
    }

    internal static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    static bool Shown(Vital vital, VitalSettings settings)
    {
        bool state = (settings.ShowText || settings.ShowIcons) && settings.Shows(vital.State);
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
    /// The marker of <paramref name="target"/>, or null when its point is behind the camera or nothing of it shows.
    /// </summary>
    static Marker? Project(ModFrame frame, VitalTarget target)
    {
        var settings = VitalSenseMod.Settings;
        var state = (settings.ShowText || settings.ShowIcons) && settings.Shows(target.Vital.State) ? target.Vital.State : VitalState.None;
        int tier = settings.ShowBounty && (target.Vital.State != VitalState.None || settings.BountyOnAll) ? Tier(target.Vital.Bounty) : -1;
        if (state == VitalState.None && tier < 0) return null;

        var head = target.Character.Position + new Vector3(0, Height(target.Vital.State, settings), 0);
        return frame.Project(head) is { } at ? new Marker(at, state, target.Relation, tier) : null;
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

        // The upper bounds of the bounty tiers (trivial to elite, bounty_tier_*_max); above the last is legendary.
        int[] bounds = VitalColours.Bounds();
        int tier = Array.FindIndex(bounds, max => bounty <= max);
        return tier < 0 ? bounds.Length : tier;
    }
}
