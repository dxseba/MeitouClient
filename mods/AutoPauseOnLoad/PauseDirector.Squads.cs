using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.AutoPauseOnLoad;

/// <summary>
/// The attacked-squad pause (<b>Verified</b>, DLL decompile, its <c>MedicalSystem::addWound</c> hook and frame step): a wound on one of the
/// player's characters, alive, whose squad is not the selected one (the squad bar's) marks that squad; the next frame pauses the game when it runs,
/// unless the last such pause was for the same squad less than the cooldown ago (0: every hit). The last marked squad of a frame counts.
/// </summary>
sealed partial class PauseDirector
{
    readonly List<CharacterId> _victims = [];
    int _lastSquad = -1;
    long _lastSquadAt;

    /// <summary>
    /// The hit feed (host thread, before <see cref="Frame"/>): the frame's landed hits.
    /// </summary>
    public void Hits(IReadOnlyList<CombatHit> hits)
    {
        try
        {
            if (!Settings.Enabled || !Settings.PauseOnSquadAttack) return;

            foreach (var hit in hits)
            {
                if (hit.VictimWasAlive) _victims.Add(hit.Victim);
            }
        }
        catch (Exception e)
        {
            LogOnce("hits", e);
        }
    }

    /// <summary>
    /// The squad of the last victim that is a player character outside the selected squad; -1 for none.
    /// </summary>
    int LastOtherSquad(WorldSnapshot snapshot)
    {
        int squad = -1;
        foreach (var victim in _victims)
        {
            foreach (var c in snapshot.Characters)
            {
                if (c.Id != victim) continue;

                if (c.IsPlayer && c.SquadId >= 0 && c.SquadId != snapshot.ActiveSquad) squad = c.SquadId;
                break;
            }
        }

        _victims.Clear();
        return squad;
    }

    void SquadStep(WorldSnapshot snapshot, long now)
    {
        if (_victims.Count == 0) return;

        int squad = LastOtherSquad(snapshot);
        if (squad < 0 || !Settings.PauseOnSquadAttack) return;

        bool cooling = squad == _lastSquad && _lastSquadAt != 0 && now - _lastSquadAt < Settings.SquadCooldownSeconds * 1000L;
        if (cooling || Paused || !game.Pause(true)) return;

        (_lastSquad, _lastSquadAt) = (squad, now);
        Debug("paused=true source=inactive_squad_attacked");
    }
}
