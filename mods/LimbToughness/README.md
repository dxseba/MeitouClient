# Limb Toughness (partial port)

Port of *LimbToughness - Max HP to Limb* (workshop 3787222853), rebuilt from its Workshop description.

- **Bonus**: every 60th tick each living character's parts get their HP multiplier (`HealthPart.HpMultiplier`; maximum = base x multiplier x
  scale - wear) set to the one first seen times 1 + *Health % per Toughness* (0.5) x stored Toughness / 100: 50 Toughness = +25%, additive.
  A part keeps its share of health as the maximum moves. The multiplier is not saved, so a load starts from the engine's own again.
- **Options**: *Robot races* (on: races marked `is robot`), *Prosthetic limbs* (off: fitted robotic limbs keep their own maximum).
- **Missing** (partial): animals (they have no stats object; the engine resets their multiplier from their size every tick), the original's
  ApplicationMode choices and ini.
- **Unknown**: not checked in game; how the health panel shows the raised maximum.
