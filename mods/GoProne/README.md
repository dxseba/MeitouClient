# Go Prone (partial port)

Port of *Go Prone* (Genpretz, Nexus 1830), rebuilt from its Nexus description.

- **Toggle prone (Ctrl)** (`AddKeyAction`; unbound by default, as the original's; bind it on the Controls page, Z for the original's
  suggested Ctrl+Z): pressed with Ctrl held (`ModKeyContext.Ctrl`; the key alone does nothing), the selected player characters that
  are sneaking play `squat T` forwards for 1 s (`AddCombatMove` with `CombatMove.Clip`, the mirror of Stand Up's backwards play; the
  same checks again at its end) and then lie in the engine's staying-low posture (`MedicalState.StayingLow`, [ragdolls](../../docs/game/ragdolls.md) "Staying
  low": stealth x2, sight x0.5, crawl speed 7, grounded clips); pressed again while any of them is prone, they get up through the game's own
  Stand Up command; pressed while they are still lying down, they stop. One not sneaking is told so.
- The engine ends it as it ends staying low: a speed order, sneak off, a leg down, a melee attack, a caught thief's watcher.
- **Missing** (partial): staying low is not saved, so prone characters stand after a load. The original's own lying-down clip and
  its length are unknown (no files); `squat T` over 1 s is a stand-in.
- **Unknown**: not checked in game, including how the posture looks when set this way.
