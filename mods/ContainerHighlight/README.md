# Container Highlight (port)

Port of *Container Highlight (MCM fork)* (workshop 3813852014, LEOPARD's fork of Emkej8's mod): while the highlight key is held, nearby
lootable containers get a marker (an icon and an outlined label, each optional, both off by default) and are tinted in the world. An `AddFrame`
callback scans `WorldSnapshot.Containers` and projects the markers for an overlay `IUiExtension`; the tint is Meitou's building outline, given
the containers by a Harmony postfix. A key action `Highlight key` (Controls page, unbound; chords such as Ctrl+X) opens the gate as well as the
game's Highlight key (Alt).

The installed original is a required item: its catalogues (`container_highlight`, en_GB, ru_RU, zh_CN) are read at start (`AddTranslations`)
and its icon `gui/gfx/container_highlight_chest.png` is drawn from its folder. Without it the texts are English and no icon shows.

## Facts (Verified: Ghidra decompile of `Container-Highlight.dll` v3, its scan, filter, marker and settings code, and its defaults, unless marked)

- **Settings** (MCM rows in order, `mod-config.defaults.json`): enabled on; highlight key `ALT` (Require Ctrl/Shift/Alt kept in the file
  only); max highlight distance 900 (100 to 5000, step 50); show icons off; show text off; marker text `BOX` (at most 24); marker colour
  `#F6C454`; enable tint on; tint colour `#F6C454` with the file's alpha `D9`; Advanced: debug logging, log nearby probe snapshot, log render
  trace. A mod key may not default to Alt, so the game's Highlight key (Alt) opens the gate and the mod's own key is unbound until bound.
- **Scan** (every `update_interval_ms`, 150; 250, 350 or 500 when 128, 256 or 512 objects were scanned): `GameWorld::getObjectsWithinSphere`
  round the selected character (else the camera's centre), radius the max distance as it is (world units, though the label says metres), for
  BUILDING and CONTAINER objects, at most `max_objects_per_type` (256) each; objects without an inventory are left out; the matches are kept
  nearest first, at most 48. The port scans every 150 ms of host time.
- **Filter**: a CONTAINER object always matches. A building matches when its class is STORAGE (vfunc `0x358` = 3) or its function is
  BF_RESOURCE_STORAGE (2) or BF_GENERAL_STORAGE (13), else when its name or the object's holds `storage`, `barrel`, `crate`, `chest`,
  `container` or `bin` (lower-cased). The port tests the function (its functionality's `function`) and the names (`BaseName` and the shown
  name). **Unknown**: which record field is the class (vfunc `0x358`); not tested by the port.
- **Marker**: an 18 px icon `ImageBox` and a 45 x 18 text box one pixel to its right (skin `Kenshi_TextboxStandardText`), with eight copies of
  the text one pixel round it for the outline, in the marker colour, text size 18 (`marker_text_size_px`). Anchor: 0.3 above the top of the
  building's meshes when that top is above 0.02, else 0.9 above the object. The port reads the top from the BUILDING's meshes' bounds
  (`WorldObjectLayout.Building`, `OgreMeshReader.TryReadBounds`), and centres the row on the point. **Unknown**: the row's exact alignment.
- **Tint**: the original swaps the containers' Ogre materials for clones whose shaders (its `materials/defferred` HLSL) blend in the tint
  colour, with depth override (drawn through walls) on by default. Meitou's `Tint` reaches characters only (`GlowTarget`); the port uses the
  building outline (Harmony: `Meitou.Game.Building.BuildingSelection.OutlinedBuildings`, postfix), which draws a building's meshes alone into
  a mask, outlines it and blends it with the colour, not through walls. Each outlined building takes a full-screen mask, so only the nearest 8
  are tinted. It needs the game's building outline (Settings, on by default). **Observed** (by reading `BuildingSelection.PickFurnitureAt` and
  `WorldObjectRenderer.Selection`): a container fixture's id is its outline key.
- **Observed** (by reading `FixtureRegistry.ContainerSpots`): `WorldSnapshot.Containers` lists every fixture with an inventory, the town's and
  the player's. Meitou keeps no separate CONTAINER objects in the snapshot.
- *Log nearby probe snapshot* (a button) logs the next scan's decisions; *Debug logging* logs a scan summary at most every 5 s.

## Harmony

- `Meitou.Game.Building.BuildingSelection.OutlinedBuildings(bool)` postfix (139 bytes of IL, not inlined): adds the containers to the outlined
  buildings while the gate is open and the tint is on. A throw is logged once and the tint stays off for the session.

## Port choices

- Not ported: the screen box highlight (`enable_screen_highlight`, a file-only key, off by default, not in the menu), which frames each
  container's projected model bounds; and *Log render trace*, a Direct3D 11 call trace of the original's renderer.
- At most 8 containers are tinted at once (above).

## Missing

- The tint through walls (`tint_force_depth_override`, on by default): Meitou's building outline has no through-walls mask; the engine draws
  one only for a character's `Tint` (`throughWalls`), and buildings are no `GlowTarget`.
- **Unknown**: not checked in game.
