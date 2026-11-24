# Attack & Block Speed Uncapped (port)

Port of *Attack & Block Speed Uncapped* (攻速解锁, Workshop 3798599010), from its DLL (`SpeedCapTuner.dll`, Ghidra decompile) and description.

- **Uncapped rates** (`ICombatSpeedFilter`): a swing or block whose play rate reached the game's cap may play faster, up to the chosen cap
  (attack 1.2, 2.4, 4.8, 9.6 or unlimited; block 4, 8, 12 or unlimited). The rate is worked out again from the fighter's stats, so a weak fighter
  stays vanilla.
- **Cards** (HUD overlay): four cards at the top centre: attack cap, block cap, language (English / 中文) and the toggle key; a click cycles
  the first two or the language, or waits for a new toggle key (Esc cancels). The toggle key (Ctrl+J, `AddKeyAction`) shows and hides them.
- **Settings**: the same values under Mod settings, kept across saves and restarts.

## Facts (Verified: decompile of `SpeedCapTuner.dll`, unless marked)

- Hook on `CharStats::calculateAttackOrBlockSpeed(float, float, bool)`. When the game's rate is at least 1.199 and the chosen cap above 1.201,
  the rate is recalculated as (0.00228 dex + 0.00152 skill + 0.765) x the call's first float x CharStats `+0x1c` (1 when not above 0.001); the
  result is min(max(game rate, recalculated), cap), at least 0. Attacks and blocks use the same formula. `dex` is `getStat(dexterity, false)`.
- **Observed** (reading): the first float is the weapon-weight term of the game's rate and `+0x1c` the gear's combat speed factor (Meitou:
  0.5 + 0.5 min(1, encumbrance + 0.5) (1 - f) and `Fighter.CombatSpeed`); the skill is the attack rating for an attack, the defence rating for
  a block (`CombatSystem.AttackSkill` / `MeleeDefence`); `dex` is the stored level times the stats' scale. Meitou's rate carries the swimming
  factor (x 0.8), which the port takes off before the test and puts back after.
- Because the recalculated rate is an attack rate (about 1.2 to 1.6 for strong fighters), a block rate over its cap of 4 rarely changes.
- Tiers: attack 1.2, 2.4, 4.8, 9.6, 99 ("UNLIMITED"); block 4, 8, 12, 99. Ini `[SpeedCapTuner]` AttackCap 4.8, BlockCap 4.0, Lang 0 (Chinese), Hidden
  0; `[Hotkeys]` TogglePanel J. Cards 300 x 36 with a gap of 4, times min(w / 1920, h / 1080) held to [1, 3], from 0.4 % of the height, centred,
  skin `Kenshi_FloatingPanelThinSkin`; card texts as the DLL's in both languages.
- The original warns when Desolation System is loaded (both hook the same function).
- **Unknown**: not run in game.

## Port choices

- The card language defaults to the game's (Chinese in a `zh` locale, else English); the original starts in Chinese. Chinese captions use the
  game's `TTDaHeiJ.ttf` as a fallback font when the install has it.
- The toggle key defaults to Ctrl+J: the game's J is Rescue, which a mod's default does not take.
- The caps are copied from the settings once a tick (serial `Inputs`) and enter the state hash.

## Missing

- Nothing. No Harmony patches.
