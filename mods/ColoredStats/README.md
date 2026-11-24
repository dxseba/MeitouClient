# ColoredStats (port)

Port of *ColoredStats* (workshop 3805088430, `StatsColor.dll`): every number on the stats window takes a colour by its value, red for low,
through yellow, to green and green-cyan for 100 and over. One screen override on the stats window (`UiRegistration.AddScreenOverrides("stats",
"*")`). No Harmony.

## Facts (Verified: Ghidra decompile of `StatsColor.dll`, its frame callback and widget walk, unless marked)

- Once a frame (a MyGUI frame delegate set up from a `TitleScreen` constructor hook) it walks every visible root window; one whose text boxes
  contain one of 力量, 韧性, STRENGTH or TOUGHNESS, and one of 武士刀, 科学, 武器匠, Katanas, Science or Weapon smith (any substring) is the
  stats window. So the original works only with the English and Chinese labels. The port takes Meitou's stats screen by its id, in any language.
- Every text box of that window (buttons and edit boxes are text boxes in MyGUI) is read: a leading 7-character `#RRGGBB` tag and one space
  after it are dropped, the rest trimmed of space, tab, CR and LF, then it must be only digits, a `-` first and at most one `.` (whitespace
  inside is dropped), with a digit. Its caption becomes `#RRGGBB` + space + the number when that differs.
- Colour: below 0 `#A83232`; else step `(int)(value / 10)`, at most 10: `#FF5252`, `#FF7A4D`, `#FFA64D`, `#FFD24D`, `#FFFF4D`, `#D9FF4D`,
  `#A6FF4D`, `#73FF4D`, `#4DFF4D`, `#4DFF80`, `#4DFFB3`.
- No settings, no ini, no translations. **Observed**: the `.mod` (274 bytes) holds only the description; the PDB path says "KillButton" (a
  reused template).
- **Observed** (by reading `StatsWindow`): Meitou's stat numbers are the `*-value-*` labels (`*-mod-value-*` for mod skills), whole levels.

## Missing

- Nothing. **Unknown**: not checked in game.
