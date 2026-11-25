# Action RPG

Port of *Action RPG (RE_Kenshi)* v328 (cjy233, workshop 3824431439; GPLv3), the fork of Direct Control 1.5.0, from its README, NOTICE,
shipped `WASDCombatPlugin.ini` and DLL (Ghidra decompile). The original is the required Workshop item: its `Settings.<locale>.tsv` are read from its installed folder.

**Design choice:** the fork keeps Direct Control's DLL name, ini and V mode and changes the control scheme, so the port is the
[Direct Control](../DirectControl/README.md) port's core (`../DirectControl/Core`, compiled in again) run with the fork's scheme and defaults
(`ControlSettings.ForActionRpg`). The two are declared incompatible (`meitou.json`), as the originals load the same DLL and hooks.

- **V** turns V mode on for the selected character: the shoulder camera with a centred crosshair and mouse look, WASD at a walk (Shift held:
  run; the Run lock key, unbound by default: run until pressed again), relative to the view. **F** switches between the shoulder camera and
  first person; the wheel zooms the shoulder camera and its nearest step enters first person.
- Mouse combat, always on in V mode: hold the left button and move the mouse to draw a direction, release to swing that way (up: the downward
  cut, down: the thrust, right: the left cut, left: the right cut; a weapon with no technique of that direction does not swing). **Ctrl+L**
  locks the drawn direction for every swing (again: unlocked). Hold the right button to block in the direction last drawn (within 0.8 s;
  none: any side; right: the right block): a blow from the front of that side is blocked whatever the skill. Arrows round the crosshair show
  the drawn direction (white) and the locked one (amber, with an L); a red X flashes when your blow lands. With
  "Character still blocks on their own" (auto-block) on, the held button blocks with the game's own roll and side and the Block order's bonus.
  No step while the block is held. The middle button locks on to the enemy under the crosshair (the view follows it, so W/S close in and back off
  and A/D circle it); press again to unlock.
- **E** uses what the crosshair points at (the game's own right click there: talk, loot, open, pick up, the steal and lock-pick rows with their
  red cursor); **Shift+E** with the weapon sheathed treats your own wounds. **R** draws or sheathes the weapon (V mode starts sheathed; no
  swing or block sheathed; the game drawing it by itself draws it here too), **Q** switches between the back and hip weapons.
- With a crossbow drawn, Attack orders a shot at the aimed enemy (the game's ranged controller aims, reloads and fires); on a turret, Attack
  orders the turret to shoot the enemy at the crosshair.
- The combat focus HUD hides the squad, orders, shortcut, floor, build and jobs panels while the view is held (setting).
- The settings and key rows show in the game's language from the fork's `Settings.<locale>.tsv` (read from the installed original) where its
  rows match the port's.
- **Ctrl+F1** the rest of the squad follows you; **Ctrl+F2** they walk to where the crosshair meets the ground.
- The target's name and blood, head, chest and stomach bars over it. Settings and keys as Direct Control's, with the fork's defaults.

## Facts (Verified: shipped `WASDCombatPlugin.ini`, README, unless marked)

- Ini defaults: `ManualBlock` true (auto-block off), `AiBlocksInManual` false, `AttackWindowMs` 400, `AttackRecoveryMs` 300, `InputRepeatGuardMs`
  180, `AimConeDeg` 35, `AimNameTag` true, `OtsDistance` 23, `OtsHeight` 18, `OtsSide` 3, `OtsSensitivity` 1, first-person `FOV` 90, `NearClip`
  1.0. Keys: V toggle, F first person, MButton lock, LButton attack, RButton block, E interact, R stance, Q weapon slot, F1/F2 companions, L
  direction lock, G take control, H shoulder.
- Directions (README): mouse up the downward cut, right and left the side cuts (a block's side follows the same mouse direction), down a thrust;
  a valid held block of the blow's direction always blocks; no player dodge (removed in its v116); no perfect-block window or reward.
- WASD walks by default, Shift runs, Caps Lock locks the run (README). In V mode the swing goes through the game's own attack state; the game
  decides reach, hits, blocks and XP.
- Technique directions: **Verified** (the editor's `CutDirection` enum, `ilspycmd` of `forgotten construction set.exe`): 0 CUT_DEFAULT,
  1 CUT_DOWNWARD, 2 CUT_LEFT, 3 CUT_RIGHT, 4 CUT_THRUST, 5 CUT_UPWARDS, 6 CUT_PIERCED, 7-9 the rear ones. **Observed** (README v56): a swing's
  side follows the blade (mouse right, blade right to left, CUT_LEFT) and the block's side is turned round (mouse right, the right block).

## Port choices

- The squad keys are Ctrl+F1 / Ctrl+F2 (F1 is the game's help and F2 its speed key; a mod action cannot take them), the direction lock Ctrl+L
  (L is the game's Rest). The run lock has no default (Caps Lock is not a key Meitou's input knows). R also reaches the game's Stop key.
- Q switches by hiding the back weapon from the fighter (the hip one is then in hand); new gear makes the fighter again on the primary slot.
- The arrows are text glyphs at 60 px, not the fork's textures at 258 px.
- The core's choices apply (Direct Control README "Port choices").

## Harmony

As Direct Control's (same core), plus `Meitou.Game.Host.GameLoop.PointerDown` and `GameLoop.PointerUp` (prefixes): while the view is held the
mouse buttons go to the control tick only, not to the world. `TechniqueChooser.ChooseAttack` (postfix) picks the drawn direction. Reflection
sets `Fighter.BackWeapon` (Q) and calls `CombatSystem.MeleeDrawn` (R). Engine API: combat hit feed (hit marker), HUD screen overrides.

## Not ported yet

The fork's interaction prompt text (icon + E + name, the 30-unit pick range): the game's own cursor at the crosshair stands in; the 8-part
health panel (4 bars shown); the character-centred sound listener.

## Missing

- The core's Missing list (Direct Control README).
- **Unknown**: not checked in game.
