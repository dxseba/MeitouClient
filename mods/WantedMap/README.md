# Wanted Map (port)

Port of *Wanted Map (QoL)* (workshop 3813761812): select a character carrying wanted posters (inventory or backpack) and the world map
(`UiRegistration.AddMapMarkers`) marks their targets: red on the spot, orange at a far squad's position ("approx."), yellow at the home town of a
target not seen yet ("lives here"), blue as a prisoner, grey where it died ("dead") for *DeadMarkerHours*. `PosterIndex` links posters to
targets once at start; `WantedSystem` (every 60th tick) finds the targets and remembers deaths and seen targets in the save.

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
  none), StopWords `the,of,and,wanted,king,boss,leader,из,банды,и,король,лидер,лидеры,глава` (read at start from a `WantedMap.ini` in the mod folder, as text is not a mod setting). **Verified**.
- A target seen before (RememberSeen) and now nowhere counts as dead and gets no marker. **Observed** (original README).
- **Unknown**: not run in game; how many vanilla posters the rules match.

## Missing

- The original's `WantedMap_overrides.txt` (its data, not shipped): put one in this mod's folder to use it.
- World states (`WORLD_EVENT_STATE` `NPC is`) as a source of dead / alive / imprisoned.
- The poster badge in the inventory (green tick, lock, `2/3`): needs an item-cell overlay hook and the original's art.
- ScanSeconds / WorldScanSeconds (the port scans every 60 ticks), Debug and the runtime table, `WantedMap_GetMarkers` for other plugins.
