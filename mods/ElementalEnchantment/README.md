# Elemental Enchantment (partial port)

Port of *Elemental Enchantment* (workshop 3832978749), rebuilt from its Workshop description.

- **Elements**: Fire, Lightning, Wind, Water and Ice, each a mod skill in the stats window's Combat column (`AddSkill`, saved with the
  character's stats). **Element: X (level)** on your own character's menu cycles its active element (none, then the five); the choice is kept by
  lasting character id in the mod sidecar.
- **Effects** (`ICombatHitFilter`, melee and bolts; this port's shares, the original's are not published), at strength s = level / 100 x
  *Effect strength*: Fire +30% s of the blow as blunt; Water +30% s as cut; Ice +15% s as cut and bleeding x (1 + 0.5 s); Lightning +0.3 s
  armour penetration; Wind none (its attack, block and reload speed have no hook). From level 50 the character's best other element joins at
  25% of its own strength, rising by 10% every ten levels to 75% at 100 (the original's milestones).
- **Training**: every landed hit with an element active trains that skill by 2 x *Elemental XP* x the character's aptitude for it (a fixed
  0.5-1.5 roll of its lasting id; 1 with *All aptitudes at 100%*), on the game's curve.
- **Missing** (partial): the ENCHANTMENT window and choosing the secondary element (the best other one is taken), weapon trails and impact
  effects, Wind's speed and Lightning's attack speed, Elemental Haven (the town, recalibrators, cores, wards, resonance), bosses using elements.
- **Unknown**: not checked in game.
