# Better Looting (port)

Port of *Better Looting* (workshop 3816121198, MCM fork; rules and defaults from its GPL-3.0 source, `BetterLootingRules.h`,
`BetterLooting.cpp`).

- **Insert** with a character's inventory window open (`InventoryWindows.InventoryOwner`; not while a text field has focus): every
  dead or unconscious non-player character with items within *Loot radius* (20 m = 200 units, 1..100 m, 3D) of that character, nearest
  first, is looted into it. Per body, the loose items, the bag's contents and the worn gear (never locked-on gear, fitted limbs, bags or
  death items), in *Best items first* order (value/kg, weightless as value x 10^6, then value, highest first). Rules in the original's
  order: category allowed; stolen skipped unless *Allow stolen*; unit value >= *Minimum value* (500); above 0.001 kg, value/kg >=
  *Minimum value per kg* (300). Placed straight into the worn bag then the main grid (*Prefer backpack*), or the reverse; never equipped.
  Money goes into the purse (`PlayerSystem.Cash`, by reflection) and counts as one item. A notice says how many items from how many sources.
- **Categories**: item function first (fcs-mod.md "Item categories": 5 weapons, 6 armour, 3/15 food, 1/2 medicine, 9 tools, 11
  blueprints, 16 ammo, 12 robotics, 13 books, 8 narcotics, 17 severed limbs), an ITEM with `artifact` set counting as a book; then
  materials (an ITEM with function 0, the stand-in for KenshiLib's `getResourceItems`); then the type (WEAPON/CROSSBOW, ARMOUR, BLUEPRINT,
  LIMB_REPLACEMENT robotics); else other. Defaults on but narcotics and severed limbs.
- **Theft**: an item is stolen when `ItemInstance.IsStolenFor` the looter or taking from that body is `CrimeSystem.IsLootTheft`. A take
  that `MarksTheft` marks the item stolen from the body (`StolenMark.Of`) unless it has an owner, and a theft take files the engine's
  Looting crime (`CrimeSystem.Loot`). A body that lost worn gear gets the engine's equipment refresh (`CharacterAssembly`, reflection).
- **Alt+Insert** (Store All): the lead's loose main-grid and bag items (no gear, bags or money) that pass the same rules go into the
  nearest complete storage buildings of the player's faction within the radius whose filter takes them (`MachineData.Accepts`).
- **Fork extras**: *Protect player storage* (on) keeps the player's storage buildings and containers out of Insert; off, they are looted
  too (the original's only container targets). *Ground pickup* (off) queues up to 32 `PickUpItemOrder`s (`Queued`, `Alt` = *Allow
  stolen*) for qualifying ground items within the radius, nearest first; another faction's item counts as stolen.
- **Hooks**: key edge polled in a postfix on `PlayerInterface.BindUi` (rebuilt at least every 50 ms, so a very short tap can be missed);
  the request rides a `SeenDiscoveriesCommand` tagged in a side table, an interface command the paused world applies, and a prefix on
  `PlayerSystem.ApplyViewCommand` runs it instead, so it works paused.
- Differences: instant transfer, no walk; no in-game settings window, presets or Spanish text; the original treats everything on a body as
  stolen (any downed non-player source), here only what the crime rules call theft; the emergency spill into player storage when full is
  not ported; death items (butchering) are left; Insert is not swallowed if something else binds it.
- **Unknown**: all in-play behaviour (detection of the open window, paused application, crime and marks, refresh, pickup orders) until
  tested in game.
