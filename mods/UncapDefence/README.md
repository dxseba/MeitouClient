# Uncap Defence Success Rate (port)

Port of *Uncap Defence Success Rate* (解锁防御成功率上限, Workshop 3799167934), from its DLL (`UncapDefenceSuccessRate.dll`, Ghidra decompile)
and description.

- **Chances** (`ICombatDefenceFilter`): the block chance (armed) and dodge chance (unarmed) lose the game's 95 % cap. By the level gap (own melee
  defence or dodge minus the attacker's attack), a stronger defender is lifted towards the ceiling and a weaker one pushed down; equal fighters
  stay vanilla. Every fighter, both ways.
- **Cards** (HUD overlay, top left): mode (Off / Amplify / Threshold), scale (full gap in Threshold), ceiling (95, 99, 99.9, 100 %), toggle key,
  language, and the last block and dodge chance changed ("vanilla → now"). The toggle key (Ctrl+K, `AddKeyAction`) shows and hides them.
- **Settings**: the same values under Mod settings, kept across saves and restarts.

## Facts (Verified: decompile of `UncapDefenceSuccessRate.dll`, unless marked)

- Hooks `CharStats::_calculateBlockChance(float)` and `calculateDodgeChance(float, bool)`; both run one step on the game's result c: gap =
  `getMeleeDefence(true)` (block) or `getDodge(true)` (dodge) minus the attacker's attack; pressure p = 1 - e^(-|gap| / scale) (Amplify) or
  min(|gap| / full gap, 1) (Threshold); gap > 0: c + (ceiling - c) p, gap < 0: c (1 - p); then at most the ceiling, at least the floor, within
  [0, 1]. Off returns the game's chance.
- Tiers: scale 15, 25, 40, 60; full gap 15, 30, 50, 80; ceiling 0.95, 0.99, 0.999, 1. Ini `[UncapDefenceSuccessRate]` Mode 1, Scale 25,
  Threshold 30, Ceiling 0.99, Floor 0.01 (held to [0, 0.5]), Lang 0 (Chinese), Hidden 0; `[Hotkeys]` TogglePanel empty, which reads as K.
- Cards 340 x 36 with a gap of 4, times min(w / 1920, h / 1080) held to [1, 3], at 0.6 % of the width and 0.4 % of the height; card texts as the
  DLL's in both languages. A click on the scale card in Threshold mode cycles the full gap (**Observed**: the caption shows it; the click
  handler was not traced).
- Meitou: the game's chance is `DefenceChoiceAsk.BlockChance` / `DodgeChance`; the port picks the defence again with the game's chooser
  (`TechniqueChooser.ChooseReaction`) and the game's own chance and pick rolls (`CombatRolls`, as `CombatSystem.ChooseDefence` draws them),
  so the change acts exactly through the chance. A stumbling defender's break-out dodge and a pick another filter changed are kept.
- **Unknown**: not run in game.

## Port choices

- The original treats a chance of at most 1.5 as a fraction; Meitou's chances are always percent, so the port always does.
- The toggle key defaults to Ctrl+K: the game's K is Put Down, which a mod's default does not take.
- The card language defaults to the game's (Chinese in a `zh` locale); the original starts in Chinese. The readout shows "N%(was M%)".

## Missing

- Nothing. No Harmony patches.
