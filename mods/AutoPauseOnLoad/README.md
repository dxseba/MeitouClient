# Auto-Pause on Load (port)

Port of *Auto-Pause on Load (MCM fork)* (workshop 3813851956, LEOPARD's fork of Emkej8's GPL-3.0 mod): pauses the game after a save is loaded
or imported, when trading starts and when an inventory opens (resuming after either if the mod paused it), and when a character of a squad that
is not the selected one takes a hit. A world-ready system arms the load pause; an `AddFrame` callback does the pauses through
`context.Interface.Pause` (the pause key's pause and resume), fed by `AddCombatHitFeed`. No Harmony.

The installed original is a required item: its gettext catalogues (`auto_pause_on_load`, en_GB, ru_RU, zh_CN) are read at start
(`AddTranslations`), so the settings show in the game's language. Without it they are English.

## Facts (Verified: Ghidra decompile of `Auto-Pause-on-Load.dll` v7, its `updateUT` step and hooks, and its README/defaults, unless marked)

- **Settings** (`mod-config.defaults.json`, the MCM rows' labels and hints): enabled on; pause debounce 2000 ms (0 to 600000); debug logging
  off; pause on trade, resume after trade, pause on inventory, resume after inventory close, pause when another squad is attacked: on; cooldown
  30 s (0 to 300). Under Mod settings; a resume row hides while its pause is off ("only applies when").
- **Load**: the original polls `GameWorld::isLoadingFromASaveGame` and pauses (`userPause(true)`) when a load finishes, unless its last pause
  was less than the debounce ago. Meitou: `IWorldReady` with `WorldStartKind.Load` arms it; the next frame pauses.
- **Import** (`SaveManager::importGame` hook, the fork's 16.10 change): it waits until frames have come steadily (no gap over 0.25 s) for 2 s,
  pauses (even when already paused), then for 3 s pauses again if the game resumed by itself, at most three times, stopping when the player
  presses a key or mouse button. The port waits and pauses the same way; Meitou's clock never resumes by itself, so the re-pause has nothing to
  catch and is left out.
- **Windows** (every 100 ms in the original, every frame in the port): trade is the trade window; inventory counts only while no trade shows.
  Opening one pauses the game when it runs and the window owns the pause. When already paused it takes over the pause only if the other window
  owns it (the fork's v2 fix for switching characters while trading), else it notes the game was paused before. Closing a window that owns the
  pause, with its resume on, resumes unless a window whose pause is on still shows. Meitou: the `trader` screen or a trade in the snapshot, and
  the `inventory` screen.
- **Attacked squad** (`MedicalSystem::addWound` hook): a wound on a living player character whose squad (platoon) is not the player
  interface's selected one marks that squad; the next step pauses the game when it runs, unless the last such pause was for the same squad less
  than the cooldown ago (wall clock, `GetTickCount`). Meitou: a landed hit (`CombatHit.VictimWasAlive`) on a snapshot character with
  `IsPlayer` whose `SquadId` is not `WorldSnapshot.ActiveSquad`. **Unknown**: a test on the wound's `Damages` (its vfunc `+0x20` == 1) and a
  medical flag `+0x164`; the port uses only "alive".
- Times are host-clock milliseconds as the original's; nothing enters the simulation or a save.
- **Observed** (by reading `ModInterface`): `Pause(true)` stores the speed and pauses; `Pause(false)` resumes at it only while the speed is 0.
  Meitou's own "Pause after loading" setting does the load pause too; both together pause once.

## Missing

- Nothing. **Unknown**: not checked in game.
