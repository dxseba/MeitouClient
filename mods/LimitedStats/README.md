# Limited Stats (port)

Port of *属性上限 Limited Stats* (workshop 3807762285, RE_Kenshi plugin `LimitTalent.dll`). Every character (animals too, for XP) gets a
talent and per-stat caps; XP stops at a stat's cap and stats above it are pulled back (animals are not pulled back).

The original's `LimitTalent.ini` and `StatGUI.ini` are its author's data and are not shipped: the port reads them from its own folder, else
from the original mod (its Workshop folder `3807762285` beside this one, or the mod named `Limit Talent` in the install). Without them the
built-in defaults are the original DLL's own: caps 100 x the race multiplier (at most 150), every talent weight 0 (all Normal), no colours.
`[General]`, `[Weights]` and `[Debug]` become Mod settings (the ini's values are their defaults, so Reset restores the ini).

## Behaviour (**Verified**, original source `LimitTalent.cpp` shipped with the mod, matching the DLL's strings)

- **Race cap** per stat: `CapScale x m + CapShift`, held in `[CapMin, CapMax]`; m is a `[Race]` entry's value (race string id or name, keys split
  by `|`; `default x` for unlisted stats, else 1) or the race's own XP multiplier (`RaceData.StatMultiplier`; outside (0, 100) reads 1).
- **Entries** (`[SpecialCharacter]` by record id then name, then `[Stats]` by the record's first `stats` reference, record id, name; case
  ignored): `stat: buff(x) & cap(y)`, `all:`, `cap(race|race_max|race_min, x)`, numbers `randi/randf/randi_ignore/randf_ignore`, key modifier
  `[rand_other_skills(_not)(,Type...)]` (readme "Configuration Syntax"). Without an entry the talent is random.
- **Random talent**: a slave (`SlaveState` 1) is Loser at `SlaveLoserPercent`, otherwise its Loser weight is 0; else a type by weight, heaviest
  first. Skills: a random first one, then each the highest summed `[Correlation]` to those taken plus `rand x CorrelationJitter`. Lazy/Diligent/
  Genius: their skills at the type's rate; Loser: every stat at `LoserRate`, its skills at `LoserSkillRate`; Genius breaks the first
  `GeniusBreakCount` skills' caps, Superhuman `SuperBreakCount` skills (break cap = `CapScale x m x mult + CapShift`, held).
- Rolled once per character, re-rolled when its record id changes, kept with the save (original: in the save's `LimitTalentSave` record; port:
  `IModSaveData` by lasting id). The original seeds from `GetTickCount64`; the port from the world seed and the lasting id.
- **XP** (`increaseStat` hook): none at or above the cap (unless `ProtectGap` >= 0 and the stat is at least that far above, which trains toward
  `CapMax`); the gain is divided by the race's own multiplier, times the talent rate and `PlayerExpRate`/`NpcExpRate`, added in 0.5 chunks on the
  core curve whose top is the cap; the game's own top (101, a training dummy's level) is ignored, as the original's hook passes its own
  limit. First aid and mass combat (added straight, no `increaseStat`) are untouched. The scan clamps every 120 frames (port: 15 ticks).
- **Stats window**: the selected stat's race XP bonus line becomes `LevelCapLabel + cap (+rate%)`, cap and rate coloured; with
  `EnableStatColors` stat names take one of 11 colours by cap kind (special: the talent's own cap, or with `GoldCapByValue` cap >= gold;
  high >= green; low <= red) and rate; `AttrStats` use the attribute set. Port: a Harmony postfix on the internal `StatsWindow.AddStatsContent`.

## Port notes

- XP through `IExperienceFilter.Adjust`: the original's chunked rise is handed to the game as one gain on the curve topped at min(cap,
  `CapMax`) (with over-cap protection: the scaled amount on the curve topped at `CapMax`), so caps above 101 and dummies train as in the
  original. The game holds one gain's amount to 20, so a single gain whose equivalent amount passes 20 rises a little less.
- Port options: *Player characters only* (off) and *Panel lines* (0; HUD lines with the cap range and talent).
- **Unknown**: everything above in game (not run). The stats window postfix and inline colour tags in its description are untested.
- **Observed** (2026-11-17/18, an earlier port with a flat cap): the clamp holds in game.

## Missing

- A public stats-window hook that can recolour the window's stat labels and replace its race XP line (the Harmony postfix stands in).
