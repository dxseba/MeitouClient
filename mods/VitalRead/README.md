# Vital Read (port)

Port of *Vital Read* (workshop 3813852230, the MCM fork). The rules come from its GPL-3.0 upstream source (`Vital-Read.cpp`,
`TryResolveMemberStateSnapshot` / `TryResolvePortraitTextLabelState`). The fork's two bed states come from its description.

- **Badge** per squad-bar portrait, first match: dead none; playing dead `PD`; dying `DY`; recovery coma `RC`; unconscious `ZZ`; crippled leg
  `CrL`; crippled arm `CrA`; starving `ST`; a robot in a repair bed `RP` (fork, a blue wrench there); in a bed `+` (fork, a blue plus).
- **Rules**:
  - Dying uses the game's health word (docs/game/ui-screens.md "Health word choice"): blood at or under 0 with wound severity 1, or
    severity 3. The upstream used KenshiLib's `pointOfNoReturn` / `isProbablyDying` and required the character to be unconscious.
  - Recovery coma: unconscious with the coma flag and no KO timer left (the upstream also needed no KO timer).
  - Crippled leg: `MedicalState.Crawling || LegsMessedUp`, standing in for the upstream's `isCrippled && !canIkick`.
  - Crippled arm: either `ArmWorks(left)` false.
  - Starving: hunger under 1, the health word's Starving.
  - Repair bed: the bed's functionality `function` is 25 (`BedKind.RepairBed`) and the race `IsRobot`.
- **Hooks**: `VitalSystem`, a tick system, publishes each player character's wound severity, leg, arm, robot and repair-bed facts every 5 ticks as
  a volatile `FrozenDictionary`. The rest comes from the snapshot (`CharacterSnapshot.Body`, `InBed`). A Harmony postfix on the private
  `PortraitBar.AddSquadCells` adds a non-pickable `TextBox` in each marked cell's corner, clipped to the page and following the bar's scroll.
- **Settings**: *Enable*, *Show text*, *Coloured text* (the colours stand in for the icons), *Text size* (18 px at 1080p), *Anchor* (top right),
  *Margin*. The upstream's icon settings are left out: its PNG icons are not shipped.
- Differences: text only, never icons. Wound severity is reimplemented (the game's own fold is internal to `HudPanels`).
- **Observed** (2026-11-18): builds against the current engine (0 warnings). **Unknown**: everything in play (placement, scroll and clip, text
  size, each state's trigger).
