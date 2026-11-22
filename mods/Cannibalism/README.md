# Cannibalism (port)

Port of *Cannibalism* (workshop 3797113265, an RE_Kenshi plugin; its shipped source carries no license, so this is our own code written from
the facts below).

- **Eat Alive** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living character that is unconscious, or held in a cage or torture
  device, and not carried. Every selected character except the target eats it.
- **Walk**: each eater gets the player's move order to the victim (`World.GiveOrder`, `TaskType.MoveCusOrdered`); one that does not get within
  50 units in 40 s drops the meal (engine choice).
- **Eating** (within 50 units): the eater stops, sheathes its weapon and plays the engine's `eat` clip (`CaptiveSystem.EatClip`); each tick it
  bites as the engine's own eat job does: a victim still awake trains toughness (continuous XP, amount 10), one playing dead gets up, the
  victim is eaten through `MedicalState.BeingEaten` at the engine's rate for its toughness (limbs lost when dismemberment is on, never dropped as
  items; the lost-limb cry; vampiric eaters drink), the eater barks event 52 every 10 s (a caged victim screams event 11), and the player's own
  victim posts "{name} has been eaten alive." The eater regains *Hunger per hour* (0.7) per in-game hour, capped at 3, in steps of at most
  0.05 h.
- **Ends**: the victim dies, is carried off, wakes outside a cage or leaves it; the eater goes down, fights, is carried; or a later order gives the
  eater another task or walks it away.
- **Refused** with a message: robots (the engine's eat job gives up on them), carried targets, targets in a bed, and uncaged targets that are
  awake.
- **Verified** (original source and DLL): the order is the native task 220 `EAT_TARGET_ALIVE` for the selected characters; the row shows on a
  character that is unconscious or `IN_PRISON` (cage or torture device), not the selected eater, and also on a cage (`BF_CAGE`) whose occupant
  qualifies; the eat trigger is widened to 50 units only while the eater's goal is 220; feeding 0.7 hunger per in-game hour of bites, cap 3.0,
  steps at most 0.05 h, the first bite only arms the clock, only for eaters on goal 220.
- **Missing**: the row on a cage's own right-click menu (mod menu rows are for characters only: engine gap, a building-menu verb hook); the
  native task itself (the engine's eat jobs run only under duty goals, so the mod drives the meal: a player-order handler for task 220 would
  replace it); the engine's death report for this kill (`DeathObservers` is internal; the death is still reported by its sweep).
- **Differences**: the original shows the row on unconscious robots, carried bodies and characters in beds too, leaving the outcome to the
  native task; here those are refused with a message.
- **Unknown**: whether the native task keeps eating once the victim dies; everything in play (walk, clip, bites, barks, end on a later order).
