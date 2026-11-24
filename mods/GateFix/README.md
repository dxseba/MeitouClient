# GateFix (port)

Port of *GateFix* v3.1 (Basusu, Workshop 3787399736, GPLv3), from the shipped DLL (Ghidra decompile and strings) and its description. Order
characters past a closed or locked gate of your faction: they open it (a locked one is unlocked for them), the whole squad walks through, and
once nobody of the gate's faction is near it closes and takes back its lock. Both directions, any distance. Settings under Mods on the
Settings screen. No Harmony.

## Facts

- **Settings** (`GateFix.cfg`, written by the DLL on first run; defaults from the DLL's initial values, **Verified**): `enabled=1`,
  `autoClose=1`, `respectLocked=0`, `closeRadius=110`, `closeDelay=3`, `closeGrace=10`, `closeGiveUp=300`, `debug=0`; also `pathDist=2000`,
  `maxDetour=1500`, `openThreshold=0.9`, `pendingWait=60`, `injectDelay=0.4`, `moveEps=8`, `arriveEps=20`, `areaFlag=0`, `verbose=0`,
  `logLimit`, which belong to its workaround for the original's pathing (finding the gate between a character and its click, then injecting
  task 72 Opening door through `Character::addOrder`). Meitou's paths already run through gates, so those keys have no counterpart.
- **Closing** (**Verified**, decompile of its once-a-second step): a gate it opened is not closed within `closeGrace` of the last pass, nor
  while a character it is waiting for still has to pass; it closes once no member of the gate's faction (`Faction::getCharactersInArea`) is
  within `closeRadius` for `closeDelay`; it then locks again if it was locked; after `closeGiveUp` it gives up and leaves it open. Its timers
  are wall-clock (the description: real seconds); the port's are game seconds, so they follow the game speed.
- **Locked gates** (**Observed**, description and the cfg comment of `respectLocked`): opened too by default; with `respectLocked=1` left
  alone, and a gate relocked after a pass is not opened again.
- **Animals** cannot open gates (**Observed**, description). Gates of other factions: not tested by the author; the port takes only gates
  of the player's faction ("friendly"), a port choice.
- **Port**: Meitou's walk-through already opens a closed, unlocked door a walker's path crosses and stops a walker at a locked one
  ([locks](../../docs/game/locks.md) "Who opens and closes"; `LockSystem` "walk-through"). The port's system runs just before the lock system
  in the slow step: a player character (not an animal, awake, free) within 60 of a closed gate's leaf whose next three path legs cross it has
  the gate taken (unlocked when locked), so the engine opens it in the same step; then the closing above, every 30 ticks. The gate's
  `wants lock` is left as it is, so a gate the player set to lock also locks again when it closes.
- `debug=1` writes `GateFix.log`; the port's *Debug log* writes each gate taken, closed and given up to the mod log.
- **Unknown**: not checked in game.

## Missing

- Nothing that needs the engine.
