# Wanted Map (port)

Port of *Wanted Map (QoL)* (workshop 3813761812): select a character carrying wanted posters (inventory or backpack) and the world map
(`UiRegistration.AddMapMarkers`) marks their targets: red on the spot, orange at a far squad's position ("approx."), yellow at the home town of a
target not seen yet ("lives here"), blue as a prisoner, grey where it died ("dead") for *DeadMarkerHours*. `PosterIndex` links posters to
targets once at start; `WantedSystem` (every *ScanSeconds*, a world search every *WorldScanSeconds*, `TargetFinder`) finds the targets and remembers deaths and seen targets in the save; `PosterBadge` marks
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
  none), ScanSeconds 2, WorldScanSeconds 30, Debug 0, StopWords `the,of,and,wanted,king,boss,leader,из,банды,и,король,лидер,лидеры,глава`
  (a text setting here, used when the posters are matched at start). **Verified**.
- **Translations**: the installed original's `wanted_map` catalogues (en_GB, ru_RU, zh_CN; `context.AddTranslations`, read from its folder,
  not shipped) translate the settings' labels and the hints that keep the original's English, and the markers' state words (dead,
  imprisoned, approx., lives here) through `context.Tr`. **Verified** (installed `.mo` files: those messages, en_GB identity). Hints the port
  words differently because its behaviour differs (RememberSeen kept in the save, both scan intervals on game time whether the map is open
  or not, Debug to the mod log) stay English; the tooltip's "Wanted" / "State" go to the game's catalogue. Shown in game **Unknown**.
- **Badge** on a poster's item cell (inventory, bag, loot): none until a target is dead or captive; `done/total` (colour 1, 0.42, 0.36; box
  3 px in, 18 high, top 20 above the cell's bottom) while some are left; when all are, `wantedmap_captive.png` (any captive) or
  `wantedmap_done.png`, 2/5 of the cell's smaller side held to 14 to 28 px, 2 px from its lower left; no picture, no badge. **Verified**
  (original DLL (decompiled), the colour from its constants). Meitou: counts only posters a player character carries; "dead" is a death seen
  or a target seen before and now gone (RememberSeen); the count's text alignment and font are Meitou's (**Unknown** in the original).
- **Unique states**: a unique CHARACTER target not found in the world counts by the engine's unique table (`World.Uniques`, the state the
  `NPC is` world states test): dead is done, imprisoned is done and captive, and neither gets the home marker. **Observed** (port code); the
  original's exact rule is **Unknown**.
- A target seen before (RememberSeen) and now nowhere counts as dead and gets no marker. **Observed** (original README).
- **Scans** (`ScanSeconds` 2, `WorldScanSeconds` 30): the posters are checked and the markers placed every ScanSeconds; every WorldScanSeconds
  the targets are searched for in the whole world, and between searches only the found ones are followed (a target not found stays so until
  the next search). **Observed** (original ini comments). Meitou: game seconds, and the scans run whether the map is open or not (the
  original's while it is open).
- **Debug** (`Debug` 0): the poster-to-target table at start (the original's `WantedMap_runtime_table.txt`) and each world search's markers,
  to the mod log instead of the files. **Observed** (ini comment); the line format is Meitou's.
- **Markers for other mods** (the original's `WantedMap_GetMarkers` export: position, colour, a state number, a 96-character label;
  **Verified**, DLL export (decompiled)): `WantedMapMod.GetMarkers()` gives the markers the map showed last (`WantedMapMarker`: map point,
  colour, `WantedState`, target record id, label) to a mod that declares `dxseba.wantedmap` a dependency. The original's state number's
  meaning is **Unknown**; `WantedState` is Meitou's.
- **Unknown**: not run in game; how many vanilla posters the rules match.

## Missing

- Nothing the port alone can add. The scan intervals run on game time (see Scans).
