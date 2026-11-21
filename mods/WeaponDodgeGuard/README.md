# Weapon Dodge & Guard (partial port)

Port of *Weapon Dodge & Guard* (Tata-Gari, workshop 3767056805; GPLv3, [source](https://github.com/Tata-Gari/RE_Kenshi_Plugins_Source)),
rebuilt from its Workshop description.

- **Dodge, then guard**: a Harmony postfix on the engine's defence choice (`CombatSystem.ChooseDefence`). A defender whose weapon can block (the
  engine gives such fighters no dodge roll) first rolls the unarmed dodge chance: `DefenceFormulas.EffectiveDefence` of its martial arts,
  encumbrance, leg factor, defence bonus and guard, then `BlockChance` against the attacker's skill in its weapon (0-95). On success the pick is a
  dodge technique of the weapon's category (`TechniqueChooser.ChooseReaction`, dodge pool), else with *Dodge back fallback* the unarmed dodges
  (the vanilla dodge back); on failure the engine's own guard stands. Deterministic (`Rng.Hash` of the blow's key and tick).
- **Adaptive priority** (off, as the original's default): on, the dodge is tried only while its chance is at least the guard chance.
- **Missing** (partial): the original's animation modes and allow/block lists (any dodge technique of the category counts); its own XP grants
  (the engine's dodge and block XP apply as they would); the attack and martial-arts figures leave out gear bonuses (the engine's are internal).
- **Unknown**: not checked in game.
