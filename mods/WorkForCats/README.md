# Work for Cats (port)

Port of *Work for Cats - Get Hired at NPC Workbenches* (workshop 3802290991, GPL-3.0). The facts below come from its shipped source
(`WorkForCats.cpp`, `Config.h`, `WorkClock.h`, `Orders.cpp`, v0.3.7); the code is Meitou's own.

- **Employment window** (`UiSlots.Overlay`, *Show employment window*): while the HUD inspects an eligible workplace
  (`PlayerState.InspectedBuilding`), a window titled with the building, the rate line and **Get a job** / **Quit job** for the first selected
  character. The press queues `HireOrder` / `QuitOrder`, applied in the system's `Inputs`. The same rate line and button show in a panel under
  the workplace's own building, bench or storage window (`AddScreenControls(..., "Root", Below, ...)`) while it is the inspected one.
- **Workplaces** (`Workplaces.cs`): a registry building, a town's level building or an object of its layout; `MachineData.From` decides.
- **The job** (`AddJobType`, task type 4290, "Paid work"): hiring puts the mod job first in the job list and turns Jobs on; its handler walks to
  the functionality's `use range` and stands at the work clip. Player orders pause it; removing the job quits.
- **No products**: the employee never operates the machine, so nothing is made from its work.
- **Save** (`IModSaveData`): employees with site, partial hour and cats; relation progress by faction id. Hashed (`IStateHashed`).

## Facts

- **Settings** (`[Employment]`): WageAtSkill0 20, WageAtSkill100 200 (whole cats, 0 to 1000000), AllowOwnWorkplaces 0, RelationsEnabled 1,
  RelationHours 8 (0.1 to 1000000), RelationPoints 1 (0 to 100), RelationBonusEnabled 1, BonusPercentAt100 20 (0 to 1000),
  ShowEmploymentWindow 1; a bad value takes its default. **Verified** (original `Config.h`, ini). Slider ends below the ini limits are Meitou's.
- **Eligible**: a production building with an owning faction (the player's only with AllowOwnWorkplaces), operator slots and a work skill; never a
  natural mine or the records `5354-furniture_otto.mod` (Brewing Booze Barrels), `1930-gamedata.base` (Grain Silo), `42167-gamedata.base`
  (Iron Refinery), `1512-gamedata.base` (Ore Processor), `42248-gamedata.base`, `42249-gamedata.base` (Iron, Copper Resource),
  `14521-rebirth.mod` (Iron Rock). **Verified** (original source). Meitou also refuses training dummies, turrets, ruins and automatic machines.
- **Working**: a living, conscious player character out of combat, doing its operate action on the bench with Jobs on and the bench job in its
  list, holding an operator slot. **Verified** (original source). Meitou: at the station at the work clip; a hire needs a free operator slot
  (`AiSystem.WorkSlots(site).Free`, "Every station here is taken."); while the job runs (walking there or working) the employee holds one
  (`AiSystem.HoldWorkSlot`), so the town's workers leave it; it is let go when the job stops, is quit or removed, and held again after a load
  (the engine does not save holds). Its own hold does not count against it in the checks (`StationFull`).
- **Wage** per in-game hour: lerp(WageAtSkill0, WageAtSkill100, clamp(skill, 0, 100) / 100) x (1 + clamp(relation, 0, 100) / 100 x
  BonusPercentAt100 / 100), the bonus only with RelationBonusEnabled at a foreign workplace. Hours and cats accrue while working; at a full hour
  the whole cats earned less the overshoot's share at the current rate are paid, the rest carries. Quitting discards the unfinished hour.
  **Verified** (original `WorkClock.h`). The original counts only consecutive observations under 0.25 h apart (Meitou: every 10 ticks).
- **Relations**: work hours add up per employer faction; each RelationHours gives RelationPoints, the relation capped at 100 and never lowered;
  progress survives quitting and saves. **Verified** (original source).
- **XP**: the station's skill trains through `CharStats::xpGeneral(dt, 1, stat)` in place of the native operating XP. **Verified** (original
  source). Meitou: `XpService.Continuous` at amount 1.
- **Window text**: "Get a job" / "Quit job"; "N cats/hour" (whole cats), employed here " | m/60 min" and " (waiting)" while not working.
  **Verified** (original source). Russian when the game's locale id (`ModContext.Language`) is `ru`, `ru_RU` or `ru-RU`, the original's test:
  the port does not carry those texts, it reads them at runtime from the installed original's shipped `Source/WorkForCats.cpp` (each English
  caption paired with the Russian of the same `russian ? ... : ...` choice) and adds them in memory as the mod's translations
  (`ModContext.AddTranslations(language, messages)`) only when all five are found (`Captions`), else English; the button shows through them by
  itself, the rate line through `context.Tr`. **Observed** (the pairing checked against the installed source with an equivalent script); not seen in a Russian game.
- **Hunger while working**: the original's employee runs the native operate action, which takes the functionality's `hunger rate` as the
  hunger situation factor (docs/game/character-stats.md). Meitou's employee stands on the job's idle task (factor 0.8 idle, else 1), so an
  `INeedRateFilter` swaps that for the workplace's rate while it stands still at the work clip out of combat (rates published by the work
  sweep). **Verified** (original source: native `OPERATE_MACHINERY`; the factor rule as the engine's docs).
- **Access check**: before each walk the job needs a free operator slot (the original's `isFreeSlot` before an approach; Meitou:
  `AiSystem.WorkSlots(site).Free`, as at the hire); an employee already standing at the station keeps working. **Verified** (original source).
- **Observed** (2026-11-18): builds. Play is **Unknown**: not run in game.

## Missing

- The panel shows only in the windows the engine opens for a building (its own machine windows); whether an NPC town's bench opens one is not
  checked.
- The approach: the original walks to within 3 units of the building's position marker and then hands over to the native operate order (retries
  after 0.5 or 2 s); Meitou walks to the use range, with a 60 s wait after three short walks, since its employee does not run the native
  operate action (that would make products and take the game's own XP).
