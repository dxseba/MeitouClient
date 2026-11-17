# Disarm (port)

Port of dafitime's *Disarm* (workshop 3759461630; rules from its GPL-3.0 source, [dafitime/disarm](https://github.com/dafitime/disarm)).

- **Disarm** (`UiRegistration.AddCharacterMenuVerbs`): a "Disarm NN%" row on a downed, sleeping, or (while the actor sneaks) awake target.
  Chance: downed 100; skill = 0.65 Thievery + 0.35 Stealth; asleep `10 + 0.9 skill` (max 100); awake `5 + 0.85 skill - 0.3 Perception`
  (max 95, 0 once the actor is detected); times *Chance multiplier*, floor `min(5, 5 x multiplier)`. The first selected character with a chance
  walks over (`MoveOrder`), and the roll happens once within 30 units (20 s limit). Success drops (or destroys) the hip and back weapons and gives
  Stealth XP 15 (event gain; not on a downed target). Failure is the engine's caught-stealing reaction (`CrimeSystem.Roll` with chance 0, by
  reflection): the target wakes, a pending Theft, the stole-from-me memory.
- **Kick**: a Harmony prefix on `GroundItemPicks.GroundItemClick` opens the game's hold menu on a loose ground item: Pick up (also the quick
  click) and Kick. The actor walks over; the item moves *Kick distance* + 0..6 along the actor-item line, stopping before unwalkable ground or a
  step over 2 units.
- **Knockout drop**: a character newly unconscious drops its wielded weapon with chance `55 - 0.4 Strength - 0.2 Athletics` (times the
  multiplier, 5..90); any cause, not only the player's blows.
- **AI rearm (beta, off)**: a disarmed character back on its feet gets a pick-up order for the nearest ground weapon within 200. The engine's
  `FindWeapon` task has no behaviour, so this is the substitute.
- **Settings**: the original's ten, under Mod settings. *Allow stolen flag* marks dropped weapons with the target's faction (`StolenMark`).
- Animals are never targets; the player's own characters only with *Allow on own squad*.
- Differences: no approach animation or Escape cancel; the menu shows the chance of the first selected character only.
- **Observed** (2026-11-17, live sandbox on origin/master f7ad84a35): the KO disarm posted "Stone has been disarmed." with her staff on the
  ground. **Observed** (2026-11-18, headless sandbox fight, all nine mods): loads, the kick patch applies, 3000 ticks without a fault. **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play):
  the chance rows, Kick and the knockout drop work. Rearm (beta, off) is **Unknown**.
