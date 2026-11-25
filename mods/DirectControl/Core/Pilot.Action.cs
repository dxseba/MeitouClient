using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.FirstAid;

namespace Meitou.DirectControl;

/// <summary>
/// The Action RPG scheme's other keys (raw, in V mode): E uses what the crosshair points at (the game's own right click there, so the game's
/// default action, cursor icon, reach, ownership and crime rules decide), Shift+E treats the character's own wounds, R draws or sheathes, Q
/// switches the weapon slots; with a crossbow drawn or on a turret, Attack shoots at the aimed target. And the hit marker.
/// </summary>
sealed partial class Pilot
{
    /// <summary>
    /// Where the held pointer is pinned, in interface pixels (the middle of the window), once seen.
    /// </summary>
    public Vector2? PinnedUi { get; private set; }

    /// <summary>
    /// When the controlled character's last blow landed (host clock, for the hit marker).
    /// </summary>
    long _hitAt = long.MinValue;

    /// <summary>
    /// How long the hit marker shows, in milliseconds (the fork's ~1000 ms fade).
    /// </summary>
    const long HitMarkerMs = 1000;

    /// <summary>
    /// The hit marker's opacity now (0: none).
    /// </summary>
    public float HitMarker => Math.Clamp(1 - (Environment.TickCount64 - _hitAt) / (float)HitMarkerMs, 0, 1) * 0.82f;

    /// <summary>
    /// The pointer was pinned at <paramref name="ui"/>: the interface's hover (the cursor icon, the character pointed at) follows the crosshair.
    /// </summary>
    public void Pinned(Vector2 ui)
    {
        PinnedUi = ui;
        HostAccess.PlayerCall("MouseMove", ui);
    }

    void StanceKeys(Meitou.Game.Host.Input.TickInput input, CharacterSnapshot me)
    {
        if (input.WasPressed(Key.R) && Fresh(Key.R)) Send(new StanceCommand(me.Id, me.WeaponDrawn) { Tick = Now });
        if (input.WasPressed(Key.Q) && Fresh(Key.Q)) Send(new SwapWeaponCommand(me.Id) { Tick = Now });
    }

    void InteractKey(Meitou.Game.Host.Input.TickInput input, CharacterSnapshot me)
    {
        if (!input.WasPressed(Key.E) || !Fresh(Key.E)) return;

        bool shift = input.IsDown(Key.LeftShift) || input.IsDown(Key.RightShift);
        if (shift)
        {
            if (!me.WeaponDrawn) Send(new TreatOrder([me.Id], me.Id) { Tick = Now });
            return;
        }

        if (PinnedUi is { } at) HostAccess.Click(MouseButton.Right, at);
    }

    /// <summary>
    /// A crossbow drawn: the attack is an order to fight the aimed target (the game's ranged controller aims, reloads and shoots); on a turret
    /// (operating a site), an order to shoot at it. False: a melee swing.
    /// </summary>
    bool Shoot(CharacterSnapshot me, CharacterSnapshot? target)
    {
        bool turret = me.Task == Simulation.Movement.CharacterTask.Operate;
        if (!me.RangedDrawn && !turret) return false;
        if (target is null) return true;

        if (turret) Send(new ShootAtOrder([me.Id], target.Id) { Tick = Now });
        else Send(new AttackOrder([me.Id], target.Id) { Tick = Now });

        return true;
    }

    /// <summary>
    /// The combat hit feed: a blow by the controlled character that landed shows the hit marker.
    /// </summary>
    public void Hits(IReadOnlyList<CombatHit> hits)
    {
        if (!On) return;

        foreach (var hit in hits)
        {
            if (hit.Attacker == Controlled && hit.Victim != Controlled) _hitAt = Environment.TickCount64;
        }
    }
}
