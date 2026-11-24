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
- **Pane and HUD**: the Bows pane shows the selected character's accuracy and drawing / reload speed, the game's Crossbows figures at the
  effective Bows level; the HUD's Current Skill line of a bow holder says Bows.
- **Animations** (a bow in hand; the clips are the original's ANIMATION records, read at runtime from its `bowmod_male.skeleton` /
  `bowmod_female.skeleton`): the bow sits in the left hand; standing, the aim is `bowmod_aim_hold` and the shot `bowmod_release`; moving faster
  than 0.1, `run lower` becomes `bowmod_sprint_lower` with `bowmod_sprint_upper` over it, and no aim or bow clip plays.

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
- Bow in hand (`FUN_180005ba0` plus the weapon-in-hand test): the equipped crossbow is a bow and is the weapon in hand. Standing
  (`FUN_180005ef0`): not moving, speed under 0.1 (`0x18000fe58`), not down, dead, in a ragdoll or stumbling. Moving (`FUN_180006000`): moving and
  speed over 0.1, same exclusions.
- Left hand (`FUN_180005d50` around `AppearanceBase`'s attach of the `hands` entity, `FUN_180005640` on Ogre `Entity::attachObjectToBone`): a
  bow holder's attach to `Bip01 Prop2` goes to `Bip01 L Hand` when the skeleton has it, orientation Q x the given one and position Q x the given
  one + (0.954711, -1.088662, -0.654805), Q = Ogre (w 0.025419, x -0.593545, y -0.016245, z -0.804235) (`0x18000fff4`, `0x180010010`).
- Clip swaps (hooks `FUN_180006e20`, `FUN_180006f70`, `FUN_1800071e0` on the animation class's play calls): standing, `aimH` or `aimwalk` plays
  `bowmod_aim_hold` (when the skeleton has it); moving, `aimH`, `aimwalk`, `stand 1 crossbow` and every `bowmod_` clip are refused except the two
  sprint clips; `run lower` while moving plays `bowmod_sprint_lower`; `bowmod_sprint`, `bowmod_idle`, `bowmod_aim`, `bowmod_equip` and
  `Standing Sprint Forward` are always refused (retired, log "Overdraw aim and equip remain retired").
- Release (`FUN_180007730`): a standing bow holder whose bow's shot count drops plays the action `bowmod_release` (rate 1, weight 1).
- Sprint upper (`FUN_180007bc0`, after the animation update): while moving, `bowmod_sprint_upper` plays at the weight and time of the
  `bowmod_sprint_lower` layer, else it is stopped.
- Data (`BowMod.mod`): the six clips are ANIMATION records of category 8 (crossbow); `bowmod_aim_hold`, `bowmod_release`, `bowmod_nock` and
  `bowmod_reload` layer `all`, the sprint pair `lower` / `upper`, synched, min speed 45; ANIMATION_FILE `BowMod animations` (male / female
  skeleton) is linked from the races; the bows' `reload anim` is `bowmod_reload`, which Meitou already plays.
- **Unknown**: not checked in game, the left-hand offset in Meitou's bone frame in particular.

## Port choices

- Meitou's ranged steps are swapped whole: the stored Crossbows level is set to the effective Bows level for the step and put back after it;
  what Crossbows gained is taken back through the core curve and the Crossbows XP rate and given to Bows on its own level.
- Pane numbers: the game's stats-window Crossbows readouts (accuracy and rate of fire, Meitou's formulas) computed again with the stored
  Crossbows level set to the effective Bows level, under the Bows stat, labelled with the pane's Affects rows.
- Aim timing: Meitou's aim pose runs over the aim time (first half of `aimH`) and the shot (second half); for a standing bow holder the first part
  is `bowmod_aim_hold` stretched over the aim and the second `bowmod_release` over the shot, instead of the original's own rates.
- Sprint upper: added to the wanted clips at the sprint lower's weight; the other upper clips give way by that share. Both clips synch, so the
  upper follows the lower's gait phase as in the original.
- The always-refused retired names are not in the original's data, so the port does not filter them.
- The original relabels a skill-up text to Bows; **Observed** (search of `src`): Meitou draws no skill-up text, so there is nothing to relabel.

## Harmony

- `Meitou.Simulation.Combat.CombatSystem.TickRanged`, `Meitou.Simulation.Combat.CombatSystem.ApplyFire` and `Meitou.Simulation.World.SkillLine`
  (prefix + finalizer): the swap.
- `Meitou.Simulation.Bodies.CharacterStats.WriteSave` and `Meitou.Simulation.Bodies.CharacterStats.ReadSave` (postfixes): the `bowmod skill`
  field.
- `Meitou.Simulation.Movement.AnimationSystem.Update` (prefix): aim hold, release and no aim while moving. `Meitou.Simulation.Movement.AnimationSystem.PickGait`
  (postfix): the split sprint. `Meitou.Simulation.Movement.AnimationSystem.Plays` is called (not patched) to skip clips a skeleton lacks.
- `Meitou.Game.Rendering.Characters.CharacterContent.AttachToBone` (postfix): the left hand.
- `Meitou.Simulation.Bodies.HudTips.StatsWindow` (postfix; calls `Meitou.Simulation.Bodies.HudTips.AddSkillReadouts`),
  `Meitou.Game.Ui.StatsWindow.AddModSkillDescription` (prefix + finalizer) and `Meitou.Game.Ui.StatsWindow.AddDescriptionPanels` (prefix): the
  pane numbers. `Meitou.Simulation.World.SkillLine` (postfix): the Bows label.
- Each logs once on a failure and leaves the vanilla step or save as it is.

## Missing

- The bow mesh's own string animation (the original poses the bow's skeleton from the aim-hold time, frame 31 at full draw) and its moved
  loaded-arrow nodes: Meitou draws an attached weapon as a rigid mesh on one bone; posing a held item's own skeleton needs the character renderer
  to skin attached parts.
