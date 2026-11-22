# Vital Sense (port)

Port of Emkej's *Vital Sense* (workshop 3813852288, MCM fork): while the Highlight key is held (or always), a marker over each downed or dead
character near the camera's centre, a `$` coloured by bounty tier, and a body tint. A tick system (`VitalSystem`, every 5th tick, the original's
150 ms) publishes states, bounties and faction standings; a Harmony postfix on `PlayerInterface.BindUi` projects the markers for an overlay
`IUiExtension` and renews the tint (`VitalTint`, the engine's `Glow`); an optional postfix on `PointerView.HighlightTargets` adds them to the hover
outline.

## Facts

- **State** (first match): dead DE; then only an unconscious character: playing dead PD; recovery coma RC (coma flag, `!canGetUpWakeUp`, no KO
  timer, not `isProbablyDying`, blood above the KO point `pointOfNoReturn`, no blood-loss trauma); dying DY (blood at or under the KO point, or
  the coma flag); else ZZ. **Verified** (original DLL (decompiled)). Meitou stand-ins: `WakesWithin(0)` for `canGetUpWakeUp`, the health word's
  Dying for `isProbablyDying` (its rule is not known).
- **Relation**: the player's characters squad; hostile to an own character enemy; an animal ally if of the player's faction, else neutral; else
  ally when allied, else neutral; a medically dead non-player character takes the enemy colour. Colours squad #40FF40, ally #DEE85A, enemy
  #FF3333, neutral #FFA64D. **Verified** (original DLL (decompiled); `mod-config.defaults.json`). Meitou: "hostile" is the snapshot's
  `HostileToPlayer` or a hostile faction.
- **Defaults and clamps** (`mod-config.defaults.json`, the DLL's clamp messages): max distance 3500 world units from the camera centre, flat
  (5 to 20000); max markers 96 (8 to 256), nearest first; per-state switches on; bounty symbol on, on everyone on; tiers <=1999 #B0B0B0C8,
  <=4999 #C9A57BD4, <=9999 #9FBD69E0, <=19999 #6FA6D9EC, <=39999 #FFC247F5, <=74999 #FF8A2BFA, else #E03131FF; bounty height 420 cm, state
  offset 0 cm (each 0 to 20000); tint on, include squad on, bounty-only off. **Verified**.
- **Heights**: a downed or dead marker at root + 2 units (the DLL's projection offset) + the state offset; a standing character's `$` at the
  bounty height (1 unit = 10 cm). **Observed** (decompile: the 2.0 offset is seen on the dead path).
- **Tint**: the original's shaders add `coloroverride` to `character`, `creature` (albedo lerped to it by its alpha) and `overrideDepth` to
  `skin` (z forced to 0, drawn through walls); the DLL sets the relation colour at alpha 1, or the bounty tier colour when the bounty is above
  0. **Verified** (its `materials/defferred/*` against the game's `data/materials/deferred`; DLL decompile). Meitou draws a `Glow`
  (additive, strength 0.5) instead.
- **Key**: the original's `highlight_key` (default ALT, with Ctrl/Shift/Alt requirements; UNBOUND for always on). Meitou: the game's Highlight
  (Alt), a bindable *Vital Sense highlight* action (no default: mods may not default to Alt), or *Always on*.
- **Observed**: nothing in play yet (placement, colours, tint, cost at 256 markers).

## Missing

- Engine: a per-character albedo colour override and a through-walls draw (`coloroverride` / `overrideDepth`); the glow is only a stand-in.
- Engine: icons need an atlas crop on `UiNode` images (the ZZ/PD fallback is the `Kenshi_UI.png` glyph `pic_PointerKnockout`); the RC, DY,
  DE icons and the bounty glow are the original's own PNGs (`heart_64px`, `death_64px`, `grave_64px`, `bounty_glow_64px`), not shipped.
- Per-state text and sizes (18 px, 8 to 128), bounty symbol text, size and position (`before_state_icon`), editable colours and tiers, the
  update interval, key modifiers, debug logs.
