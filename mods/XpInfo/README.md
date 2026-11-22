# XP Info (port)

Port of *XP Info - experience gain notifications* (workshop 3795213616, GPLv3).

- **Gains**: every 10th tick the player characters' stored stats are compared with the last poll (the original hooks `increaseStat`); a rise is
  summed per skill and shown as `+N% Skill` once it reaches *Announce gains over %* and the skill's last announcement is *Announce at most every*
  ago; a character's gains of one poll are merged into one label.
- **Levels**: a new whole level shows `Skill N` in gold, a little above the gains, at once and in place of that skill's gain.
- **Drawing**: as Level Up Notify, a postfix on `PlayerInterface.BindUi` projects each label over its character (`PlayerInterface.Project`),
  rising for *Seconds* (2.5, Meitou's); a first sighting (spawn, load, recruit) only records.
- **Panel XP bars** (`AddCharacterPanelLines`): under the stats in the HUD's character panel, up to *Most bars at once* of the skills that gained
  XP within *Keep a bar for* seconds, the most recent first, each `Skill  level  N%` over a bar of its progress to the next level.

## Facts

- **Settings** (`xp_info.ini`, labels from its settings window): floatingLabels 1 "XP gain text", levelUpLabels 1 "Level-up text", panelBars 1
  "XP bars in panel", maxRows 2 "Most bars at once", minPercent 2.00 "Announce gains over %", announceEverySeconds 1.0 "Announce at most every
  (s)", recentSeconds 120 "Keep a bar for (s)", verboseLog 0. **Verified** (original ini; DLL strings).
- **Texts**: gain `+%.0f%% %s` (whole per cent), level `%s %d`. An entry announces when it has a level, or when its gain is at least minPercent
  and announceEverySeconds have passed since its last announcement; a level replaces the gain text. **Verified** (original DLL (decompiled)).
  Meitou runs the intervals on game time (the original's on the wall clock).
- Nothing is saved. **Observed** (description).
- **Unknown**: not checked in game, the panel bars included (how many rows fit beside the game's stats, their look).

## Missing

- The panel's divider line and the original's capacity rule (rows fit to the panel's height); the panel takes a few lines only.
- The in-game settings window on the mods list and `verboseLog`. With Level Up Notify also on, a level shows twice.
