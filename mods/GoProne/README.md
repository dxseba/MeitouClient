# Go Prone (partial port)

Port of *Go Prone* (Genpretz, Nexus 1830), rebuilt from its Nexus description.

- **Toggle prone** (`AddKeyAction`; unbound by default, as the original's; bind it on the Controls page): the selected player characters that
  are sneaking lie down into the engine's staying-low posture (`MedicalState.StayingLow`, [ragdolls](../../docs/game/ragdolls.md) "Staying
  low": stealth x2, sight x0.5, crawl speed 7, grounded clips); pressed again while any of them is prone, they get up through the game's own
  Stand Up command. One not sneaking is told so.
- The engine ends it as it ends staying low: a speed order, sneak off, a leg down, a melee attack, a caught thief's watcher.
- **Missing** (partial): the lying-down transition (the posture is set at once; getting up plays `squat T` backwards); the key cannot carry a
  modifier (the original suggests Ctrl+Z; a key action is one key). Staying low is not saved, so prone characters stand after a load.
- **Unknown**: not checked in game, including how the posture looks when set this way.
