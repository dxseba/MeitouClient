# Nitoryu: Dual Wielding (port)

Port of *Nitoryu: Dual Wielding* (enths0, workshop 3812165089, GPL-3.0), from its plugin (`Nitoryu.dll`, Ghidra decompile), its data
(`Nitoryu.mod`), `Nitoryu.ini`, `FCS.md` and `README.md`. The original is a required item: enabled, its `.mod` (38 moves as COMBAT_TECHNIQUE
records, about 135 `nitoryu ...` settings on GLOBAL CONSTANTS, the Pair Training Dummies, the NPC lists) loads as data; the port reads its
`Nitoryu.ini` and its two animation skeletons from the install at start.

- **Pairs**: a fighter with a weapon on the back and one on the hip, both allowed (`nitoryu weapon categories`, katanas and sabres as
  shipped; WEAPON `nitoryu dual wield`; the two vendor lists; `[Weapons] allow / refuse`; never heavy weapons, polearms or bows) and the second
  with a `bare sword` mesh, both arms working, draws both: the one in hand is the main blade, the other the second blade.
- **Dual Wielding** (`AddSkill`, id `nitoryu.ambidexterity`, Weapons column): seeded the first time a character is seen at a quarter of its best
  katana or sabre skill (or the NPC rules' level, or the original's saved `NIT_ambidexterity`); trains by half the weapon experience of a dual
  fighter's fights. Heavy pieces worn take 7 %, medium 4 % (floor 0.3), overload lowers it (floor 0.2), as it counts in a fight.
- **Path and ranks**: Path = 60 % Dual Wielding (in the fight) + 10 % each of the lower weapon skill, Dexterity, melee attack and melee defence;
  Deshi 20, Bushi 40, Ronin 60 (Dual Wielding 40), Kenshi 75 (60), Kensei 90 (80). A message on a player character's rank-up.
- **Moves** (the attack-choice filter): from rank 1 the second blade takes the off-hand share of attacks (20 % at Path 20 to 55 % at 100) with
  the pool's second-blade strikes; from rank 3 twin strikes (25 % to 45 % of those, x1.3 for equal blades) on a 5 s double-hit cooldown
  (Kensei: 3.5 to 2 s), ripostes within 1.5 s of a block (30 % to 60 %), the X cut on a staggering enemy or after a block, the openers (Dash
  slash, Spin) from their gap windows; strikes that step in (big chop) for an enemy out of reach on their own cooldowns; with only my strikes,
  main-blade strikes of the pool at 60 % of the rest. Moves need Path at least their `min skill`, never repeat back to back.
- **The blow's weapon**: an impact with `limb 0` (left arm) does the second weapon's own damage, bleeding, penetration and race multipliers,
  times the off-hand damage curve (x1.04 at Path 20 to x1.30 from 70) and the WEAPON's `nitoryu off hand damage`; its experience goes to the
  second weapon's skill. Every dual blow's cut counts Dexterity +12 %.
- **Second-blade parry** (rank 2+): a blow the game's pick does not meet may be parried (20 % at Path 40 to 55 % at 100, x1.3 long + short), from
  the front and flanks (110 degrees), from behind at half from rank 3; the faced enemy's blows at half from rank 3, a combo's later blows at half;
  once a game second.
- **Speed**: attack and block rates x0.85 at Path 0 to x1 at 30, times 1 + 0.3 x the shares of Dexterity and Path above 30; attack cap 1.5, block 4.
- **Flow** (rank 4+): the third attack at the fighter within 5 s gives 4 s of +12 % more Dexterity in the cut, +0.04 speed and cap 1.8; 5 s
  before it counts again.
- **Learning is hard**: -10 melee attack and defence at Path 0, gone at 40; the split guard's -4 / -4 / -2 defence at ranks 0 to 2.
- **The second blade held**: drawn in the left hand (its sheath stays worn, empty), display only.
- **Pressure** (rank 3+): every hit of a dual fighter an enemy blocks piles a stack on it (up to 5, one fades every 3 s, cleared when it lands a
  hit on that fighter); each stack counts `pressure points` (1) in the initiative roll between the two, against the pressured enemy.
- **The same body part** (rank 3+): the second impact of a twin strike on the same enemy within 1 s goes for the part the first hit (80 % at
  Path 60, always from 75).
- **Two enemies, two blades** (rank 4+): the second blade's impact of a twin or the X cut lands on an enemy who attacked the fighter in the
  last 8 s and stands in front (180 degrees) within 2.5 x the second blade's half length, while the main blade stays on the target.
