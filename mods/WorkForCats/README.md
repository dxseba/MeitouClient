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
  (`AiSystem.WorkSlots(site).Free`, "Every station here is taken."), but the employee then holds none, so NPCs may still fill the station.
- **Wage** per in-game hour: lerp(WageAtSkill0, WageAtSkill100, clamp(skill, 0, 100) / 100) x (1 + clamp(relation, 0, 100) / 100 x
  BonusPercentAt100 / 100), the bonus only with RelationBonusEnabled at a foreign workplace. Hours and cats accrue while working; at a full hour
  the whole cats earned less the overshoot's share at the current rate are paid, the rest carries. Quitting discards the unfinished hour.
  **Verified** (original `WorkClock.h`). The original counts only consecutive observations under 0.25 h apart (Meitou: every 10 ticks).
- **Relations**: work hours add up per employer faction; each RelationHours gives RelationPoints, the relation capped at 100 and never lowered;
  progress survives quitting and saves. **Verified** (original source).
- **XP**: the station's skill trains through `CharStats::xpGeneral(dt, 1, stat)` in place of the native operating XP. **Verified** (original
  source). Meitou: `XpService.Continuous` at amount 1.
- **Window text**: "Get a job" / "Quit job"; "N cats/hour" (whole cats), employed here " | m/60 min" and " (waiting)" while not working.
  **Verified** (original source; it also has the Russian captions).
- **Observed** (2026-11-18): builds. Play is **Unknown**: not run in game.

## Missing

- Holding an operator slot for the employee: `WorkSlots` is read only, so an NPC may take the station after the hire.
- The panel shows only in the windows the engine opens for a building (its own machine windows); whether an NPC town's bench opens one is not
  checked.
- Russian captions.
- The original's approach (to within 3 units of the building's position marker, retries after 0.5 or 2 s) is Meitou's walk to the use range,
  with a 60 s wait after three short walks.
- No functionality hunger rate while working, no access check before the walk.
