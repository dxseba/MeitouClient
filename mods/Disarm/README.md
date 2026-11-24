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
- **AI rearm (beta, off)**: a disarmed character is watched 300 s; while up, awake, unarmed and with no order in hand it is ordered again and
  again to pick up the nearest ground weapon within 200; one whose Martial Arts is at least its Melee Attack is not sent. For the first 45 s
  a more urgent goal defers it, by the HUD goal text as the original reads it (`AiSystem.ActivityText`: Medic, Fixing Legs, Protect close
  allies, Attacking others, Attacking enemies, Self preservation, Find and rescue), or a fight (Meitou gives a fight the AI did not start no
  goal text). The engine's `FindWeapon` task has no behaviour, so a pick-up order is the substitute.
- **Settings**: the original's ten, under Mod settings. *Allow stolen flag* marks dropped weapons with the target's faction (`StolenMark`).
- Animals are never targets; the player's own characters only with *Allow on own squad*.
- **Verified** (original source `Disarm.cpp` and its DLL): the formulas, distances (18 / 28 / 30, 5 s, 10 and 50 stuck cycles of 300 ms),
  XP 15 on every success, the down states (`PS_KO`, `PS_PLAYING_DEAD`, `PS_CRIPPLED`), the row's conditions, the rearm timings (300 s, 500 ms
  retries, 45 s defer, Melee Attack > Martial Arts) and the defaults (rearm and own squad off). The shipped `DisarmSettings.ini` is the
  author's saved file with `AllowAIRearm=1` and `AllowDisarmOwnSquad=1`, so a fresh install of the original runs with both on.
- Differences (engine): no Escape cancel (the original polls the key; a mod can neither bind nor read Escape); Pick up is not tinted red
  for a theft (hold menu rows have no colour); the labels are English only (the shipped DLL holds Disarm, Kick, Pickup and its settings'
  labels in nine languages; Meitou has no per-mod translation API); the rearm is a pick-up order (no `FindWeapon` behaviour). The actor is
  the first selected character with a chance (the original: the primary selected character).
- **Observed** (2026-11-17, live sandbox on origin/master f7ad84a35): the KO disarm posted "Stone has been disarmed." with her staff on the
  ground. **Observed** (2026-11-18, headless sandbox fight, all nine mods): loads, 3000 ticks without a fault. **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play):
  the chance rows, Kick (then a Harmony hold menu) and the knockout drop work. The Kick row through `AddMenuVerbs` and the rearm (beta, off)
  are **Unknown** in play.
