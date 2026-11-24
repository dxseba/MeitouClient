# Guard Break & Bash (port)

Port of *Guard Break & Bash* v1.0.0 (Tata-Gari, Workshop 3775510993; GPLv3, [source](https://github.com/Tata-Gari/RE_Kenshi_Plugins_Source)),
from its DLL (Ghidra decompile) and shipped `GuardBreakBash.ini`.

- **Bash**: when a melee blow is blocked, the blocker's force is rolled against the attacker's resistance; on success the attacker stumbles.
- **Guard Break**: on the same block, independently, the attacker's force against the blocker's resistance; on success the blocker stumbles.
- **Settings**: every ini key under Mod settings (presets, curves, references and weights, cooldowns, eligibility, logging).

## Facts (Verified: decompile of `GuardBreakBash.dll` and the shipped ini, unless marked)

- Hooks `CombatClass::_blockHit` (the block) and `Character::_startStumble` (to keep each real stagger as a model). On a block: Bash first, then
  Guard Break unless AllowBothEffects is 0 and Bash was applied.
- Score: ReferenceMode 0 max(STR, DEX, TGH), 1 max(STR, DEX), 2 weighted average (weights under 0 count 0; 0 when they sum to 0.0001 or
  less). ValueMode 1 current stats (gear, injuries), 0 base. Bash: blocker force (mode 0) vs attacker resistance (0); Guard Break: attacker
  force (1) vs blocker resistance (0).
- Chance by d = source - target: from BaseChance at 0 linearly to the mid chance at the mid difference, then to MinimumChance (d < 0) or
  MaximumChance (d > 0) at the cap difference, flat past it; held to [minimum, maximum]. Presets 1 Recommended (base 17.5, min 2.5, max 47.5,
  -30: 7.5, -60 cap, +30: 35, +60 cap), 2 Growth (mid chances 10 and 32.5, same differences), 3 Vanilla-shaped (mid chances 10 and 32.5 at 15,
  caps at 30); 0 the ini's own curve. Success when `rand()` x 100 / 32768 is under the chance.
- A stagger needs a target that is alive, not down and (IgnoreAlreadyStumbling 1) not stumbling; past the effect's CooldownMs (1500) on it;
  and a model: its own last real stagger, else anyone's, no older than TemplateMaxAgeMs (10000) and seen MinimumTemplateObservations (1)
  times, with a body part. The original then replays `startStumble` with the model's damages and direction, after setting the combat state to
  stumble with `calculateStumbleBlockTimer(max(model damage x ForceMultiplier, MinimumForce))`.
- Meitou: the block is `CombatSystem.ApplyBlocked`; the stagger is the engine's own (`CombatSystem.Stagger`, as a landed blow's), heavy when
  the model's stagger damage (cut + blunt + pierce + extra stun) passes the target's stumble threshold; the model comes from the hit reaction
  filter (`IBlowReactionFilter`, every landed blow that staggers); the body part is the target's last hit part (none: skipped, as the
  original's null body part).
- **Unknown**: not run in game.

## Harmony

- Harmony: `Meitou.Simulation.Combat.CombatSystem.ApplyBlocked` (postfix: notes the block).
- Harmony: `Meitou.Simulation.Combat.CombatSystem.ApplyBlow` (postfix: rolls the noted block once the blow's own steps, the defence observers
  included, are done, so a stagger does not change what they read).
- Reflection: `CombatSystem.Stagger`, `CombatSystem._write` (the slots Commit writes), `CombatSystem.Eff`. A missing member leaves the mod off,
  logged.

## Port choices

- Rolls use the mod's `Rng` purpose (the original's `rand()`); cooldowns and model ages are game time (the original's the wall clock); not saved,
  as the original's.
- ForceMultiplier and MinimumForce only reach the log: Meitou's stagger lasts its clip, it has no stumble timer to set.
- Logging: Log rolls, Log stagger and Max log lines (to the mod log); the ini's LogBlocks and LogNativeTemplates have no counterpart.

## Missing

- A stagger of a blocked blow is not in the mod API (hit reactions are asked only for blows that land), hence the Harmony patches.
