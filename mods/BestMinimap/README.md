# Best Minimap (port)

Port of *Best Minimap (QoL)* (Workshop 3824087152, Nexus 2242, version 5), from the original's DLL (`BestMinimap.dll`, Ghidra decompile),
its `BestMinimap.defaults.ini`, README and `best_minimap.mo` catalogues. The original is a required Workshop item: its catalogues are read at
runtime from its folder; nothing of it is shipped.

- **Minimap** in the top right corner (`Left`/`Top` -1) on the game's map picture: a circle, square or 16:10 rectangle of `Size` pixels,
  `Range` world units from the centre to the edge, centred on the camera's target or the selected characters, north up or turned with the
  camera. Background colour where the picture ends, map opacity, frame (none, line, game panel; the circle gets a line). Hidden while the
  overview (big map) is open or the interface is hidden (F7).
- **Characters** as dots by who they are (squad, allies, neutrals, enemies, animals; the dead dimmed), selected bigger with a white outline, a
  nose where each looks, bounties (gold ring and sum), names, the player's squad names; colours auto (the big map's) or `#RRGGBB`.
- **Places** the big map knows (discovered towns, known camps and nests, the player's outposts) with the big map's icons or diamonds, names
  up to `TownNamesRange`, town radius rings, colour by the faction's standing.
- **Map-markers and WantedMap marks** (the Meitou ports, optional dependencies), pinned to the edge with an arrow when out of range.
- **Attack alert**: a pulsing ring where a player character is hit (at the edge, pointing, when out of range), the frame flashing, and
  "Under attack: names" in the message line at most once in 3 seconds.
- **Camera arrow** (own colour and size) and **camera reach** (dotted or solid, width 1-8) around the player's characters.
- **Controls**: Ctrl+M show / hide, `=` / `-` zoom (repeats while held), Ctrl+= / Ctrl+- size, the wheel over it zooms (Ctrl+wheel: size),
  left drag moves it (unless locked; from the lower right corner it resizes), right click sends the selected there (a green flag), double
  click moves the camera there when within the camera's reach. Keys are rebindable on the Controls page.
- **Information strip** under the map: zoom, PC clock, centre X/Z in metres, camera heading.
- **Settings**: every ini key as a Mod settings row (labels and hints the original's catalogue texts, so they translate).

## Facts (Verified: decompile of `BestMinimap.dll` and its shipped files, unless marked)

- Ini keys and defaults as `BestMinimap.defaults.ini`. Loader clamps: `Size` 80-900, `Left`/`Top` -1-10000, `Range` 30-150000; MCM ranges:
  `DotSize` 2-16, `TownIconSize` 10-64, `CameraLineWidth` 1-8, `AlertSeconds` up to 120, `UpdateFps` up to 60, `Opacity` 1-10.
- Zoom multiplies `Range` by 0.8 (in) or 1.25 (out), clamped to 30-150000; the size keys and Ctrl+wheel step `Size` by 20, clamped to 80-900
  (`.rdata` constants read from the DLL).
- A right click calls the game's `PlayerInterface::playerMove` with the world point (a normal move order); port: `MoveOrder`.
- Texts: the catalogue's msgids (settings labels and hints, "Under attack: ", `Zoom:%s`, `PC time:%s`, `X:%d Z:%d`, `Dir:%03d`, "ally",
  "neutral", "hostile", "your faction"). Translations: `AddTranslations("best_minimap", <original folder>)`.
- Zoom percentage is 2000 / Range x 100; coordinates are world units / 10 (metres); heading 000-359 clockwise from map north (README).
- WantedMap marks come from its `WantedMap_GetMarkers` export (port: `WantedMapMod.GetMarkers()`); Map-markers from the save's
  `Map-markers.json` (port: the Map-markers port's markers, the same data).
- **Observed**: `#AFA68B` sits beside the defaults among the DLL's strings; the port uses it as the "auto" frame colour.
- Auto colours (**Observed**, Meitou's world map): MarkerPlayer `#8DCDFF`, MarkerAlly `#5CB473`, MarkerNeutral `#DFDFDF`, MarkerEnemy
  `#E65139`, movement `#79B44B`. Camera reach 0 = Meitou's leash radius (`KenshiCamera.LeashRadius`, 2250 units).
- **Unknown**: the original's double-click interval (the port uses 0.4 s), alert pulse rate, dot outline widths and town-name font size.

## Hooks

- Public API: `AddFrame` (snapshot, camera, pointer, selection), `AddUi` overlay (image nodes with `ImageCrop`, pointer events
  `Release|Drag|Wheel|Buttons`), `AddKeyAction` (5 actions), `AddCombatHitFeed` (alerts), `AddSettings`, `Enqueue(MoveOrder)`,
  `AddTranslations`, `Dependency<IMod>` (optional `dxseba.mapmarkers`, `dxseba.wantedmap`, read by reflection).
- **Harmony: `Meitou.Game.Ui.UiHost.Control`** (postfix): adds `ImageAngle` to marked image nodes (the map turning with the camera, the camera
  arrow, noses, edge arrows) and swaps a node's image file for a `Kenshi_MapMarkers` group (the big map's town icons, the move flag).
- Reflection (no patch): `Meitou.Modding.ModFrame._host`; `Meitou.Game.GameHost.Assets` (finds `GUI_Map.dds` as the big map does, mods'
  replacements included), `GameHost.PopData` (factions, relations, towns), `GameHost.Session` -> `CameraRig.MovePivot` (double click camera),
  `GameHost.Player` -> `PlayerInterface.Follow` (cleared so a followed character stops pulling the camera back).

## Port choices

- The map picture is scaled down to `MaxSourceSize` on a worker and kept as a PNG in `%LOCALAPPDATA%/Meitou/mod-cache/dxseba.bestminimap`
  (with the port's own white shapes); remade when the source, limit or `SwapRedBlue` changes.
- A round minimap is cut from 3-pixel strips (the overlay clips children to their parent); the original masks its own texture.
- The world point to picture mapping is Meitou's (the picture spans the world's 294912 units); the original asks `MapScreen::worldToMapCoords`.
- `Detailed log` logs move orders and camera moves to the mod log; no render timings.

## Missing

- Squad names of squads that are not the player's ("Other squads" shows nothing): the snapshot names only the player's squads.
- **Unknown**: not run in game (the strips, the turned picture, the image-set icons, the camera move and every control).
