# Wanted Map (port)

Port of *Wanted Map (QoL)* (workshop 3813761812): select a character carrying wanted posters (inventory or backpack) and the world map
(`UiRegistration.AddMapMarkers`) marks their targets: red on the spot, orange at a far squad's position ("approx."), yellow at the home town of a
target not seen yet ("lives here"), blue as a prisoner, grey where it died ("dead") for *DeadMarkerHours*. `PosterIndex` links posters to
targets once at start; `WantedSystem` (every 60th tick) finds the targets and remembers deaths and seen targets in the save; `PosterBadge` marks
poster item cells (`UiScreens.ItemCells` screen controls). The original's own files (the override file and the badge pictures) are read from
this mod's folder, else from the installed original (`OriginalFiles`: its Workshop folder beside this one, else the code-less mod named
`WantedMap`); none of them is shipped.

## Facts

- **Posters** are ITEMs with `item function` 13 whose title has a colon (`:` or the full-width `：`); a title prefix counts as a poster prefix
  when at least two such books name a character exactly after it. **Verified** (original DLL (decompiled): the item loop and prefix count;
  `meitou-tools record`: ITEM `63883-Dialogue.mod` "WANTED: Shade", item function 13).
- **Matching**, first rule that finds targets: the override pairs (`WantedMap_overrides.txt`, `poster id | target ids`); the name after the colon
  (CHARACTER or SQUAD_TEMPLATE name); bounty holders (`bounty amount` > 0) named in the `description`; the reward in the description equal to
  a holder's `bounty amount`, sharing a word that is not a stop word. **Observed** (original README); the exact comparisons are Meitou's.
- **Homes**: TOWN reference lists whose names hold `squad`, `resident` or `spawn` name its squads; SQUAD_TEMPLATE lists `leader`, `squad`,
  `squad2`, `slaves` its members. **Verified** (original DLL strings and the lists it compares).
- **Settings** (`WantedMap.defaults.ini`): Enabled 1, AllInventories 0, ShowLabels 1, TownFallback 1, RememberSeen 1, DeadMarkerHours 24 (0
  none), StopWords `the,of,and,wanted,king,boss,leader,из,банды,и,король,лидер,лидеры,глава` (a text setting here, used when the posters are
  matched at start). **Verified**.
- **Badge** on a poster's item cell (inventory, bag, loot): none until a target is dead or captive; `done/total` (colour 1, 0.42, 0.36; box
  3 px in, 18 high, top 20 above the cell's bottom) while some are left; when all are, `wantedmap_captive.png` (any captive) or
  `wantedmap_done.png`, 2/5 of the cell's smaller side held to 14 to 28 px, 2 px from its lower left; no picture, no badge. **Verified**
  (original DLL (decompiled), the colour from its constants). Meitou: counts only posters a player character carries; "dead" is a death seen
  or a target seen before and now gone (RememberSeen); the count's text alignment and font are Meitou's (**Unknown** in the original).
- **Unique states**: a unique CHARACTER target not found in the world counts by the engine's unique table (`World.Uniques`, the state the
  `NPC is` world states test): dead is done, imprisoned is done and captive, and neither gets the home marker. **Observed** (port code); the
  original's exact rule is **Unknown**.
- A target seen before (RememberSeen) and now nowhere counts as dead and gets no marker. **Observed** (original README).
- **Unknown**: not run in game; how many vanilla posters the rules match.

## Missing

- ScanSeconds / WorldScanSeconds (the port scans every 60 ticks), Debug and the runtime table, `WantedMap_GetMarkers` for other plugins.
