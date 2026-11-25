# Direct Control

Port of *Direct Control (Re_Kenshi)* 1.5.1 (smokefoolius, workshop 3737240806, [source](https://github.com/smokefoolius/Kenshi-Direct-Control);
GPLv3), from its DLL (`WASDCombatPlugin.dll`, Ghidra decompile), `WASDCombatPlugin.ini`, `README.txt` and Workshop description. The shared code
in `Core/` is also compiled into the [Action RPG](../ActionRpg/README.md) port.

- **V** drives the selected character: WASD (the game's camera move keys) walk it relative to the view, overriding click-to-move, job walks and
  the fight footwork while held, with a crisp stop on release; the camera follows it. Selecting another player character (a click, a portrait)
  hands control to it; **G** takes the player character under the cursor. Direct control switches itself off with a notice when its character is
  knocked out or dies, and on a new game or load.
- **X** cycles the character's walk / jog / run speed (the game's own speed setting).
- **O** first person at the character's eyes with mouse look (sensitivity, field of view 50-110, clipping distance, eye height and forward);
  the body turns to the view while standing; your own head, hair and headgear are hidden. **U** the third-person camera over the shoulder with
  a centred crosshair and wheel zoom, easing in clear of walls and above the ground; **N** swaps the shoulder. The pointer is held at the
  middle of the window for the mouse look (also on the Metal host) and freed while a window that needs the mouse is open or Alt is held; held
  there, the game's own hover cursor (attack, talk, loot, doors, lockpicking) shows at the crosshair and a click acts there. **Shift+C** toggles
  sneak. In first person the body and clothes draw two-sided (solid from the inside), buildings show every storey, and the grass range is
  scaled by the original's grass range multiplier (1.0 by default; with the clipping distance, its cure for the turning shimmer).
- Opening the character's own inventory turns the camera to face it (not in combat; setting); WASD waits meanwhile. WASD on a character that
  sits, lies in a bed or works at something gives it a move order a step ahead (the game's get-up), then the direct step takes over.
- **Z** manual combat: the character swings only on **E** (Attack, at the enemy you point at: under the cursor in the game's camera, nearest the
  crosshair within the cone in first and third person; out of reach it is ordered to fight it and steps in), holds a guard on **F** (the game's
  Block order, its defence bonus) for at most the longest-hold time, and a tap of **F** stops hits from the front within the perfect-block window
  whatever the skill (a later blow of the same attack too), then lights the weapon blue and lets the next press skip the cooldown. **Q** is a
  timed dodge: a roll of the dodge skill plus the bonus against the attacker, a dodge technique for the blow's side. Without a press the
  character blocks and dodges on its own only as the two settings allow. No swing starts while WASD is held. The target's name (red) and blood,
  head, chest and stomach bars show over it; an enemy swinging near you glints at its weapon.
- Every setting of the original's ini except the advanced camera tuning is on the Mods page (Keys, Movement, Manual combat, Third person,
  First person). All keys are on the Controls page; the Attack, Block and Dodge keys are settings (key names), read only while manual combat is on.

## Facts (Verified: decompile of `WASDCombatPlugin.dll` and the shipped ini, unless marked)

- Ini keys and defaults as in the settings: `WasdSpeedCap` true, `WasdSpeedMult` 1.0, `AiBlocksInManual` / `AiDodgesInManual` false,
  `BlockHoldMs` 1200, `BlockCooldownMs` 800, `PerfectBlockWindowMs` 1000, `PerfectBlockRewardMs` 1200, `DodgeWindowMs` 600, `DodgeSkillBonus` 20,
  `DodgeCooldownMs` 300, `AttackWindowMs` 400, `AttackApproachMs` 1500, `AttackRecoveryMs` / `DodgeRecoveryMs` 400, `InputRepeatGuardMs` 250,
  `AimConeDeg` 35, `AimNameTag` / `WeaponGlint` true, `GlintLeadMs` 700, `OtsDistance` 20, `OtsHeight` 15, `OtsSide` 4, first-person
  `Sensitivity` 1, `FOV` 75, `NearClip` 1.0, `EyeDrop` -0.2, `GrassRangeMult` 1.0, `BodySolidInside` 1, `FloorRevealBelow` 1,
  `InventoryFaceCam` true. Keys V W A S D X G P C(+Shift) H Z E F Q.
- Speed cycle: the orders panel's `speedNext`, then the movement's desired speed set to the panel's mode (0 walk, 1 jog, 2 run).
- Block hold: the game's block order is set while held and restored on release (`block_hold vanilla_order`, `block_release ... restored`).
- An attack press out of reach lets the AI close in for that swing (`attack_approach`); one with no hostile in range is ignored.
- **Observed** (README.txt): a perfect block covers only hits from the front, dodges every side; a stagger plays through.
- **Unknown**: the exact aim and dodge formulas inside the DLL (the port uses the game's own block-chance formula for the dodge roll).

## Port choices

- Keys the game holds get another default: first person **O** (P is photo mode), the third-person camera **U** (Ctrl is the camera turn and is
  refused as a mod default), swap shoulder **N** (H is the medic alias). All rebindable.
- The step is a one-point path kept 40 units ahead every tick, walked by the movement system (its speed, acceleration, turning, wall slides).
- Meitou decides a defence when the swing is 10 % along, so a tap counts for hits whose defence is decided within the window after it.
- The glint lights when an enemy fighting the player near you starts a swing (`Attacking`), for the glint time; not tied to the blow's timing.
- Head hide uses Meitou's headless look (as decapitation draws), not the original's shader; its `materials/` are not read. The two-sided body
  is the character renderer's own `DoubleSided` material flag, set only on that first-person copy's materials.
- "Show floors below you": every building in the interior view shows all its floors in first person (Meitou hides the floors above the viewed
  one; the original reveals the ones below), and no character indoors is hidden for its floor.
- Wall avoidance uses the world's sight test (`IWalkability.LineOfSight`) from the shoulder pivot, halved eight times, and the strategy camera's
  ground-and-floor height; read on the host thread between ticks.
- Not saved, as in the original.

## Harmony

- `Meitou.Game.Host.Cameras.CameraRig.Update` (prefix): the control tick's keys, mouse look, steering; the move (and turn) keys taken out.
- `Meitou.Game.Scenarios.SandboxScenario.FollowCamera` (postfix): the first-person and third-person camera.
- `Meitou.Game.Host.GameLoop.OnMouseMove` and `Meitou.Game.Host.MetalGameLoop.OnMouseMove` (prefixes): the pointer held at the window's middle,
  its movement the mouse look, the interface's hover moved there.
- `Meitou.Game.Host.WorldSync.InteriorView.UpdateInteriorView` (postfix): every floor in first person.
- `Meitou.Game.Rendering.Characters.CharacterContent.BodyMaterial` and `.ItemMaterial` (postfixes): two-sided materials of the first-person copy.
- `Meitou.Game.GameHost.Drawn` (postfix) and `Meitou.Game.Effects.Decapitation.CutHead` (prefix): the hidden head.
- `Meitou.Simulation.Combat.CombatSystem.StartAttack` (prefix, postfix): the manual swing gate.
- `Meitou.Simulation.Combat.TechniqueChooser.ChooseAttack` (postfix): directions (Action RPG only; passes through here).
- Reflection on `GameHost` (`Session`, `Player`, `Camera`, `Gpu`, `DrawnPosition`, `HoldPointer`, `Covered`), `PlayerInterface` (`Follow`,
  `StealthView.DrawnHead`, `ModalOpen`, `UiCapturesPointer`, `MouseMove`, `MouseDown`, `MouseUp`), `WorldGpu.Foliage.GrassRangeSetting`,
  the loops' `_window`, `_lastMouse` and `Pixels`, `InteriorView.InteriorFloors` / `HiddenIndoors`, `ModFrame._host`, `ActionState.Set` and `CharacterAppearance.Headless`.
- Engine API: a tick system before Movement, `ICombatDefenceFilter`, key actions, settings, frame, overlay, `Glow`.

## Not ported yet

The hand-to-wound pose suppression, the stair eye lift and the look smoothing and per-gait forward pushes (`[FirstPerson]` advanced values).

## Missing

- The Metal host's mouse buttons are not taken from the world in the Action RPG scheme: its button handlers are lambdas (compiler-named
  closures), not patchable by name.
- **Unknown**: not checked in game.
