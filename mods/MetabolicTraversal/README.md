# Metabolic Traversal (port)

Port of *Metabolic Traversal* (workshop 3709852838, RE_Kenshi plugin, discontinued; no source published).

- **Rule** (**Verified**, original DLL (decompiled)): a hook on `calculateHungerMult` returns the game's factor times
  `idle_hunger_rate + max_movement_penalty x effort^metabolic_curve`, effort = current speed / top speed of the character's movement (0 when the
  top speed is 0.1 or less, held at 1). Every character (no player filter). Defaults in the DLL: 0.35, 6, 3 (idle 0.35x, a sprint 6.35x).
- **Config** (**Verified**): `config.json` from `./mods/Metabolic Traversal/` or `../../workshop/content/233860/3709852838/`; per line, the
  number after the first colon of the line holding the key (the shipped file has a trailing comma, so it is not strict JSON). Port: this mod's
  folder, else the original's Workshop folder beside it; the values become the Mod settings' defaults.
- **Port**: an `INeedRateFilter` multiplies every body update's hunger rate (`NeedKind.Hunger`: race rate x the game's situation factor, which
  only drives the empty-stomach drain, docs/game/character-stats.md "Drain and digestion"; the HUD's figure too) by the factor, as the
  original's hook multiplies `calculateHungerMult`. Effort: the last finished tick's horizontal velocity over `MaxSpeed`. Options:
  *Metabolic hunger* (on), *Player characters only* (off).
- **Unknown**: not run in game; whether the original's speed is horizontal or full 3D.

## Missing

- Nothing known.