- **Mugen's chain** (rank 5): a landed twin may pull the next twin within 2.5 s whatever the cooldown (25 % at Path 90, 50 % at 100).
- **Left-hand fighting**: with the right arm crippled or gone, the left working and Dual Wielding 50+, a fighter keeps an allowed blade (one is
  used): 70 % damage, 85 % attack and block speed, -10 defence at 50, fading to nothing at 100; it trains Dual Wielding.
- **Pair Training Dummies** (BUILDING `nitoryu pair dummy`): a character carrying a pair (sheathed is enough) trains Dual Wielding up to the
  dummy's `output rate` (10 / 20 / 30), at the training dummies' rate.
- **Animations** (the original's tracks): its moves' own tracks; the rank stances; the dual draw, the sheathe and the blood flick after a fight
  (back or hip versions, by where the second blade is worn); the run and jog holds (`[Animations] run_stances`, each fighter its own); blocks by
  the blow's side (overhead, angled left / right, scissor X or one-blade) and the second blade's parry; experimental: the game's swing mirrored
  onto the left side for a second-blade strike without its own track (setting, off by default).
- **Marks over the heads** (`[Marks] marks`: 0 none, 1 selected, 2 the player's, 3 everyone): flow with a bar that runs out, a Mugen chain
  waiting, pressure pips (red at full); text marks.
- **The Dual Wielding pane**: in a fight now (with the armour and overload factors), Path and rank, what the next rank needs, the dual speed
  bonus, the fight's Dexterity bonus, what the pair gives, left-hand fighting.
- **Inventory buttons** on the back weapon's cell: the dual stance on / off, and for blunt weapons and hackers the any-weapon pair; both saved
  (`[UI] toggle_button`, `any_weapon_button`). The key action stays too.
- **Add-on API** (`INitoryuApi`, the original's `NitoryuApi.h` as a C# interface): an add-on declares the dependency `dxseba.nitoryu` and gets
  it with `Dependency<INitoryuApi>`: is the character dual, its rank, Path and Dual Wielding; events (second-blade hit, parry, special, rank
  change) raised serially; pair rules (the only way a heavy weapon or polearm pairs). `[Compat] addons=0` turns them off.
- **Toggle dual stance** (`AddKeyAction`, unbound): the selected characters fight with one blade, or with two again; saved with the character.
- **NPCs**: `[NPC] dual` all / authored / off; an NPC fights dual with Dexterity 30, both weapon skills 30 and strength half the pair's weight,
  at most rank 3, or always when opted in (CHARACTER / FACTION `nitoryu dual wielder`, the opt-in squads, `[NPC] opt_in`), with the level and
  Path floor the squads, fields and ini give.
- **Settings** (Mods page): dual wielding on/off, only my strikes, the second blade's parry, flow, the own animations, the experimental mirror, the blade in the left hand,
  the rank-up message. Every balance number is the data's, a `Nitoryu.ini` key uncommented wins.

## Facts (Verified: decompile of `Nitoryu.dll` and the bytes of the Workshop build, unless marked)

- The moves are the `.mod`'s COMBAT_TECHNIQUE records with `nitoryu move` 1 (strike), 2 (twin), 3 (finisher), 4 (opener), all weapon flags off
  and `animal` ATTACK_NULL (21), so the game never picks them (`FUN_180006ec0` reads `nitoryu move`, `arms`, `clip`, `needs clip`, `roll`,
  `cooldown ms`, `min gap`, `max gap`, `timing from`). **Observed** (`FCS.md`): `min skill` is the Path a move needs; `limb N` 0 is the second
  blade; a twin is one right- and one left-arm impact; the higher `chance` opener goes first.
- The tracks live in `character/meshes/{male,female}_skeleton/Nitoryu_L3_{male,female}.skeleton` (67 `nitoryu ...` animations); the DLL loads
  them through `OldSkeletonManager` itself, and no race links its ANIMATION_FILE `28-Nitoryu.mod` (empty file fields, `preprocess` set).
- Every balance number and curve is a `nitoryu ...` field of the `.mod`'s GLOBAL CONSTANTS (values as listed above); `Nitoryu.ini` keys
  override them. **Observed** (README, `Nitoryu.ini`): the ini key names and their meaning.
- Pressure (`FUN_18005ca70`, the `CombatClassAI::whoAttacksYouOrMe` hook): the pressure points between the two fighters are added to the
  game's score before it is asked (log names `pressure_contest_master_won` / `target_won`); the points per stack are the `pressure points`
  curve by Path (1 as shipped).
- `nitoryu split reach`, `split arc deg`, `split attacker ms`, `scissor window ms`, `mugen chain window ms` mean what `FCS-settings.md` says
  (**Observed**): the flanker within reach x the second blade's half length plus both bodies, within the arc in front, an attacker of the last
  8 s; the two blades of a twin count as one within 1 s; a chained twin waits 2.5 s.
- **Observed** (README numbers): the speed bonus fits 0.3 x ((Dex - 30) / 70) x ((Path - 30) / 70), the constants' `speed bonus from` 30
  (Dex 60 + Path 60: x1.06; Dex 100 + Path 60: x1.13; both 100: x1.3).
- **Observed** (`fcs.def`): the skill is the STATS key `NIT_ambidexterity` in the original's saves.
- **Observed** (Meitou `AnimationMask`: `override L arm` moves `Bip01 Prop1`): `Bip01 Prop1` is the left-hand prop bone. **Unknown**: whether a
  blade mesh sits in it as in the right hand's `Bip01 Prop2` (the original mirrors the right hand's grip).
- **Unknown**: the original's exact pick order and pool weights beyond what `FCS.md` and the README say; the overload factor's formula (the
  port uses the encumbrance factor); not checked in game.

## Harmony

- `Meitou.Simulation.Combat.CombatSystem.StartAttack` (prefix, postfix), `Meitou.Simulation.Combat.TechniqueChooser.ChooseAttack` (postfix): the
  attack-choice filter, and the initiative ask's context.
- `Meitou.Simulation.Combat.CombatSystem.HasInitiative` (transpiler: its `CombatSystem.Eff` reads go through the port, adding pressure).
- `Meitou.Simulation.Combat.CombatSystem.LandBlow` (prefix, finalizer) and `Meitou.Simulation.Bodies.MedicalState.Rolled` (prefix): the same
  body part.
- `Meitou.Simulation.Combat.CombatSystem.ApplyBlow` (prefix, calling the method again for the flanker): the left blade onto a flank enemy.
- `Meitou.Simulation.Combat.CombatSystem.FollowArms` (transpiler: `Fighter.SetArms` goes through the port): left-hand fighting.
- `Meitou.Simulation.Production.ProductionSystem.TrainingXp` (postfix): the Pair Training Dummies.
- `Meitou.Simulation.Movement.AnimationSystem.Update` (prefix) and `Meitou.Simulation.Movement.AnimationSystem.PickGait` (postfix): the pose
  layer (moves, blocks, stances, runs); `AnimationSystem.Plays` by reflection.
- `Meitou.Data.Characters.PoseScratch.AddLayer` (prefix): the mirrored clips; `PoseScratch.Sample` and its fields by reflection.
- `Meitou.Game.Host.HiddenHatLooks.WorldLook` (postfix): the second blade in the left hand; `CharacterRenderer.IsPrepared` / `Warm` by reflection.
- `Meitou.Simulation.Bodies.HudTips.StatsWindow` (postfix), `Meitou.Game.Ui.StatsWindow.AddModSkillDescription` (prefix, finalizer) and
  `Meitou.Game.Ui.StatsWindow.AddDescriptionPanels` (prefix): the pane's live lines.
- By reflection, not patched: `CombatSystem.Eff`, `CombatTuning.Gap`, `XpService.ContinuousRate`.
- Registration surface: the data phase, `AddSkill`, `AddSystem` (`ICombatHitFilter`, `ICombatDefenceFilter`, `ICombatSpeedFilter`,
  `IExperienceFilter`, `ICombatDefenceObserver`, `ICombatHitObserver`, `IWorldReady`), `AddCombatMove` (draw, sheathe, flick), stat modifiers,
  character save fields, `AddKeyAction`, `AddSettings`, `AddFrame` and `AddUi` (marks, inventory buttons), `IModDependents`.

## Port choices

- The original mirrors the right arm onto the left frame by frame over any clip; the port mirrors only the second blade's strikes without a track,
  when the setting is on, and plays the original's tracks as authored.
- Cooldowns, flow, pressure and the last block are not saved (they start fresh after a load).
- The draw, sheathe and flick play through the engine's mod moves, which only play while the game plays nothing of its own.

## Missing

- **The buttons' and marks' hover texts** (`[UI] button_tips`, `[Marks] tips`): a mod's screen controls and overlay nodes have no tooltip.
- **Picture marks**: the original's marks are its own drawings; the port draws text marks (a wave `~` with a bar, `(o)`, `*` pips).

## Not yet ported

- The hit-taken and back-guard tracks (the game's own reaction and block play), the jog / run holds' exact speeds (the port starts the jog at 8
  and the run at 20 units a second), the original's `lang/*.ini` texts (English only), the parry, riposte and outnumbered experience bonuses,
  the pair weight in the speed formula, `even_picks`, `foreign_control`.
- **Unknown**: not checked in game; the mirror's axes are a guess (off by default).
