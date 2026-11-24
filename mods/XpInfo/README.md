# XP Info (port)

Port of *XP Info - experience gain notifications* (workshop 3795213616, GPLv3).

- **Gains**: every 10th tick the player characters' stored stats are compared with the last poll (the original hooks `increaseStat`); a rise is
  summed per skill and shown as `+N% Skill` once it reaches *Announce gains over %* and the skill's last announcement is *Announce at most every*
  ago; a character's gains of one poll are merged into one label, one gain a line, the first four and then `+N more`.
- **Levels**: a new whole level shows `Skill N` in gold, a little above the gains, at once and in place of that skill's gain.
- **Drawing**: an `AddFrame` callback projects each label over its character (`ModFrame.Project`) for an overlay laid out every frame while
  any shows, rising for *Seconds* (2.5, Meitou's); a first sighting (spawn, load, recruit) only records. No Harmony.
- **Panel XP bars** (`AddCharacterPanelLines`): under the stats in the HUD's character panel, up to *Most bars at once* of the skills that gained
  XP within *Keep a bar for* seconds, the most recent first, as many as fit under the stats (the engine leaves out lines past the panel's
  height, the original's capacity rule), each `Skill  level  N%` over a bar of its progress to the next level.

## Facts

- **Settings** (`xp_info.ini`, labels from its settings window): floatingLabels 1 "XP gain text", levelUpLabels 1 "Level-up text", panelBars 1
  "XP bars in panel", maxRows 2 "Most bars at once", minPercent 2.00 "Announce gains over %", announceEverySeconds 1.0 "Announce at most every
  (s)", recentSeconds 120 "Keep a bar for (s)", verboseLog 0. **Verified** (original ini; DLL strings). The settings window is Meitou's settings page;
  *Most bars at once* goes to 10 here (the original's range is not known).
- **Texts**: gain `+%.0f%% %s` (whole per cent), level `%s %d`. An entry announces when it has a level, or when its gain is at least minPercent
  and announceEverySeconds have passed since its last announcement; a level replaces the gain text. **Verified** (original DLL (decompiled)).
  Meitou runs the intervals on game time (the original's on the wall clock).
- **Merging**: one label per character and poll lists at most four gains, one a line, then `+N more` (`\n+%d more`). **Verified** (original
  DLL (decompiled): the cap of 4 and the format); the separator byte is read as a newline (**Observed**).
- **Verbose log** (`verboseLog`): the settings line at start and every label shown, to the mod log. **Observed** (DLL strings); most of the
  original's verbose lines are its widget geometry, which has no counterpart.
- Nothing is saved. **Observed** (description).
- **Unknown**: not checked in game, the panel bars included (how many rows fit beside the game's stats, their look).

## Missing

- The divider line between the game's stats and the bars (engine gap: no divider row for panel lines).
- With Level Up Notify also on, a level shows twice (each mod announces it, as the originals do together).
