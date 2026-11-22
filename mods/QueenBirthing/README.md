# Queen Birthing (partial port)

Port of *Queen Start with Birthing* (workshop 3777203526). The original's data (`QueenBirth.mod`: the Exiled Queen start, the worker template)
arrives with it as a required item.

- **Birth rows** (`UiRegistration.AddCharacterMenuVerbs`) on a living player queen: *Give Birth: Worker Drone (130)*, *Soldier Drone (230)*,
  *Hive Prince (230)*, each shown while her hunger covers it, else *Give Birth: too hungry*. A birth spawns the newborn into her squad 2 units
  along +X (`PopulationSystem.SpawnIntoPlayerSquad`; rings 12-45 round her when that point is not walkable), takes the cost from her hunger, knocks
  her out for 10 s, sets the newborn's hunger to 1.75 and knocks it out for 600 s. *Open editor on birth* (on) opens the character editor on the
  newborn as a recruit's edit (Harmony postfix on `PlayerInterface.BindUi`).
- **Verified** (original DLL decompiled, and its shipped source): costs 1.30 / 2.30 / 2.30 raw hunger (buttons show x100), refused only when
  hunger is below the cost; templates `12-QueenBirth.mod` (worker), `10-` (soldier), `11-` (prince); queen KO 10 s, newborn KO 600 s, newborn
  hunger 1.75; spawn at queen + (2, 0, 0) into her platoon and faction; a queen is a race whose string id contains `Queen` or
  `57335-Newwworld.mod`, or whose name contains `Queen` (case-sensitive); the button reads GIVE BIRTH, or TOO HUNGRY below 1.30;
  `ShowEditorOnBirth` (default 1) in `%LOCALAPPDATA%\kenshi\QueenBirth.settings`, set by a tick box on the Mods options tab.
- **Verified** (`QueenBirth.mod` read with `meitou-tools fcs`/`record`): it defines only `12-QueenBirth.mod` ("Worker Drone", race Hive Worker
  Drone), so only the worker is offered; the soldier and prince rows appear when data supplies `10-`/`11-QueenBirth.mod`. Without
  `QueenBirth.mod` the worker falls back to a CHARACTER named "Hive Worker Drone" (or of that race).
- **Deliberate differences**: the original takes the hunger and knocks the queen out before spawning, so a missing template or failed spawn cost
  her; here nothing is paid unless the newborn appears. Its unpause and speed 1 before the editor opens (to settle its spawn) is not needed.
- **Missing**: the button in the queen's inventory window (no inventory-window slot in the UI API; menu rows instead). A squad at `max squad
  size` or faction at `max faction size` refuses the birth (`SpawnIntoPlayerSquad`'s caps; the original's code checks none before `createRandomCharacter`).
- **Observed** (2026-11-19, headless sandbox, earlier version): loads and runs 3000 ticks without a fault. The birth and editor are not checked
  in game.
