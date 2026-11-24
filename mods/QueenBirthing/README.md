# Queen Birthing (port)

Port of *Queen Start with Birthing* (workshop 3777203526). The original's data (`QueenBirth.mod`: the Exiled Queen start, the worker template)
arrives with it as a required item.

- **GIVE BIRTH** (`UiRegistration.AddScreenControls("inventory", "ArrangeButton", Left)`): a button in a living player queen's inventory
  window at the original's window-relative x 0.52 (width 0.22), captioned TOO HUNGRY below the worker's cost; for her ARRANGE moves right from
  0.6615 to 0.7615 (`AddScreenOverrides(... "ArrangeButton" ...)` with `Bounds`: +0.1 of the window width, taken as ARRANGE's width / 0.22179). A click opens a popup at the pointer (12 left, 4 up, kept on the screen):
  *WORKER DRONE (130)*, *SOLDIER DRONE (230)*, *HIVE PRINCE (230)*, each enabled while her hunger covers it; it hides once the pointer has been
  off it for more than 10 frames (`AddFrame`) or its window closes. A birth spawns the newborn into her squad 2 units along +X
  (`PopulationSystem.SpawnIntoPlayerSquad` with `SpawnOptions(IgnoreCaps: true)`, past `max squad size` / `max faction size` as the original;
  rings 12-45 round her when that point is not walkable), takes the cost from her hunger, knocks her
  out for 10 s, sets the newborn's hunger to 1.75 and knocks it out for 600 s. *Open editor on birth* (on) opens the character editor on the
  newborn as a recruit's edit (`ModInterface.OpenScreen(ModScreen.CharacterEditor)`, retried until the newborn is in the snapshot).
- **Verified** (original DLL decompiled, and its shipped source): costs 1.30 / 2.30 / 2.30 raw hunger (buttons show x100), refused only when
  hunger is below the cost; templates `12-QueenBirth.mod` (worker), `10-` (soldier), `11-` (prince); queen KO 10 s, newborn KO 600 s, newborn
  hunger 1.75; spawn at queen + (2, 0, 0) into her platoon and faction; the button at window-relative (0.52, 0.9205, 0.22 x 0.0499) with ARRANGE
  (0.22179 wide) set to x 0.7615 for a queen and 0.6615 otherwise (DLL constants at 0x1800114d8-f0); popup 176 x 100 (`Kenshi_FloatingPanelSkin`, the game's own context menu skin, as here), buttons 24 high every
  30 from 8; a queen is a race whose string id contains `Queen` or `57335-Newwworld.mod`, or whose name contains `Queen` (case-sensitive); the button
  reads GIVE BIRTH, or TOO HUNGRY below 1.30; the popup hides after more than 10 frames with the pointer off it; `ShowEditorOnBirth` (default
  1) in `%LOCALAPPDATA%\kenshi\QueenBirth.settings`, set by a tick box on the Mods options tab (here a Mod setting). Its log lines (spawned from
  a template with hunger 1.75 and knocked out for 600 s; no newborn) go to the mod log.
- **Verified** (`QueenBirth.mod` read with `meitou-tools fcs`/`record`): it defines only `12-QueenBirth.mod` ("Worker Drone", race Hive Worker
  Drone), so only the worker is offered; the soldier and prince rows appear when data supplies `10-`/`11-QueenBirth.mod`. Without
  `QueenBirth.mod` the worker falls back to a CHARACTER named "Hive Worker Drone" (or of that race).
- **Deliberate differences**: the original takes the hunger and knocks the queen out before spawning, so a missing template or failed spawn cost
  her; here nothing is paid unless the newborn appears. Its unpause and speed 1 before the editor opens (to settle its spawn) is not needed.
- **Deliberate**: kinds without a template are not offered (the original shows them and charges for nothing). The button and ARRANGE's move
  show only for a living player queen (the original: any queen's inventory window). **Unknown**: whether Meitou's inventory layout has
  ARRANGE at the original's 0.6615 (the move is relative, so the gap to the button is the original's either way).
- **Observed** (2026-11-19, headless sandbox, earlier version): loads and runs 3000 ticks without a fault. The button, popup, birth and editor are
  not checked in game.

## Missing

- Nothing beyond the deliberate differences above; not checked in game.
