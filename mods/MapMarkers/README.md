# Map markers (port)

Port of *Map markers* (MCM fork, workshop 3813852161) of Emkej8's mod (GPL-3.0 source,
[Emkej/Map-markers](https://github.com/Emkej/Map-markers) `0.1.0-alpha.2`), rules from that source.

- **Markers**: an id (one past the highest), a point on the map image (0..1, world `(n*2-1)*147456` on X and Z), a type and a label of at most
  48 characters (tabs and line breaks become spaces, other control characters go, `[]{}` become `()`, no leading or doubled spaces).
  Types and glyphs: Note `n`, Danger `!`, Stash `s`, Ruin `r`, Mine `m`, Base `b`, Trader `$`, Safe Spot `+`, Quest `q`, Todo `t`, in the
  original's colours (brighter when selected). A marker is an 18 px square (26 selected) times the UI scale, kept inside the map image.
- **Controls** (only while the markers are shown): middle click on empty map adds a marker of the default type and selects it; left click on
  a marker selects it; left click on empty map moves the selected one; Delete removes it (not while a box has the typing focus); right click
  on the map deselects. The game still pans and gives the move order, as the original only watches the buttons.
- **Editor** (while a marker is selected), at the map's top right, in the MCM fork's layout (**Verified**, original DLL (decompiled): a
  510 x 198 panel, header at 16,16 with an "x" close at 470,13; type button 16,42 346 wide in the type's colour cycling the ten types, Delete at
  374,42; Label box 108,74 386 wide (port: each change kept at once, trailing spaces trimmed as the editor closes); Colour row at y 107:
  "-" and ten 22 px swatches 28 apart from x 108 (frame 0.05 grey; port: its own ten colours, white first, the fork's palette is its author's and not copied); a swatch replaces the label's leading `#RRGGBB` tag (7 characters, not `##`) and commits, "-" drops it; the
  hint "MMB new marker | LMB move | Enter save" / "Del or the button - delete | RMB deselect" at 16,142). Port: sizes times the UI scale.
  Markers under the editor are hidden, as in the original. **Hover label**: "Type: label" in `#F5F5F5` on `#1A1A1AF2` above the marker, at
  most 480 px wide (the fork measures the text; the port estimates it, tags left out).
- **Footer**: "Markers: On/Off" at the map's left, level with the zoom buttons.
- **Saved per save** in the mod's sidecar (`IModSaveData`, system `markers`, version 1) and, as the original, in `<save>/Map-markers.json`
  written beside every save the game writes (`AddSaveWritten`). A loaded or imported save's `Map-markers.json` (the original's or the port's)
  replaces the sidecar's markers (`IWorldReady`, `world.Start.SaveFolder`), so saves move between the original and the port.
- **Settings** (Mod settings): Enabled, Markers visible, Close editor on map close, Show hover labels, Hover label text / background colour
  (free text, `#RRGGBB` or `#RRGGBBAA`, the original's `hover_label_*_color_hex`; invalid: its default), Default marker type (labels and
  hints the MCM fork's, **Verified** DLL strings; the fork's MCM page has no Markers visible, which is its footer button).
- **Original's save file** (**Verified**, DLL strings): `<save>/Map-markers.json`, `{"version": 3, "markers": [{"id", "x", "y", "type", "label"}]}`, x and y
  the 0..1 map point, type `note`, `danger`, `stash`, `ruin`, `mine`, `base`, `trader`, `safe_spot`, `quest`, `todo` (the DLL also holds
  `trader_shop` and `safe`, read as aliases; its loader takes integers too); v5 keeps it across imports.
- Hooks (public only): screen controls on the overview's `MapImage` (markers, hover label, editor; the pointer from `AddFrame`) and left of
  `MapZoomInButton` (the footer button), `AddMapInput` (presses, Delete, the map closing). The label box is a screen-control text box.

## Missing

- The editor is not draggable and has no remembered position (`editor_left`/`editor_top`): screen controls get presses only (pointer drags
  reach overlay nodes, which draw under the screens).
- A new marker's label box does not take the typing focus, and Enter only ends typing (the original also deselects): a mod text box has no
  focus call and its submit is not passed on.
- Markers are flat coloured squares with a dark glyph, not the tinted button skin; the footer button's width is fixed, not measured.
- No diagnostics hotkeys (Ctrl+Alt+F7/F8) or Debug logging setting; no Russian or Chinese text.

**Unknown**: not run in game (the screen controls, the footer button beside the zoom buttons, the JSON exchange with the original). Builds.
