# Limited Stats (port)

Port of *属性上限 Limited Stats* (workshop 3807762285). A global cap (default 100) for player characters or everyone, overridden per race
(name or string id) and per character (name) from the mod folder's `limits.json`, which also sets per-stat XP multipliers (the engine's
`IExperienceFilter`). Clamped every 15th tick in the serial slow phase.

- The original's own race and character values are not shipped (its ini is the author's; `limits.json` starts empty with an example).
- **Panel lines** (`AddCharacterPanelLines`): under the stats in the HUD's character panel, a player character shows `Stat cap` with its cap
  (a range when race or character overrides differ by stat), its tooltip naming the stats at their cap; *Panel lines* (0 to 2, default 1) at 2
  adds `At cap` with their count. Other factions' characters show none, even with *Player characters only* off.
- Not ported: the cap label in the stats window itself (its value column is too narrow and the window has no mod slot); the panel lines
  stand in for it.
- **Observed** (2026-11-17, sandbox, cap 1): Toughness 2 was shown as 1. **Observed** (2026-11-18, windowed sandbox on origin/master
  f7ad84a35, user play): the cap holds on master. Race/character overrides, XP multipliers and the panel lines are **Unknown** (not seen in game).
