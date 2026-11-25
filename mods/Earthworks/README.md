# Earthworks

Partial port of *Earthworks* (Workshop 3829117994), from its Lua scripts, README, Workshop text and `Earthworks.mod`. The original is a required
item: its `Earthworks.mod` (loaded as data with the original enabled) brings the three Earthworks buildings and the research that unlocks them.
Its TerrainLab DLLs and KenshiLua scripts are never run.

- **Clearing:** each Earthworks building clears the scenery in its area as engineers build it.
  - The area is a rectangle turned with the building (rotate it while placing, as any building). Its size defaults to the settings' width and
    depth (288 × 288 units, 28.8 m); the building's panel changes it in 7.2 m steps within the original's limits (14.4–115.2 m a side).
  - It grows over 16 steps of the construction progress, by area.
  - Trees, rocks and other scenery marked `avoid towns` go from the drawn foliage and the navmesh. Resource rocks (iron, copper) stay, as in
    the original.
  - Dismantling the building brings the scenery back. A building inside a town's own no-foliage range clears nothing, so it can never shrink
    the town's cleared area.
- **Saved per save:** the clearing follows the building, and each area's size is kept in the mod's save data (sidecar) and the state hash.
- **Shovel:** a character whose build task works an Earthworks site holds the original's `TerrainLabShovel7.mesh` on its right hand (a bone
  mesh, read from the installed original's folder).
- **Settings:** Clear scenery, Default width, Default depth, Shovel.

## Facts

- **Verified** (`scripts/modules/native_earthworks.lua`, `ids` table): `880010-TerrainLab.mod` is Leveling (`platform`), `880011` Passage
  (`pass`), `880012` Clearing (`clearance`). `Earthworks.mod` lists all three under `enable buildings` of the starts' research
  (`5359-gamedata.base`, `46044-rebirth.mod`).
- **Verified** (scripts' localisation table): the English names are Clearing, Leveling and Passage, under the category "Earthworks".
- **Observed** (original README): Clearing removes ordinary scenery inside a chosen rectangle in steps of 1/16 of the engineers' progress,
  updating models, scenery collision and navmesh without rebuilding the terrain. Iron and copper stay. Leveling and Passage clear their
  area and slopes when confirmed.
- Limits (**Observed**, original README):
  - Clearing: sides of at least 14.4 m.
  - Platform: 14.4–115.2 m a side.
  - Passage: 14.4–921.6 m long and 7.2–115.2 m wide.
  - At most 256 projects a save.
- **Observed** (original README): the original keeps heights, the clearing mask and the project list in `TerrainLab.state` beside `quick.save`.
- **Verified** (docs/formats/foliage.md "Towns"): an owned outpost's circle rejects `avoid towns` meshes but not resource rocks; the nearest
  town decides. That is why a clearing is left out inside a level town's range.
- **Unknown**: whether the original's buildings, whose parts are `16847-nodes_otto1.mod` nodes, show a usable footprint and construction site
  in Meitou's build mode.

- **Verified** (docs/formats/foliage.md, `FoliageWorld.NearestTown`): the town nearest a point decides. The port covers a rectangle with circles on
  a grid (spacing at most 144 units, each reaching its cell's corners), so every point inside has its nearest circle within reach; the edge
  overshoots by at most about a fifth of a cell.
- **Unknown**: the shovel's grip offset and turn on `Bip01 R Hand` (the port hangs it at the bone's origin); whether a site's id stays the
  same across a save and load (the area sizes are keyed by it).

## Harmony

- `Meitou.Game.Host.WorldSync.BuildingSync.UpdateFoliageTowns` (prefix): works out the clearings from the snapshot's buildings. When they
  changed, it clears the sync's private `_foliageOutposts` so the towns are given again.
- `Meitou.Simulation.Navigation.NavSystem.SetPlayerTowns` (prefix): appends the clearings, as owned outposts, to the outposts given. The
  navmesh rebuilds the zones they touch, and the foliage renderer takes the same list.

## Port choices

- The rectangle is the building's own footprint place and rotation (Meitou's build mode) and is resized from its panel, not drawn with the
  original's two corners, drag handles, `<` `>` rotation and XYZ gizmo.
- Leveling and Passage buildings clear their rectangle too: the original clears a leveled area and a passage automatically.

## Not ported yet

- The two-corner drawing, outline, drag handles and gizmo. They need a ground point under the pointer and drawn world outlines: the mod API has
  neither, and the build mode's own pick and gizmo are internal to it. A Harmony port would have to drive the build mode's private state each
  frame.
- The digging cycle `TLShovelDig10M/F.skeleton`. Meitou builds each race's clip set from its animation files, and a clip from a loose Ogre
  skeleton (bone order differing between the male and female skeletons) has no way in without a new loader. The builders keep the game's
  build clip.

## Missing

- **Leveling and Passage terrain.** Harmony on the sample reads (`TerrainHeightmap.Sample`, `ReadRow`, `ReadWindow`, `Downsample`, the
  holes fix's `HeightmapRepair`) could change every height read after a patch, but the readers do not read again:
  - The simulation's ground (`GroundHeights`, from `Scene.Window` and the coarse grid) is built once, at boot (`GameHost.Boot`), before any save
    is chosen, and every world of the session shares it. A per-save delta would need a new ground per load and a rebuilt walk grid.
  - The navmesh and the foliage layout are cached on disk under keys of the heightmap file's stamp and the repair stamp
    (`NavContentHash`, `FoliageLayoutCacheKey`). A per-save delta would need its own key, so each save would build and keep its own navmesh.
  - The terrain streamer's tiles and the GPU height snapshot are read once per window, with no invalidation call to redraw an edited area.
  - The heights feed the simulation (character height, walkability, building ground), so a delta would also have to enter the world state at
    a fixed tick.

  Kept consistent only by changing those engine paths, so the port does not change the ground.
- Per-save terrain deltas: as above.
- **Unknown**: not checked in game.
