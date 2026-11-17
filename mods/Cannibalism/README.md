# Cannibalism (port)

Port of *Cannibalism* (workshop 3797113265). Its source has no license, so this is written from a description of its behaviour only.

- **Eat Alive** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living character that is unconscious, or held in a cage or torture
  device, and not carried. Every selected character except the target eats it.
- **The job is the engine's own** (`CaptiveSystem`, `TaskType.EatTargetAlive` 220). The mod adds a `CaptiveJob` to the private
  `CaptiveSystem._jobs` list by reflection, the way a player animal's race goal adds one: `PlayerSide`, `Engaged`, last task noted. A caged or
  racked victim gets `CaptiveJobKind.Eat` at its cage (`InWhat`). Any other victim gets `EatBody` where it lies. The engine then runs the walk,
  sheathes the weapon, plays the `eat` clip, bites through `MedicalState.BeingEaten`, plays the barks and screams, and ends the job when the
  victim dies or leaves.
  - Read from the code: `EatBody` takes any eater and any dead or downed victim that is not carried or held, so it works for a player worker
    on an uncaged victim. `Eat` needs the victim imprisoned in that cage. Row 220's repertoire (0x61) includes humans.
- **Range 50**: while a job is walking, this mod starts the eating once the eater is within 50 units of the victim. The engine's own range is 15
  units, or 15 plus a cage's use range. Only this mod's jobs are affected.
- **Feeding**: while one of this mod's eaters is in the eating phase, it gains *Hunger per hour* (default 0.7) per in-game hour
  (`MedicalState.Hunger`, capped at `MaxHunger` 3). Each step is at most 0.05 h.
- **Ends**: on any later player order (the job's override test), or when the eater fights, flees, is carried or goes down, or the victim
  dies, wakes outside a cage, is carried off or leaves the cage. The engine posts "{1} has been eaten alive." for the player's own.
- **Refused** with a message: robots (the engine's cage job gives up on them), carried targets, targets in a bed, and uncaged targets that are
  awake.
- **Settings**: *Enable Cannibalism*, *Hunger per hour*.
- Differences: the chosen characters must be the player's and free (not down, fighting, carried or held). Several eaters on one victim each
  bite, so it dies faster. There is no eater-race or ally filter.
- **Observed** (2026-11-18): builds against the current engine (0 warnings). **Unknown**: everything in play (the walk and eating for a player
  human, the range-50 start, the feeding, the job ending when the player gives another order).
