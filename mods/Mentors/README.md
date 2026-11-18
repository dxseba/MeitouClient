# Mentors (partial port)

Port of *KENSHI: MENTORS* (workshop 3834394158), rebuilt from its Workshop description: no source is published.

- **Learn from** (`UiRegistration.AddCharacterMenuVerbs`): a row on a living, conscious, non-hostile, non-player character that is not caged.
  The first selected character walks over (`MoveOrder`) and the training window opens.
- **Window**: the teacher's name and field, the student's cats, and one row per skill the field covers that the teacher knows at
  *Minimum teacher level* (20) or more: the student's stored level, the teacher's, and **+1** / **+5** with their prices. A lesson needs the student
  within *Teaching distance* (80) of the teacher.
- **Limits** (from the description): training stops *Levels below teacher* (5) under the teacher and at *Maximum training level* (60).
- **Price**: our formula (the original's is not published): each level costs `base x (1 + level/10)^2 x (0.75 + teacher/200)`, with
  *Price per level* (40) as the base; attributes cost *Attribute price multiplier* (1.5) times more. Paid from the player's money; the stat is raised
  on the stored value (`CharacterStats.Stored`), so mod modifiers are not baked in.
- **Fields** (`Professions`): matched on words in the teacher's CHARACTER record name (guard, soldier, ninja, thief, farmer, miner, drone, smith,
  doctor, engineer, cook, bartender, shooter, martial, fisher, wanderer...), then a shop (traders: the four attributes, plus a skill by the goods
  word in the name: weapon, armour, crossbow, mechanic, building, book, bar), then NPC class law enforcement (fighter). Others teach nothing.
  *Require profession* off lets any teacher teach every skill.
- **Missing** (partial): the dialogue line "Can you teach me something?" (a menu row instead), the Ctrl+Shift+T shortcut, the Mentors.ini file
  (settings under Mod settings instead) and the original's exact prices and profession table.
- **Unknown**: how well record-name matching finds teachers across factions and mods; not checked in game. **Observed** (2026-11-19): builds.
