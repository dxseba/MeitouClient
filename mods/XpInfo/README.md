# XP Info (partial port)

Port of *XP Info - experience gain notifications* (workshop 3795213616, GPLv3), rebuilt from its Workshop description.

- **Gains**: every 10th tick the player characters' stored stats are compared with the last poll; a rise is summed per stat until it reaches
  *Smallest gain* (1% of a level), then shown as `+N% Skill` (a stat's value is its level, so +0.03 is 3%); a character's gains of one poll are
  merged into one label.
- **Levels**: a new whole level shows `Skill N` in gold, a little above the gains.
- **Drawing**: as Level Up Notify, a postfix on `PlayerInterface.BindUi` projects each label over its character (`PlayerInterface.Project`),
  rising for *Seconds* (2.5); a first sighting (spawn, load, recruit) only records.
- **Panel XP bars** (`AddCharacterPanelLines`): under the stats in the HUD's character panel, a player character's skills that gained XP most
  recently since the mod first saw it (*Panel XP bars*, 0 to 2, default 2), each as `Skill  level  N%` over a bar of its progress to the next
  level. Only a few lines fit (four across all mods, fewer in the panel's room), so the most recent gains are the ones shown: they are what
  the character is training now. Before a first gain, and for other factions' characters, there are none.
- **Missing** (partial): XP bars for more than two skills (the panel has room for a few lines only), the ini and its verbose log.
  With Level Up Notify also on, a level shows twice.
- **Unknown**: not checked in game, the panel bars included (how many rows fit beside the game's stats, their look).
