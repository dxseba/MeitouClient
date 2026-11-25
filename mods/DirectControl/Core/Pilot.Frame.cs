using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DirectControl;

/// <summary>
/// What the simulation publishes for the host: the perfect-block reward.
/// </summary>
static class HostState
{
    public static bool RewardShown => HostAccess.Session?.World is { } world && AttackGate.Of(world) is { RewardShown: true };

    public static bool Stuck => HostAccess.Session?.World is { } world && AttackGate.Of(world) is { Stuck: true };

    public static long LastPerfect =>
        HostAccess.Session?.World is { } world && AttackGate.Of(world) is { } system ? system.LastPerfect : long.MinValue;
}

/// <summary>
/// A health bar of the target's name tag: its label and how full it is.
/// </summary>
readonly record struct TagBar(string Label, float Fill);

/// <summary>
/// The target's name tag as the overlay draws it this frame.
/// </summary>
sealed record NameTag(string Name, Vector2 At, IReadOnlyList<TagBar> Bars);

/// <summary>
/// The once-a-frame part (<c>AddFrame</c>): the camera follows the driven character, the selection's lead is driven (the original follows the
/// selected character), notices, the aim target and its name tag, the weapon glint and the perfect-block glow.
/// </summary>
sealed partial class Pilot
{
    static readonly Vector3 GlintColour = new(1f, 0.85f, 0.55f), RewardColour = new(0.35f, 0.6f, 1f);

    CharacterId? _hovered;
    readonly HashSet<CharacterId> _swinging = [];
    long _shownPerfect = long.MinValue;

    /// <summary>
    /// The name tag to draw (null: none).
    /// </summary>
    public NameTag? Tag { get; private set; }

    /// <summary>
    /// The screen size of the last frame, for the crosshair.
    /// </summary>
    public System.Numerics.Vector2 Screen { get; private set; }

    public void Frame(ModFrame frame)
    {
        HostAccess.Capture(frame);
        Screen = frame.ScreenSize;
        if (_notice is { } notice)
        {
            frame.Notify(notice);
            _notice = null;
        }

        _hovered = frame.Hovered;
        if (!On || HostAccess.Find(frame.Snapshot, Controlled) is not { } me)
        {
            Tag = null;
            return;
        }

        FollowSelection(frame);
        _inventoryOpen = frame.IsOpen("inventory") && frame.Shown == Controlled;
        _inCombat = me.InCombat;
        HostAccess.Follow = Controlled;
        Tag = settings.AimNameTag && Manual ? TagOf(frame, me) : null;
        Glint(frame, me);
        Reward();
    }

    /// <summary>
    /// A newly selected player character takes over direct control (a portrait double-click or a click on another of the squad).
    /// </summary>
    void FollowSelection(ModFrame frame)
    {
        if (frame.Selected.Count == 0 || frame.Selected[0] == Controlled) return;
        if (HostAccess.Find(frame.Snapshot, frame.Selected[0]) is not { } next || !Fit(next)) return;

        bool manual = Manual;
        var view = View;
        TurnOff();
        Manual = manual;
        TurnOn(next.Id);
        SetView(view);
    }

    /// <summary>
    /// The enemy the player points at: in the game's camera the one under the cursor; in first person and the third-person camera the nearest
    /// to the crosshair within the cone (Action RPG: the locked one first). Null: none (the fight's own target is kept).
    /// </summary>
    CharacterSnapshot? AimTarget(CharacterSnapshot me)
    {
        if (HostAccess.Session?.CurrentSnapshot is not { } snapshot) return null;
        if (!_lock.IsNone && HostAccess.Find(snapshot, _lock) is { } locked) return locked;
        if (View == View.Normal) return _hovered is { } id && HostAccess.Find(snapshot, id) is { IsPlayer: false } hovered ? hovered : null;

        return Nearest(snapshot, me);
    }

    CharacterSnapshot? Nearest(WorldSnapshot snapshot, CharacterSnapshot me)
    {
        float cone = MathF.Cos(settings.AimConeDeg * MathF.PI / 180);
        var forward = new Vector2(-MathF.Sin(LookYaw), -MathF.Cos(LookYaw));
        CharacterSnapshot? best = null;
        float bestDot = cone;
        foreach (var c in snapshot.Characters)
        {
            if (c.IsPlayer || c.Body is { Dead: true }) continue;

            var to = new Vector2(c.Position.X - me.Position.X, c.Position.Z - me.Position.Z);
            float d = to.Length();
            if (d < 1 || d > 80) continue;

            float dot = Vector2.Dot(forward, to / d);
            if (dot > bestDot) (best, bestDot) = (c, dot);
        }

        return best;
    }

    /// <summary>
    /// The target's name (red) and its blood, head, chest and stomach bars over its head; with no aimed target the one fighting the character.
    /// </summary>
    NameTag? TagOf(ModFrame frame, CharacterSnapshot me)
    {
        var target = AimTarget(me) ?? Attacker(frame.Snapshot, me);
        if (target is null || frame.Project(HostAccess.Drawn(target) + new Vector3(0, target.TagHeight + 2, 0)) is not { } at) return null;

        var bars = new List<TagBar>(4);
        if (target.Body is { } body) bars.Add(new TagBar(context.Tr("Blood"), Math.Clamp(body.Blood, 0, 1)));
        // A person's first three parts are the head, chest and stomach (the race's body part order).
        foreach (var part in target.HealthParts.Take(3))
        {
            if (part.Maximum > 0) bars.Add(new TagBar(part.Name, Math.Clamp(part.Flesh / part.Maximum, 0, 1)));
        }

        return new NameTag(target.Name, at, bars);
    }

    /// <summary>
    /// The nearest enemy fighting the player within a sword's reach and a bit.
    /// </summary>
    static CharacterSnapshot? Attacker(WorldSnapshot snapshot, CharacterSnapshot me)
    {
        CharacterSnapshot? best = null;
        float bestDistance = 40;
        foreach (var c in snapshot.Characters)
        {
            if (c.IsPlayer || !c.FightsPlayer || !c.InCombat) continue;

            float d = Vector3.Distance(c.Position, me.Position);
            if (d < bestDistance) (best, bestDistance) = (c, d);
        }

        return best;
    }

    /// <summary>
    /// Weapon glint: an enemy fighting the player near the character that starts a swing glows at its weapon for the glint time.
    /// </summary>
    void Glint(ModFrame frame, CharacterSnapshot me)
    {
        if (!settings.WeaponGlint || !Manual)
        {
            _swinging.Clear();
            return;
        }

        foreach (var c in frame.Snapshot.Characters)
        {
            bool swinging = !c.IsPlayer && c.Attacking && c.FightsPlayer && Vector3.Distance(c.Position, me.Position) < 40;
            if (!swinging)
            {
                _swinging.Remove(c.Id);
                continue;
            }

            if (_swinging.Add(c.Id)) context.Glow(GlowTarget.Weapon(c.Id), GlintColour, settings.GlintLeadMs / 1000f);
        }
    }

    /// <summary>
    /// A perfect block lights the character's own weapon blue for the reward time.
    /// </summary>
    void Reward()
    {
        long perfect = HostState.LastPerfect;
        if (perfect == _shownPerfect) return;

        _shownPerfect = perfect;
        if (perfect > long.MinValue / 8) context.Glow(GlowTarget.Weapon(Controlled), RewardColour, settings.PerfectBlockRewardMs / 1000f);
    }
}
