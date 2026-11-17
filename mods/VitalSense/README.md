# Vital Sense (port)

Port of Emkej's *Vital Sense* (workshop 3813852288, MCM fork; rules and defaults from its GPL-3.0 source, `vs_probe.cpp`,
`vs_marker_render.cpp`, `mod-config.json`).

- **Markers** (Harmony postfix on `PlayerInterface.BindUi`, drawn by an overlay `IUiExtension`): while the Highlight key (Alt) is held, or
  always with *Always on*, a label 42 units above each drawn character within *Max distance* (3500, flat) of the camera's centre, nearest
  first, at most *Marker budget* (8..256, 64). Text ZZ unconscious, RC recovery coma, DY dying, PD playing dead, DE dead, each with its own
  switch. Colour by relation: squad #40FF40, ally #DEE85A, enemy #FF3333 (hostile faction or fighting the player, animals included),
  neutral #B4B4B4 (the original has none). A label's colour is its style's, so each colour is a registered style.
- **State rule** (tick system, every 5th tick, the original's 150 ms): Meitou's medical header word (`HudPanels.DownWord`,
  docs/game/ui-screens.md "Health word choice"), recomputed from `MedicalState` so it works for unselected characters: dead; then only a
  character unconscious or playing dead; being eaten, a vital part below 0 (not above the KO point) or blood 0 with a destroyed limb: DY;
  a KO timer: ZZ; coma with no wound: RC; playing dead: PD; out from blood loss and still bleeding ("Bleeding out"): DY; else ZZ.
- **Bounty**: a "$" left of the state (alone, centred, on a standing character with *Bounty on everyone*), coloured by the total bounty
  (`BountyLedger.Total`): <=1999 #B0B0B0, <=4999 #C9A57B, <=9999 #9FBD69, <=19999 #6FA6D9, <=39999 #FFC247, <=74999 #FF8A2B, else #E03131.
- **Outline** (postfix on `PointerView.HighlightTargets`, setting on): the marked downed characters join the hover outline the held Alt
  draws, beyond its 500-unit radius, in the relation colour. Needs Settings: Hover outline.
- Differences: no shader tint (the outline stands in), no icons or glow, one font size; label alpha ignored; the keybind and modifier
  options are the game's Highlight key; the original's tint options are gone. The original derives dying from KenshiLib's
  `isProbablyDying` and blood thresholds; this uses Meitou's header word instead, so the two can disagree.
- Unconscious characters' parts are read in the tick, not from the snapshot (whose part rows exist only for selected or inspected ones).
- **Unknown**: all in-play behaviour (marker placement, colours, the outline, cost with 256 markers) until tested in game.
