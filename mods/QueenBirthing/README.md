# Queen Birthing (partial port)

Port of *Queen Start with Birthing* (workshop 3777203526), rebuilt from its Workshop description.

- **Give Birth** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living, conscious player character whose race name contains "Hive Queen"
  (the vanilla race). The queen pays *Hunger cost* (100 of 300; refused if it would leave her below 100) and a drone joins her squad on the
  first walkable point of rings 12-45 units round her (`PopulationSystem.SpawnIntoPlayerSquad`), knocked out for *Knockout seconds* (60).
- **Drone**: the CHARACTER record named "Hive Worker Drone", else the first CHARACTER whose race is the RACE of that name (logged at start).
- **Missing** (partial): the button in the queen's inventory window (a menu row instead) and the exiled-queen game start (the original's
  `.mod` data, not shipped); the original's hunger cost and knockout length are not published (ours are settings). A full squad (`max squad
  size`) refuses the birth.
- **Observed** (2026-11-19, headless sandbox fight with 152 data mods): loads, the lookup picked a "Hive Worker Drone" CHARACTER record from
  `rebirth.mod`; 3000 ticks without a fault. **Unknown**: the birth itself, not checked in game.
