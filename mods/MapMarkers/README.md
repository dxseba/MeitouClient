# Map markers (port)

Port of *Map markers* (MCM fork, workshop 3813852161) of Emkej8's mod (GPL-3.0 source,
[Emkej/Map-markers](https://github.com/Emkej/Map-markers) `0.1.0-alpha.2`), rules from that source.

- **Markers**: an id (one past the highest), a point on the map image (0..1, world `(n*2-1)*147456` on X and Z), a type and a label of at most
  48 characters (tabs and line breaks become spaces, other control characters go, `[]{}` become `()`, no leading or doubled spaces).
  Types and glyphs: Note `n`, Danger `!`, Stash `s`, Ruin `r`, Mine `m`, Base `b`, Trader `$`, Safe Spot `+`, Quest `q`, Todo `t`, in the
  original's colours (brighter when selected). A marker is an 18 px square (26 selected) times the UI scale, kept inside the map image.
- **Controls** (only while the markers are shown): middle click on empty map adds a marker of the default type, selects it and puts the
  typing focus in its label; left click on a marker selects it; left click on empty map moves the selected one; Delete removes it (not while
  a box has the typing focus); right click on the map deselects. The game still pans and gives the move order, as the original only watches
  the buttons.
- **Editor** (while a marker is selected), at the map's top right: "Marker N: label", a type button ("Type: Danger", in the type's colour)
  cycling the ten types, the label box (Enter commits and deselects, losing the focus commits), Delete and Close, and the controls hint.
  Markers under the editor are hidden, as in the original. **Hover label**: "Type: label" in `#F5F5F5` on `#1A1A1AF2` above the marker.
- **Footer**: "Markers: On/Off" at the map's left, level with the zoom buttons.
- **Saved per save** in the mod's sidecar (`IModSaveData`, system `markers`, version 1); a new game, an import or a save made without the mod
  starts with none.
- **Settings** (Mod settings): Enable, Markers visible, Close editor on map close, Show hover labels, Default marker type.
- Hooks: postfix `WorldMap.AddMapContent` (the overlay, after the map's own markers), postfix `WorldMap.UpdateMap` (map closed), prefix
  `PlayerInterface.MouseDown` (the presses; a press on the mod's widgets goes to the screen host, as the map would pan on it), prefix
  `PlayerInterface.UiKey` (Delete), prefix `ScreenRouter.FaithfulAction` (the widgets' events). The label box is the host's own edit box
  (typing, caret, clipboard); a new marker focuses it through the host's private `FocusEdit`.
- Differences: the editor is not draggable and has no remembered position; markers are flat coloured squares with a dark glyph, not the
  tinted button skin; the footer button's "Markers visible" is the setting, kept across sessions only when changed on the settings page;
  no diagnostics hotkeys, no label colour setting, no Russian or Chinese text; no `Map-markers.json` (markers live in the save's sidecar, so
  markers made with the original are not read).
- **Unknown**: not run in game. Builds (0 warnings).
