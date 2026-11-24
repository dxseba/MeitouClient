# Disarm (port)

Port of dafitime's *Disarm* (workshop 3759461630; rules from its GPL-3.0 source, [dafitime/disarm](https://github.com/dafitime/disarm)).

- **Disarm** (`UiRegistration.AddCharacterMenuVerbs`): a "Disarm NN%" row on a downed (knocked out, crippled or playing dead), sleeping, or
  (while the actor sneaks) awake target that has a weapon equipped and is not carried; never for an animal actor. Chance: downed 100; skill =
  0.65 Thievery + 0.35 Stealth; asleep `10 + 0.9 skill` (max 100); awake `5 + 0.85 skill - 0.3 Perception` (max 95, 0 once the actor is
  detected); times *Chance multiplier*, floor `min(5, 5 x multiplier)`. The first selected character with a chance walks over (`MoveOrder`,
  aimed at the target again when it moves more than 5 or the walk ends). The roll happens once: within 18; within 28 once the actor is caught
  (chance 0) or has come no nearer for 3 s; within 30 after 5 s. It gives up after 15 s without progress, and is cancelled by another order
  (an order in hand that is not this walk). Success drops (or destroys) the hip and back weapons (then `CharacterRefresh.Refresh`) and gives Stealth XP 15 (event gain, on
  every success). Failure is the engine's caught-stealing reaction (`CrimeSystem.StealRoll` with chance 0): the target wakes, a
  pending Theft, the stole-from-me memory.
- **Kick**: a row on a loose ground item's hold menu (`UiRegistration.AddMenuVerbs`, kind `GroundItem`), after the game's Pick Up (also the
  quick click). The actor (never an animal) walks over; the item moves *Kick distance* + 0..6 the way the actor faces (`CharacterHot.Yaw`, the
  original's movement facing direction), keeping its turn, and stops before unwalkable ground or a step over 2 units (the original casts a ray
  and stops 0.25 short of a hit).
- **Knockout drop**: a character newly down (knocked out, crippled on the ground or playing dead) drops its wielded weapon with chance
  `55 - 0.4 Strength - 0.2 Athletics` (times the multiplier, 5..90); any cause, not only the player's blows.
- **AI rearm (beta, off)**: a disarmed character is watched 300 s; while up, awake, unarmed and with no order in hand it is given the game's
  Finding weapon job again and again (`World.GiveOrder(id, TaskType.FindWeapon)`, the original's FIND_WEAPON: the engine's `AI::findWeapon`
  picks up the ground weapon it finds, or loots a body); one whose Martial Arts is at least its Melee Attack is not sent. For the first 45 s
  a more urgent goal defers it, by the HUD goal text as the original reads it (`AiSystem.ActivityText`: Medic, Fixing Legs, Protect close
  allies, Attacking others, Attacking enemies, Self preservation, Find and rescue), or a fight (Meitou gives a fight the AI did not start no
  goal text).
- **Escape** (`UiRegistration.AddKeyInput`): with a disarm pending, Esc drops it and stops the actors (`StopCommand`, the original's
  `clearAllAIGoals`); the key still goes on to the game, as the original only polls it. **Unknown**: the original ignores Esc while its
  options window shows; Meitou's settings page takes the keys itself (not checked).
- **Theft tint** (`UiRegistration.AddMenuRowOverrides`): the hold menu's Pick Up on another faction's ground item (`GroundItemSnapshot.OwnedBy`
  not the player's faction, a theft) is red (0.85, 0.15, 0.15), the original's Pickup colour for a steal (its DLL's constants).
- **Labels**: the original holds Disarm, Kick, Pickup and its settings' labels in nine languages in its DLL (no catalogue). At start the port
  reads those of the game's language from the installed original (`OriginalLabels.cs`: each label is one call of a picker taking nine string
  addresses, English, de, es, fr, pt, ru, ja, ko, zh; the reader follows the `lea`/stack stores before each call and keeps only complete,
  valid sets under one picker), writes them as a gettext catalogue in the temporary folder and adds it (`ModContext.AddTranslations`); the
  menu rows and settings labels show through it. Without the original, or in English, they stay English. **Verified** by a scratch run (not
  in the repo) against the installed DLL: 12 labels for each of the eight languages, none equal to its English. The settings labels are the
  original's English ones.
- **Settings**: the original's ten, under Mod settings. *Allow stolen flag* marks dropped weapons with the target's faction (`StolenMark`).
- Animals are never targets; the player's own characters only with *Allow on own squad*.
- **Verified** (original source `Disarm.cpp` and its DLL): the formulas, distances (18 / 28 / 30, 5 s, 10 and 50 stuck cycles of 300 ms),
  XP 15 on every success, the down states (`PS_KO`, `PS_PLAYING_DEAD`, `PS_CRIPPLED`), the row's conditions, the rearm timings (300 s, 500 ms
  retries, 45 s defer, Melee Attack > Martial Arts) and the defaults (rearm and own squad off). The shipped `DisarmSettings.ini` is the
  author's saved file with `AllowAIRearm=1` and `AllowDisarmOwnSquad=1`, so a fresh install of the original runs with both on.
- Difference: the actor is the first selected character with a chance (the original: the primary selected character).
- **Observed** (2026-11-17, live sandbox on origin/master f7ad84a35): the KO disarm posted "Stone has been disarmed." with her staff on the
  ground. **Observed** (2026-11-18, headless sandbox fight, all nine mods): loads, 3000 ticks without a fault. **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play):
  the chance rows, Kick (then a Harmony hold menu) and the knockout drop work. The Kick row through `AddMenuVerbs` and the rearm (beta, off)
  are **Unknown** in play.

## Missing

- Nothing. The Escape cancel, the theft tint, the translated labels and the Finding weapon rearm are not checked in game.
