# Cannibalism (port)

Port of *Cannibalism* (workshop 3797113265, an RE_Kenshi plugin; its shipped source carries no license, so this is our own code written from
the facts below).

- **Eat Alive** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living character that is unconscious, or held in a cage or torture
  device (carried ones, robots and characters in beds too, as the original). Every selected character except the target gets the game's task
  220 Eat target alive on it (`World.GiveOrder(eater, TaskType.EatTargetAlive, target)`).
- **Cage row** (`AddMenuVerbs`): on a cage whose occupant qualifies, a player-built one (kind `Building`) or a town's or camp's (kind
  `Furniture`, its fixture id): the cage fixture of the same id, else the nearest one within 6 units.
- **The meal** is the engine's task 220 (docs/modding.md "Ordering characters"): it walks the eater there and eats a live caged occupant at its
  cage, or a body down on the ground where it lies, with the cannibal job (clip, bites, toughness XP, barks, limb loss, the eaten-alive notice,
  the death report); a target it cannot eat (a robot, a carried body, one in a bed) is refused by the task's check, and another order ends it.
  One eater per victim: the engine gives a second eater's order up while another's job holds the victim or its cage.
- **Feeding**: an eater whose order in hand is a started task 220 and whose meal is at its bites regains *Hunger per hour* (0.7) per in-game
  hour, capped at 3, in steps of at most 0.05 h; the first bite only arms its clock. The cannibal goals' own meals (NPCs) are not fed.
- **Verified** (original source and DLL): the order is the native task 220 `EAT_TARGET_ALIVE` for the selected characters; the row shows on a
  character that is unconscious or `IN_PRISON` (cage or torture device), not the selected eater, and also on a cage (`BF_CAGE`) whose occupant
  qualifies; the eat trigger is widened to 50 units only while the eater's goal is 220; feeding 0.7 hunger per in-game hour of bites, cap 3.0,
  steps at most 0.05 h, the first bite only arms the clock, only for eaters on goal 220.
- **Difference**: the original widens task 220's eat trigger to 50 units (its `AI::isWithinChoppingRange` hook); the engine's job starts at its
  own reach (15, or a cage's use range + 15), which a mod cannot change (`CaptiveSettings.Reach` is fixed for all captive jobs).
- **Unknown**: whether the native task lets several eaters share a victim, and keeps eating once it dies; everything in play.

## Missing

- The 50-unit eat trigger (above): needs a per-task reach on the engine's captive jobs. Otherwise nothing; not checked in game.
