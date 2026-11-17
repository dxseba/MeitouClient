# Work for Cats (port)

Port of *Work for Cats - Get Hired at NPC Workbenches* (workshop 3802290991, GPL-3.0). Its source could not be downloaded, so the behaviour is
rebuilt from the workshop description; nothing here is checked against the original's code.

- **Employment window** (`UiSlots.Overlay`, setting *Show employment window*): while the HUD inspects a workplace
  (`PlayerState.InspectedBuilding`), a window shows the building, its employer, the first selected character's skill, level and hourly rate,
  and **Get a job** / **Quit job**. The press queues `HireOrder` / `QuitOrder`, applied in the system's `Inputs`.
- **Workplaces** (`Workplaces.cs`): the key is resolved as a registry building (`World.Buildings`), a town's level building or an object of its
  layout (a forge inside a smithy); `MachineData.From` decides. Employs: a functionality with operator slots (not automatic), a `stat used`,
  an owner (the building's `owner faction ID`, else the town's faction). Refused: natural deposits (function 27), training dummies, turrets,
  ruins, unowned buildings, and the player's own unless *Allow own workplaces*.
- **The job** (`AddJobType`, task type 4290, "Paid work"): hiring puts the mod job first in the character's job list (`PermaJob` with the
  building key as site) and turns Jobs on. Its `IJobSource` / row `ITaskHandler` (`WorkJob.cs`) walks to within the functionality's `use range`
  (`TaskStart` `GoTo`), then stands facing the station at its work clip (`ActionClip` + `ActionWork`, as obedient slaves stand at town work).
  Player orders run first and Jobs off stops the task (`IJobSource.Stop`); the next decision resumes it. Three walks ending short of the
  station make it wait 60 s. Removing the job in the jobs panel quits.
- **Wage**: `lerp(Wage at skill 0, Wage at skill 100, clamp(skill, 0, 100) / 100)` x `(1 + clamp(relation, 0, 100) / 100 x Bonus % / 100)`
  cats per in-game hour (`MachineData.DaySeconds / 24` seconds), accrued only while standing at the station at the clip, conscious and out of
  combat (not walking, waiting, paused, on another order or with Jobs off). Each full hour pays `floor(cats)` into `PlayerState.Money`; the
  fraction of a cat carries over. Quitting discards the unfinished hour. No bonus at own workplaces.
- **XP**: the station's skill trains through `XpService.Continuous` (reached as `CrimeSystem.Xp`) with Meitou's operating amount for that stat
  (`ProductionSystem.OperateXp`, by reflection; 0.5 without it) x 1, the same rate as operating a player machine.
- **Relations**: every *Relation hours* (8) combined work hours for a faction give *Relation points* (1) through `PlayerRelations.Add` (cause
  Trade), up to 100. Progress is kept per faction across quits and saves; *Relations enabled* off keeps it but adds nothing.
- **No products**: the employee never operates the machine (`Operating`, `SlaveWork` stay unset), so nothing is made from its work; town workers
  keep theirs.
- **Save** (`IModSaveData`): employees by name with site, partial hour and cats; relation progress by faction id. The mod job is also saved by
  the game (`meitou mjob`); a load that lost it gets it back. The state is hashed (`IStateHashed`).

## Differences and gaps

- No employment button inside a workbench's inventory window (no extension point for it); only the separate window.
- English only (the original also has Russian).
- The employee does not hold one of the building's operator slots: the AI's slot count (`AiSystem._workTaken`) is internal, so an NPC worker
  may stand at the same station.
- No functionality hunger rate while working (`BodySystem` applies it only to `SlaveWork`), and no destination-access check before the walk: a
  station behind a locked door or on an upper floor may be unreachable (it then waits and retries).
- One job per character; hiring elsewhere quits the old job. Faction names are the data's.

## Status

- **Observed** (2026-11-18): `dotnet build mods/WorkForCats -c Release` with 0 warnings, 0 errors.
- Play is **Unknown**: not run in game. To check: hire at a town smithy and a mine, walk and work clip, Jobs off / a move order pauses and
  resumes, an hour pays the expected wage, skill rises, relation +1 per 8 h, quit, save/load mid-hour, own workplaces with the setting on.
