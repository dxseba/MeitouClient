# Vital Sense (port)

Port of Emkej's *Vital Sense* (workshop 3813852288, MCM fork): while the Highlight key is held (or always), a marker over each downed or dead
character near the camera's centre, a `$` coloured by bounty tier, and a body tint. A tick system (`VitalSystem`, every *Update interval*, 150 ms of
game time: every 5th tick) publishes states, bounties and faction standings; an `AddFrame` callback projects the markers for an overlay `IUiExtension` and renews
the tint (`VitalTint`, the engine's `Tint`, through walls). No Harmony.

## Facts

- **State** (first match): dead DE; then only an unconscious character: playing dead PD; recovery coma RC (coma flag, `!canGetUpWakeUp`, no KO
  timer, not `isProbablyDying`, blood above the KO point `pointOfNoReturn`, no blood-loss trauma); dying DY (blood at or under the KO point, or
  the coma flag); else ZZ. **Verified** (original DLL (decompiled)). Meitou: `MedicalState.CanGetUpWakeUp` and `ProbablyDying`, the engine's
  reads named after those tests.
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
  0. **Verified** (its `materials/defferred/*` against the game's `data/materials/deferred`; DLL decompile). Meitou: `Tint` by the colour's
  alpha (relation colours 1; a tier's own alpha, unverified) and, with *Tint through walls* (`character_tint_force_depth_override`, default
  on), through walls for the first 64 characters (the engine's cap).
- **Icons**: drawn 18 by 18 (the DLL's widget size; `*_icon_size_px` is the picture's own size). Recovery coma, dying and dead default to the
  original's `gui/gfx/heart_64px.png`, `death_64px.png`, `grave_64px.png`; a missing picture, and unconscious and playing dead (empty texture),
  fall back to `Kenshi_UI.png` cells: unconscious and recovery coma the Knockout pointer (45 122 32 32), dying and dead 108 49 34 34, playing
  dead 80 126 38 27. **Verified** (original DLL (decompiled); `data/gui/images/kenshi_images.xml` for the Knockout cell). Pictures are looked
  for under this mod's folder (`icons/`, `gui/gfx/`, root), the installed original's (its required item, `OriginalFolder`; the pictures are not
  shipped), then the game's `data/gui/gfx`.
- **Row**: the bounty symbol (`bounty_symbol`, "$", 18 px, 8 to 128, a 12 px wide box at 18), the icon (18 px) and the state text (at least 45 by
  18 px; text size per state, 18 by default, 8 to 128); the symbol before the icon or between icon and text (`bounty_symbol_position`
  `before_state_icon` / `before_state_text`). Each text has eight outline copies (`*_outline_*` text boxes); their colour is not read
  (Meitou: black, *Text outline*). **Verified** (DLL strings and widget sizes); the DLL's centring is not fully read, Meitou centres the row.
- **Bounty glow** (`show_bounty_glow`, on): `gui/gfx/bounty_glow_64px.png`, its 64 px square, as a 24 px image round the 18 px icon (3 px out)
  of a character with a bounty; drawn only beside an icon. **Verified** (decompile: widget size and texture); its colour is **Unknown**.
- **Editable**: per-state texts (`*_text`), relation and tier colours (`*_color_hex`, #RRGGBB or #RRGGBBAA, an invalid one its default), tier
  bounds (`bounty_tier_*_max`, the defaults unless strictly ascending), per-state icon pictures and sizes (`*_icon_texture`, `*_icon_size_px`
  8 to 512, 64; the picture's top left square of that size; an empty or missing picture the atlas cell), the update interval
  (`update_interval_ms`, 50 to 2000). **Verified** (the DLL's clamp and validation messages).
- **Debug** (`debug_log_diagnostics`, `debug_log_texture_info`): each update's counts per state when they change, and each picture lookup, to
  the mod log. Meitou's line formats.
- **Key**: the original's `highlight_key` (default ALT, with Ctrl/Shift/Alt requirements; UNBOUND for always on). Meitou: the game's Highlight
  (Alt), a bindable *Vital Sense highlight* action whose binding can be a chord (the modifier requirements), or *Always on*.
- **Observed**: nothing in play yet (placement, colours, tint, cost at 256 markers with outlines).

- **Atlas cells**: the DLL colours the `Kenshi_UI.png` cells of dying, playing dead and dead with the relation colour (`setColour`); Meitou
  sets the same colour as the image node's `Tint` (only on an atlas cell, not on a configured picture). **Verified** (decompile, per the
  earlier comparison); its look is **Unknown** (not seen in play).
- **Translations**: settings labels, hints and the bounty position choices are the msgids of the original's gettext catalogues
  (`vital_sense`: en_GB, ru_RU, zh_CN), read from the installed original (`AddTranslations`). **Verified** (catalogue msgids read). *Always
  on* and *Text outline* are Meitou's and English; the sub-group names are Meitou's.

## Missing

- The update interval runs on game time (the original's on the wall clock), so it slows and speeds with the game.
