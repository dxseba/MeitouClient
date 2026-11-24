# Shop Counter Restock (port)

Port of *ShopCounterRestock* 0.1.7-r10 (mangki, workshop 3826183068, `ShopCounterRestock.dll`), from the original's DLL (Ghidra decompile and
strings) and its shipped `README.md` (the author's notes on the reconstructed build, with the constants it says were recovered from the DLL).

- **Assigning** (`AddJobType` 4311 "Shop counter restock", `AddJobOffer`): a completed player shop counter's right-click menu ends with
  **Restock...** for a selection holding a person. Chosen with Shift it gives the selected workers the job there, turns their Jobs on and
  restocking on (the original's Shift quick assignment); chosen without Shift it opens the settings window, as the original's row. The window's worker choice does the same without turning restocking on.
  One worker per counter (the newest keeps it), one counter per worker. The job shows on the HUD's Jobs panel; its X unassigns, the counter
  keeps its whitelist and targets.
- **Settings window** (the menu row, or *Restock...* on the counter's building panel): the assigned worker (`<` / `>` and Assign, or Unassigned), an
  item search by name or FCS id, "Counter whitelist (target units)" with the counter's stock / target, `Target:`, Add / update, Set target,
  Remove, Enable / Disable restock, Close, and the status line. Esc closes it.
- **The job** (`RestockJob`, an `IJobSource` and the row's act-out): while restocking is on and the worker's Jobs are on, after its orders and
  other jobs (the job is kept last in the list), the worker walks to the counter to deliver what it carries of the short items, else to the
  nearest player storage in the same town or outpost holding a short item it can carry more of. The sweep (`RestockSystem`, every 0.5 s) loads
  or unloads up to 8 units once it is there.
- **Save** (`IModSaveData`): each counter's whitelist (in order), targets and switch, by site id; the job itself is saved by the game
  (`meitou mjob` with the counter's id). Trips and cooldowns are reset on load. Hashed (`IStateHashed`).

## Facts

- **Verified** (decompile, the search's type list): the item search covers ITEM 4, WEAPON 2, ARMOUR 3, CONTAINER 46, CROSSBOW 107,
  LIMB_REPLACEMENT 111, BLUEPRINT 90, MAP_ITEM 102. Targets 1 to 10000 ("Enter a target quantity from 1 to 10000.").
- **Verified** (DLL strings): every status and window text the port shows ("Waiting: ...", "Paused: ...", "Targets met: all whitelisted items
  are stocked.", "Restock assigned. Add items to the whitelist.", "Worker reassigned to another counter.", "Restock assignment removed from
  Jobs panel.", "Missing item", the window's captions), the Jobs entry name "Shop counter restock" and its colour `#afa68b`, the save keys
  `ShopCounterRestock.v1.counters` / `.worker` / `.items` / `.enabled`.
- **Verified** (decompile; each float occurs once in the DLL): 120 is the trip timeout (the worker's clock minus the trip's start); 60 the
  cooldown set on the trip's target when it times out (a counter target pauses restocking instead, "Paused: counter unreachable"); 5000 the
  radius of the `getObjectsWithinSphere` storage search for a counter with no town; 0.01 the squared distance within which a move task (type
  0x1d) is taken for the plugin's own walk. The README's values are right.
- **Verified** (decompile, save and load): the original's save keys are `PlayerInterface::serialise` keys: the handle list
  `ShopCounterRestock.v1.counters`, and per index i the handle `ShopCounterRestock.v1.<i>.worker` and the strings `.items` (the count, a
  newline, then per item `<id length>:<id> <target>` and a newline, in the map's id order) and `.enabled` (`"1"` / `"0"`). **Observed**: the
  interface's record is the save's CAMERA record (it holds the interface's other keys, `selected_characters<k>`), and a handle list is stored
  as `<name><k>` handles as there; not checked against an original-plugin save.
- **Observed** (the shipped README, which states them as recovered from the DLL): update every 0.5 game seconds; at most 8 units per step;
  minimum reach 2; storages are resource or general storage (functions 2 and 13) in the counter's town or outpost, interiors included;
  goods counted in units; carried whitelist goods delivered first; equipment and non-empty containers never moved; main inventory and worn
  backpack; a unit that does not fit is put back; restocking pauses on an unreachable counter; other orders, combat, being down or Jobs off
  suspend it without unassigning.
- **Observed**: builds. Play is **Unknown**: not run in game.

## Port choices

- Shift + right-click straight on the counter (without the menu) is the game's own; the quick assignment is the menu row with Shift.
- The plugin's own-walk test (0.01) has no counterpart: the port knows its walks by its row.
- The worker is picked with `<` / `>` and Assign in place of a combo box; whitelist lines and search results are buttons in scroll lists.
- "Same interior and floor" is a horizontal reach (the building's `use range`, at least 2, doubled once stopped) and at most 15 units of height.
- Same town: the counter's outpost number, else its foreign town (a bought building), else within 5000 units.
- Esc in a text box drops its edit (the engine's text box rule) and does not close the window; a second Esc does.
- The save is the port's own sidecar data. **Original saves**: after loading a save whose CAMERA record holds the original's keys
  (`OriginalSave`), each counter the port's data lacks takes its whitelist, targets and switch; its counter handle is matched to a site by the
  zone files' building states (`handle`), and the saved worker, matched by its character handle, gets the job when it has none. Item ids are
  read by the saved length in characters (exact for ASCII ids).

## Harmony

- `Meitou.Game.Building.BuildMode.GiveOffered` (prefix): the Restock... row without Shift opens the window instead of assigning.

## Missing

- Nothing needs engine changes.
