# Elemental Enchantment (partial port)

Port of *Elemental Enchantment* (workshop 3832978749), rebuilt from its Workshop description.

- **Elements**: Fire, Lightning, Wind, Water and Ice, each a mod skill in the stats window's Combat column (`AddSkill`, saved with the
  character's stats). **Element: X (level)** on your own character's menu cycles its active element (none, then the five); the choice is kept by
  lasting character id in the mod sidecar.
- **Effects** (`ICombatHitFilter`, melee and bolts; this port's shares, the original's are not published), at strength s = level / 100 x
  *Effect strength*: Fire +30% s of the blow as blunt; Water +30% s as cut; Ice +15% s as cut and bleeding x (1 + 0.5 s); Lightning +0.3 s
  armour penetration. From level 50 the character's best other element joins at 25% of its own strength, rising by 10% every ten levels to
  75% at 100 (the original's milestones).
- **Speed** (`ICombatSpeedFilter`, the same strengths and shares; this port's factors): Wind multiplies attack and block play rates by
  (1 + 0.2 s), Lightning the attack rate by (1 + 0.1 s), past the game's caps. A pure read of the attacker's skills and its element.
- **Looks** (`AddCombatHitFeed` + `ModContext.PlayEffect`, *Element effects* setting): each hit by a character with an element active plays
  the element's impact on the victim's chest for 0.3 s and the parry sparks in the element's colour at the attacker's weapon bone
  (`Bip01 Prop2`, where the game throws its block sparks) for 0.25 s. Impacts: Fire `small- fire2_torch`, Lightning `small- sparks1`, Ice
  `small- smoke3_small` (EFFECT records, ids in `ElementEffects.cs`), Wind the system `Dustkick01`, Water `Footsplashes01`, each tinted.
  **Verified** with `meitou-tools record` and `particles effects` that the three EFFECT records are base-game (`gamedata.base`) with those
  systems, and that both systems have scripts in the base `data/particles/scripts`. **Unknown**: how they look and read at combat scale.
- **Training**: every landed hit with an element active trains that skill by 2 x *Elemental XP* x the character's aptitude for it (a fixed
  0.5-1.5 roll of its lasting id; 1 with *All aptitudes at 100%*), on the game's curve.
- **Missing** (partial): the ENCHANTMENT window and choosing the secondary element (the best other one is taken), a true swing trail (sparks
  at the weapon on a hit instead), Wind's reload speed, Elemental Haven (the town, recalibrators, cores, wards, resonance), bosses using elements.
- **Unknown**: not checked in game.
