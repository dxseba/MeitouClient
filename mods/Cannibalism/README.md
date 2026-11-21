# Cannibalism (partial port)

Port of *Cannibalism* (workshop 3797113265). Its source has no license, so this is written from a description of its behaviour only.

- **Eat Alive** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living character that is unconscious, or held in a cage or torture
  device, and not carried. Every selected character except the target eats it.
- **Walk**: each eater gets the player's move order to the victim (`World.GiveOrder`, `TaskType.MoveCusOrdered`); one that does not get within
  50 units in 40 s drops the meal.
- **Eating** (within 50 units): the eater stops, sheathes its weapon and plays the engine's `eat` clip (`CaptiveSystem.EatClip`); each tick the
  victim is eaten through `MedicalState.BeingEaten` at the engine's rate for its toughness (`MedicalState.EatRate`), vampiric eaters drinking
  instead, and the eater gains *Hunger per hour* (0.7) per in-game hour, capped at 3 (steps of at most 0.05 h).
- **Ends**: the victim dies, is carried off, wakes outside a cage or leaves it; the eater goes down, fights, is carried; or a later order gives the
  eater another task or walks it away.
- **Refused** with a message: robots, carried targets, targets in a bed, and uncaged targets that are awake.
- **Missing** (partial): the engine's eat job (its barks, screams and lost-limb drops) runs only under its own duty goals, so this mod bites
  without limb loss or barks; no eater-race or ally filter.
- **Unknown**: everything in play (the walk, the clip, the bites, the end on a later order). **Observed** (2026-11-22): builds on origin/master
  3a5c7e0e1.
