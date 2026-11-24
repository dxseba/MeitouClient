# Level Up Notifications (port)

Port of *[RE_Kenshi] Level Up Notifications* (workshop 3701534964). When a player character's skill reaches a new level, a toast
"Mu: Dexterity Lv.12" fades in and out in a stack at a corner of the screen. Settings on the Settings screen's Mods page.

Facts, **Verified** from the original DLL (decompiled, its string and data tables) and its shipped `LevelUpNotify.ini`:
- Text: `<character name>: <skill> Lv.<level>` (`<unknown>` without a name); preview "Preview: Athletics Lv.42".
- Checked every `pollIntervalMs` (500, 50-10000): each player character (or only the selected one, `showForAllFaction` = false) and each of
  33 skills (stats 1-38 without hive medic, vet, weapons, mass combat, survival), level = floor of `getStat(stat, true)`, only levels 1 and up.
  A first sighting only records. It notifies once, with the level now, when old / N < new / N (`notifyEveryNLevels`, 1-100), unless muted.
- `getStat(stat, true)` is the trained level without modifiers: the game's stat getter (`FUN_140884ac0`) with flag 1 is the raw stat the stats
  window's Levels show (docs/game/ui-screens.md "Levels and Next Level"), so the port reads `CharacterStats.Stored`.
- Toast layout: `toastWidth` x `toastHeight` boxes (600 x 52, each 1-8192; at least 1.8 font heights, font 36), `toastSpacing` 6 apart
  (0-8192), sizes x (screen height / `referenceHeight` 2160, with `autoScale` on; a reference below 1 reads 2160) x `uiScale`; oldest first; `anchor`
  one of nine (default Top Right) with `offsetX`/`offsetY` 40/20 from the edge (scaled, ignored on a centred axis); text aligned to the
  anchor's column; font `Kenshi_StandardFont_Medium`.
- Each lasts `displayDuration` 3 s with `fadeDuration` 0.3 s fades in and out (at most half), alpha x `opacity` (1.0); at most
  `maxSimultaneous` 8, the oldest removed first. Colour `textColor` #FFFFFF00 (yellow); panel presets Yellow FFFF00, White FFFFFF, Green
  00FF88, Cyan 00FFFF, Orange FF8800, Red FF4040, Purple C080FF, Gray C0C0C0.
- The original's settings window is F10 ("Level Up Notifications"), with a "Show Preview" button that adds the preview toast;
  `hideWithGameUI` hides toasts with the interface (F7).

Port specifics: a frame callback (`AddFrame`) ages the toasts on the real clock; `hud.overlay` labels draw them with their own colour,
opacity (ARGB alpha x opacity x fade), text scale (font size x the pixel scale over the standard font's 13.9 px) and
`Animated` fades; with *Hide with game UI* off the overlay sets `ShowWithHudHidden`. Settings: the panel's presets plus *Custom* with the
ini's `textColor` as #AARRGGBB, the ini's `fontSize`, and its `autoScale`, `referenceHeight` and box sizes under *Layout*. *Show Preview*
is a button row (an `Action` setting) that adds the preview toast, as the panel's button. The original has no translation catalogue (English
only, as here). **Observed** (2026-11-17/18, earlier version: notices and floating text) that level
ups are detected in play; the toasts are **Unknown** in a running game.

Missing:
- The F10 panel itself: its settings are on the Settings screen's Mods page (F10 is Meitou's debug menu).
- **Unknown**: the original's scale function body (truncated in the decompile); the port takes (autoScale ? height / reference : 1) x uiScale.
