# Weapon Dodge & Guard (port)

Port of *Weapon Dodge & Guard* v1.2.0-rc1 (Tata-Gari, workshop 3767056805; GPLv3, [source](https://github.com/Tata-Gari/RE_Kenshi_Plugins_Source)).
An `ICombatDefenceFilter` on the engine's defence choice (the original hooks `CharStats::chooseBlock`) lets an armed fighter dodge before it
guards. Deterministic (`Rng.Hash` of the defender's key and the tick). No Harmony patches or reflection.

## Facts

- **Gate** (in order): Mode 1; armed; the vanilla pick is a standing block (a dodge pick is kept, none is kept); posture normal with
  `RequireNormalProneState`; not in defensive mode unless `AllowInDefensiveMode`; past `DodgeCooldownMs`; with `AdaptivePriority`, effective
  dodge (`getDodge(true)`) not below effective melee defence (`getMeleeDefence(true)`). **Verified** (original DLL (decompiled)).
- **Chance**: the vanilla dodge chance (`calculateDodgeChance`) held to [0, 100], times `ChanceScale`, held to [`MinimumChance`,
  `MaximumChance`]; dodge when 100 x a uniform draw is under it, else the guard stands. **Verified** (as above). Meitou: the engine's unarmed dodge
  chance (`DefenceFormulas.EffectiveDefence` of martial arts, then `BlockChance` 0 to 95) and its effective defence as `getDodge(true)`.
- **Technique**: VanillaOnly (0) the dodge back; Compatible (1) a draw weighted on `chance` over every standing dodge technique (dodge, not
  stumble or prone) whose skill range holds the effective dodge, not a taunt or battle cry, not in `BlockedAnimations`, in `AllowedAnimations`
  when it has names; else the dodge back; CustomAllowList (2) only the allowed. **Verified** (as above). Names are compared without case or
  spaces (Meitou's reading of the DLL's canonical names).
- **Defaults** (shipped `WeaponDodgeGuard.ini`): Mode 1, ChanceScale 1.0, MinimumChance 0, MaximumChance 95, AdaptivePriority 0,
  AnimationMode 1, AllowInDefensiveMode 1, RequireNormalProneState 1, DodgeCooldownMs 0. **Verified**. The allow and block lists are text
  settings.
- The filter answers the game's pick unchanged unless it is a standing block it turns into a dodge; techniques are `CombatSystem.Techniques`. The
  chance uses the database's `CombatConstants`, the ones the combat system is built with.
- XP: the engine's own dodge and block XP apply (the original grants dodge XP on a weapon dodge and defence XP on a guard). **Observed**
  (description).
- **Logging** (shipped ini: Verbose 0, LogSelections 0, LogDecisions 0, LogDiscoveredTechniques 0, LogSelectedAnimation 0, MaxLines 500):
  to the mod log; Verbose the settings at start and the picks turned away before the fighter is looked at, LogSelections each decision past
  that (who, the opponent, the decision name, the original and selected technique, effective dodge, chance and roll), LogDecisions Adaptive
  priority's comparison, LogDiscoveredTechniques the standing dodges once a game, LogSelectedAnimation each dodge taken; at most MaxLines lines a
  session, then one saying so. **Observed** (DLL strings: the keys, decision names and field names); which option gates which line is Meitou's
  reading. Off, each check is one read.
- **Cooldown** (`DodgeCooldownMs`): game time, per defender, in the state hash; not saved (the original's runs on the wall clock and is not
  saved either), so a load starts every fighter off cooldown.
- **Unknown**: not run in game.

## Missing

- Gear bonuses on the attacker's skill and the defender's dodge (the engine's are internal; engine gap).
