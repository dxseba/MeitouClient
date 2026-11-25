using Meitou.Game.Host.Input;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Player;

namespace Meitou.DirectControl;

/// <summary>
/// The toggles as the game's own key actions (Controls page, rebindable). The original's keys are kept where the game has them free; where the
/// game holds the key (P photo mode, H the medic alias, Ctrl the camera turn and a refused default) another free key stands in (README "Port
/// choices").
/// </summary>
sealed partial class Pilot
{
    bool _runLock;

    public void AddKeys()
    {
        context.AddKeyAction("toggle", settings.Classic ? "Toggle Direct Control" : "Toggle V mode (Action RPG)", Toggle, Binding.Of(Key.V));
        if (settings.Classic) AddClassicKeys();
        else AddActionKeys();
    }

    void AddClassicKeys()
    {
        context.AddKeyAction("speed", "Cycle speed (Direct Control)", _ => WhenOn(CycleSpeed), Binding.Of(Key.X));
        context.AddKeyAction("take", "Control the character under the cursor", Take, Binding.Of(Key.G));
        context.AddKeyAction("first-person", "First-person view", _ => WhenOn(() => Flip(View.FirstPerson)), Binding.Of(Key.O));
        context.AddKeyAction("third-person", "Third-person action camera", _ => WhenOn(() => Flip(View.Shoulder)), Binding.Of(Key.U));
        context.AddKeyAction("shoulder", "Swap camera shoulder", _ => Shoulder = -Shoulder, Binding.Of(Key.N));
        context.AddKeyAction("sneak", "Toggle sneak (Direct Control)", _ => WhenOn(Sneak), Binding.Of(Key.C).With(KeyModifiers.Shift));
        context.AddKeyAction("manual", "Manual combat on / off", _ => WhenOn(ToggleManual), Binding.Of(Key.Z));
    }

    void AddActionKeys()
    {
        context.AddKeyAction("run-lock", "Run lock (Action RPG)", _ => _runLock = !_runLock);
        context.AddKeyAction("direction-lock", "Lock attack direction", _ => WhenOn(ToggleDirectionLock), Binding.Of(Key.L).With(KeyModifiers.Ctrl));
        context.AddKeyAction("squad-follow", "Squad follows you (Action RPG)", _ => WhenOn(SquadFollow), Binding.Of(Key.F1).With(KeyModifiers.Ctrl));
        context.AddKeyAction("squad-move", "Squad moves to the crosshair (Action RPG)", _ => WhenOn(SquadMove),
            Binding.Of(Key.F2).With(KeyModifiers.Ctrl));
    }

    void CycleSpeed() => Send(new RunSpeedCommand([Controlled]) { Tick = Now });

    void Sneak() => Send(new OrdersCommand([Controlled], CharacterOrders.Sneak) { Tick = Now });

    void Flip(View view) => SetView(View == view ? View.Normal : view);

    void WhenOn(Action act)
    {
        if (On) act();
    }

    /// <summary>
    /// V: direct control on the selected character (the first selected), or off.
    /// </summary>
    void Toggle(ModKeyContext key)
    {
        if (On)
        {
            TurnOff();
            return;
        }

        if (key.Selected.Count == 0 || HostAccess.Find(key.Snapshot, key.Selected[0]) is not { } c || !Fit(c)) return;

        Manual = !settings.Classic;
        TurnOn(c.Id);
    }

    /// <summary>
    /// G: control passes to the player's character under the cursor.
    /// </summary>
    void Take(ModKeyContext key)
    {
        if (key.Frame?.Hovered is not { } id || HostAccess.Find(key.Snapshot, id) is not { } c || !Fit(c)) return;

        bool manual = Manual;
        if (On) TurnOff();
        Manual = manual;
        TurnOn(c.Id);
    }

    void ToggleManual()
    {
        Manual = !Manual;
        ClearCombatKeys();
        SendControl();
        Notify(context.Tr(Manual ? "Manual combat on" : "Manual combat off"));
    }

    /// <summary>
    /// The driven character's squad mates (in the last snapshot).
    /// </summary>
    List<CharacterId> Mates()
    {
        var mates = new List<CharacterId>();
        if (HostAccess.Session?.CurrentSnapshot is not { } snapshot || HostAccess.Find(snapshot, Controlled) is not { } me) return mates;

        foreach (var c in snapshot.Characters)
        {
            if (c.IsPlayer && c.SquadId == me.SquadId && c.Id != me.Id) mates.Add(c.Id);
        }

        return mates;
    }

    void SquadFollow()
    {
        var mates = Mates();
        if (mates.Count > 0) Send(new FollowOrder(mates, Controlled) { Tick = Now });
    }

    void SquadMove()
    {
        var mates = Mates();
        if (mates.Count > 0 && CrosshairGround() is { } point) Send(new MoveOrder(mates, point) { Tick = Now });
    }
}
