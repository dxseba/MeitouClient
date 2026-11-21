# XP Info (partial port)

Port of *XP Info - experience gain notifications* (workshop 3795213616, GPLv3), rebuilt from its Workshop description.

- **Gains**: every 10th tick the player characters' stored stats are compared with the last poll; a rise is summed per stat until it reaches
  *Smallest gain* (1% of a level), then shown as `+N% Skill` (a stat's value is its level, so +0.03 is 3%); a character's gains of one poll are
  merged into one label.
- **Levels**: a new whole level shows `Skill N` in gold, a little above the gains.
- **Drawing**: as Level Up Notify, a postfix on `PlayerInterface.BindUi` projects each label over its character (`PlayerInterface.Project`),
  rising for *Seconds* (2.5); a first sighting (spawn, load, recruit) only records.
- **Missing** (partial): the live XP bars under the stats in the character panel (the panel has no mod slot), the ini and its verbose log.
  With Level Up Notify also on, a level shows twice.
- **Unknown**: not checked in game.
