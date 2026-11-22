# Weapon Dodge & Guard (port)

Port of *Weapon Dodge & Guard* v1.2.0-rc1 (Tata-Gari, workshop 3767056805; GPLv3, [source](https://github.com/Tata-Gari/RE_Kenshi_Plugins_Source)).
A Harmony postfix on the engine's defence choice (`CombatSystem.ChooseDefence`, the original hooks `CharStats::chooseBlock`) lets an armed
fighter dodge before it guards. Deterministic (`Rng.Hash` of the blow's key and tick).

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
  AnimationMode 1, AllowInDefensiveMode 1, RequireNormalProneState 1, DodgeCooldownMs 0. **Verified**. The allow and block lists are read from a
  `WeaponDodgeGuard.ini` in the mod's folder (text is not a mod setting).
- XP: the engine's own dodge and block XP apply (the original grants dodge XP on a weapon dodge and defence XP on a guard). **Observed**
  (description).
- **Unknown**: not run in game.

## Missing

- The cooldown runs on game time and is neither saved nor hashed (the original's runs on the wall clock); off by default.
- Gear bonuses on the attacker's skill and the defender's martial arts (the engine's are internal).
- The logging options (`Verbose`, `LogSelections`, `LogDecisions`, `LogDiscoveredTechniques`, `LogSelectedAnimation`, `MaxLines`).
