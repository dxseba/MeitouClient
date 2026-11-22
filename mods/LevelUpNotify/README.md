# Level Up Notifications (port)

Port of *[RE_Kenshi] Level Up Notifications* (workshop 3701534964). When a player character's skill reaches a new level, a toast
"Mu: Dexterity Lv.12" fades in and out in a stack at a corner of the screen. Settings on the Settings screen's Mods page.

Facts, **Verified** from the original DLL (decompiled, its string and data tables) and its shipped `LevelUpNotify.ini`:
- Text: `<character name>: <skill> Lv.<level>` (`<unknown>` without a name); preview "Preview: Athletics Lv.42".
- Checked every `pollIntervalMs` (500, 50-10000): each player character (or only the selected one, `showForAllFaction` = false) and each of
  33 skills (stats 1-38 without hive medic, vet, weapons, mass combat, survival), level = floor of the stat, only levels 1 and up. A first
  sighting only records. It notifies once, with the level now, when old / N < new / N (`notifyEveryNLevels`, 1-100), unless muted.
- Toast layout: 600 x 52 boxes (at least 1.8 font heights, font 36), 6 apart, sizes x (screen height / 2160) x `uiScale`; oldest first; `anchor`
  one of nine (default Top Right) with `offsetX`/`offsetY` 40/20 from the edge (scaled, ignored on a centred axis); text aligned to the
  anchor's column; font `Kenshi_StandardFont_Medium`.
- Each lasts `displayDuration` 3 s with `fadeDuration` 0.3 s fades in and out (at most half), alpha x `opacity` (1.0); at most
  `maxSimultaneous` 8, the oldest removed first. Colour `textColor` #FFFFFF00 (yellow); panel presets Yellow FFFF00, White FFFFFF, Green
  00FF88, Cyan 00FFFF, Orange FF8800, Red FF4040, Purple C080FF, Gray C0C0C0.
- The original's settings window is F10 ("Level Up Notifications"); `hideWithGameUI` hides toasts with the interface (F7).

Port specifics: a postfix on `PlayerInterface.BindUi` ages the toasts on the real clock and binds them; `hud.overlay` labels draw them.
**Observed** (2026-11-17/18, earlier version: notices and floating text) that level ups are detected in play; the toasts are **Unknown** in a
running game.

Missing:
- Custom ARGB colours (only the presets), the font size scaling (the text keeps the HUD font size; boxes scale), and showing toasts with the
  interface hidden (`hideWithGameUI` = false): the HUD overlay hides with it. The F10 panel's "Show Preview".
- Toasts age only when the HUD is arranged (each tick, mouse move), so while paused with a still mouse they wait.
- Whether the original's `getStat(stat, true)` includes stat modifiers is **Unknown**; the port reads the stat with modifiers.
