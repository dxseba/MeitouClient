# Build Mode Gizmo

Port of *Build Mode Gizmo* release 5 (workshop 3813761881), from its DLL (`BuildModeGizmo.dll`, Ghidra decompile), `README.txt`,
`BuildModeGizmo.defaults.ini`, gettext catalogues and Workshop description. The original is the required Workshop item: its catalogues
(`locale/*/LC_MESSAGES/build_mode_gizmo.mo`, English, Russian, Chinese) are read from its installed folder.

- Selecting one of your own building sites that is not built yet opens the arrows and the transform window titled with its name. Drag the
  red X or blue Z arrow to move the site along it (the green up arrow too with "Settle on the ground" off); the window's Rotate button switches
  to the turning handle. Releasing applies the move. The window also takes typed X, Y, Z and yaw (Apply), steps of 1 and 10 units (1 and 15
  degrees) and Reset (back to where the site stood when it was selected).
- The site keeps its id, record, delivered materials and progress; it is rebuilt where it now stands: its model, collision, lights and navmesh
  membership are taken out and put in again, and the save keeps the new place. The site's builders, deliverers and fetchers drop the way they
  walk, so they aim at the new work spot at once; the player's outposts are reassessed as after a placement (a site moved out of an
  outpost's radius leaves it). With "Settle on the ground" on it keeps its height over the ground.
- The arrows stay while the site is selected and through clicks on the ground; they go when another building is selected, the site is
  finished or gone, or the window is closed with its cross or Esc (then a "Move arrows" button on the site's panel brings them back).
- Settings: Enabled, Arrows on your construction site, Settle on the ground after a move (the original's labels, translated by its catalogues).

## Facts (Verified: decompile strings and the defaults ini, unless marked)

- Settings and defaults: `Enabled` 1, `SiteArrows` 1, `SnapToGround` 1, `EditorAnyBuilding` 1, `EditorArrows` 0, `SkipDuringPlacement` 1,
  `ApplyChanges` 0, `Debug` 0; text domain `build_mode_gizmo`.
- The arrows close on: another site or object selected, the site built, removed or gone, the window closed by the player, a new building being
  placed (log strings "another site selected", "site built, removed or gone", "transform window closed by the player", "placing a new
  building").
- The move is written into the building and the site rebuilt as on load, its work marker moved ("site rebuilt at ..., marker ... -> ...").
- **Observed** (README.txt): the window's cross hides the arrows until something else is selected on that site; the selection box is not drawn
  while an arrow is dragged.

## Port choices

- The arrows are interface handles at the projected axis tips (the original uses the editor's gizmo); the site's picture moves on release,
  not during the drag (the fields show the place meanwhile).
- Not moved (refused quietly): a complete site, another faction's, a level building, a wall piece, a site on or in another building, a site
  with furniture or pieces on it.

## Harmony

None. Reflection sets `BuildSite.Position` and `BuildSite.Rotation` (init-only) in the serial input step and calls the engine's
`TownRefit.Reassess` (internal), then `BuildingRegistry.Changed()`
lets the host rebuild the site (`BuildingSync`, "moved or changed: out, then in again"). Engine API: a tick system, building panel buttons,
overlay, key input, frame, settings, translations.

## Missing

- The developer menu (Shift+F12) modes (`EditorAnyBuilding`, `EditorArrows`, `SkipDuringPlacement`, `ApplyChanges`): Meitou has no such
  editor build mode.
- **Unknown**: not checked in game.
