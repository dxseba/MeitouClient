# BowMod (port)

Port of *BowMod [1.0]* (workshop 3838294517), from its plugin (`BowSkills.dll`, Ghidra decompile), its `fcs.def`, its data and its
`Localization/*.json`. The original is a required item: enabled, its `.mod` (six bows as CROSSBOW records, two arrow sizes, research, vendors,
animations) loads as data; the port reads its translations at start.

- **Bows skill** (`AddSkill`, id `bowmod.archery`, under the Weapons column): its own level, start 1, saved per character.
- **Using a bow** (a CROSSBOW with `bowmod bow` set): wherever the game's ranged steps read the holder's Crossbows level (reload time, aim
  spread, the shot's skill, the HUD's accuracy figure), they read the Bows level's effective proficiency instead, with the injury, gear and light multipliers as for
  Crossbows; the Crossbows experience those steps give (reloading, aiming) trains Bows instead. Precision Shooting and Dexterity / Perception
  experience are shared as before.
- **Effective proficiency**: `0.65 L` up to 60; `39 + 61 ((L - 60) / 40)^1.25` from 60 to 100; 100 at 100 and above; 0 at 0 or below.
- **Texts**: the skill's name, description and pane lines, and the bows', arrows' and research's names and descriptions (data phase), in the
  game's language from the original's `Localization/<first two letters>.json` (en, es, ja, ko, pt, ru, zh).
- **Saves**: Meitou keeps the level as `meitou skill bowmod.archery`; the port also writes the original's `bowmod skill` and reads it from a
  save that has only that (one the original wrote).

## Facts (Verified: decompile of `BowSkills.dll` and the bytes of the Workshop build, unless marked)

- Curve (`FUN_180001060`, constants at `0x18000f730`: 39, 61, 40, 1.25, 100, 0.65, 60), as above.
- Stat 10001 (`0x2711`) named `bowmod.archery`, added to the stats window after Crossbows (stat 35, `0x23`) with the `BOW_LABEL` and
  `BOW_DESCRIPTION` texts (`FUN_180004820`); a stat 10001 another plugin already owns disables BowMod.
- Bow test (`FUN_180003ba0`): the character's equipped crossbow's record has the bool `bowmod bow` true (`fcs.def`: `[CROSSBOW] bowmod bow`).
  Six records of `BowMod.mod` set it (400 to 405).
- The stat getter hook (`FUN_180003180`): stat 10001 returns the stored Bows level; stat 35 while a bow step runs on that character (thread
  context set by the hooks `FUN_180003d60`, `FUN_180003df0`) returns the curve of the Bows level; both times
  `MedicalSystem::getHealthStatModifier(35, ...)` unless injuries are ignored.
- XP (`FUN_180003e80` sets the context, `LAB_180003280` returns the Bows storage for stat 35): the game's Crossbows experience during a bow
  step goes to the Bows level. The skill-up message shows the Bows label (`LAB_1800010d0`, `FUN_1800039b0`).
- Pane (`FUN_180004320`): "Affected attributes", accuracy and drawing / reload speed as percentages of the game's own crossbow functions with
  the Bows context, then the training lines.
- Save (`FUN_1800032d0`, `FUN_180003f30`): STATS float `bowmod skill` written on save; on load a value not finite, below 0 or above 100 reads 1,
  as does a missing one.
- **Unknown**: not checked in game.

## Port choices

- Meitou's ranged steps are swapped whole: the stored Crossbows level is set to the effective Bows level for the step and put back after it;
  what Crossbows gained is taken back through the core curve and the Crossbows XP rate and given to Bows on its own level.
- The pane lists the affected attributes without the holder's live percentages (a code mod's pane lines are fixed texts).
- The HUD's Current Skill line keeps the Crossbows label (its number is the bow's).
- The original relabels a skill-up text to Bows; **Observed** (search of `src`): Meitou draws no skill-up text, so there is nothing to relabel.

## Harmony

- `Meitou.Simulation.Combat.CombatSystem.TickRanged`, `Meitou.Simulation.Combat.CombatSystem.ApplyFire` and `Meitou.Simulation.World.SkillLine`
  (prefix + finalizer): the swap.
- `Meitou.Simulation.Bodies.CharacterStats.WriteSave` and `Meitou.Simulation.Bodies.CharacterStats.ReadSave` (postfixes): the `bowmod skill`
  field.
- Each logs once on a failure and leaves the vanilla step or save as it is.

## Missing

- The bow animations' plugin side (held bow bound to `Bip01 L Hand`, `bowmod_aim_hold`, the `bowmod_release` action on a shot, the split
  `bowmod_sprint_upper` / `bowmod_sprint_lower` run): Meitou plays the crossbow poses itself and offers no mod hook for a ranged character's
  clips or a held item's bone (`AddCombatMove` is not asked during a crossbow pose); a patch would have to replace the animation layer's
  crossbow handling.
