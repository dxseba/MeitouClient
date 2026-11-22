# Better Looting (port)

Port of *Better Looting* (workshop 3816121198, MCM fork release 8; rules and defaults from its GPL-3.0 source and decompiled DLL).

- **Looting key** (Insert by default; Delete, Home, End, PageUp or PageDown in Mod settings) with a character's inventory window open
  (`InventoryWindows.InventoryOwner`; not while a text field has focus): every dead or unconscious non-player character with items within
  *Loot radius* (20 m = 200 units, 1..100 m, 3D) of that character, nearest first, is looted into it, except bodies one of the player's
  characters carries (`CarriedBy`). Per body: the loose items, the bag's contents and the worn gear (never locked-on gear, fitted limbs,
  bags or death items), in *Best items first* order (value/kg, weightless as value x 10^6, then value, highest first). Rules in the
  original's order: category allowed; stolen skipped unless *Allow stolen*; unit value >= *Minimum value* (500); above 0.001 kg, value/kg
  >= *Minimum value per kg* (300). Placed into the worn bag then the main grid (*Prefer backpack*), or the reverse; never equipped. Money
  goes into the purse (`PlayerSystem.Cash`, by reflection) and counts as one item. A notice says how many items from how many sources.
- **Always take these items**: `[Rules] RequiredItems=` in `BetterLooting.ini` in the mod folder (read at every press; the settings page has
  no free text field): entries split by `;`, `|` or new lines; `name:` matches the shown or base name ignoring case, `id:` the record id
  exactly, a bare entry either; at most 128 entries, over 8191 UTF-8 bytes disables it. They skip the category and price rules, never the
  stolen rule, storage protection, radius or room.
- **Categories**: item function first (fcs-mod.md "Item categories": 5 weapons, 6 armour, 3/15 food, 1/2 medicine, 9 tools, 11
  blueprints, 16 ammo, 12 robotics, 13 books, 8 narcotics, 17 severed limbs), an ITEM with `artifact` set counting as a book; then
  materials (an ITEM with function 0, the stand-in for KenshiLib's `getResourceItems`); then the type (WEAPON/CROSSBOW, ARMOUR, BLUEPRINT,
  LIMB_REPLACEMENT robotics); else other. Defaults on but narcotics and severed limbs.
- **Theft**: an item is stolen when `ItemInstance.IsStolenFor` the looter or taking from that body is `CrimeSystem.IsLootTheft`. A take
  that `MarksTheft` marks the item stolen from the body (`StolenMark.Of`) unless it has an owner, and a theft take files the engine's
  Looting crime (`CrimeSystem.Loot`). A body that lost worn gear gets the engine's equipment refresh (`CharacterAssembly`, reflection).
- **Alt + looting key** (deposit): the character's loose main-grid and bag items (no worn gear, bags or money), all of them, or with
  *Deposit only by looting rules* (off) those that pass the rules, go into the complete storage buildings of the player's faction within the
  radius whose filter takes them (`MachineData.Accepts`): storages limited to that item or its record type first, then general ones, each
  nearest first.
- **Protect player storage** (on) keeps the player's storage buildings and containers out of looting; off, they are looted too.
  **Pick up ground items** (off) queues up to 32 `PickUpItemOrder`s (`Queued`, `Alt` = *Allow stolen*) for qualifying ground items among
  the 256 nearest within the radius, nearest to the character first, then nearest to the previous item, never money, death items or a container holding something; another faction's item counts as stolen.
- **Hooks**: key edge polled in a postfix on `PlayerInterface.BindUi` (rebuilt at least every 50 ms, so a very short tap can be missed);
  the request rides a `SeenDiscoveriesCommand` tagged in a side table, an interface command the paused world applies, and a prefix on
  `PlayerSystem.ApplyViewCommand` runs it instead, so it works paused.
- **Verified** (original DLL, decompiled): ini sections, keys and defaults (`[General] LootRadius=20.0`; `[Rules] MinValue=500`,
  `MinValuePerKg=300.0`, `AllowStolen=0`, `RequiredItems`; `[Categories]` all 1 but `Narcotics`, `SeveredLimbs` 0; `[Behavior]
  BestItemsFirst=1`, `PreferBackpack=1`, `ActivationVirtualKey=0x2D` (Insert), `DepositKey=ALT+INSERT`, `DepositByRules=0`,
  `IgnorePlayerStorage=1`, `PickupGroundItems=0`; `[Debug] Log=0`); character targets are non-player, dead or down, not carried by a player
  character, with items; building targets are only the player faction's buildings of function 2, 10 or 13 with items, and none while
  storage protection is on (a building whose furniture parent chain, up to 8, is the player's counts as protected); targets sorted nearest
  first.
- **Missing**: five MCM preset slots; the original's Russian and Chinese texts; deposit's first tier, the storage an item was taken from
  (the original tracks each item's origin storage); the original's guard keeping the game's own auto-deposit (`getExcessLoot` hook) from
  storing items the character holds; the emergency spill into player storage when full; detailed logging. Insert
  also opens the developer console on the Vulkan host and is reserved for mod key actions (`InputBindings.HostKey`), hence the key option.
- **Unknown**: in-play behaviour (window detection, paused application, crime and marks, refresh, pickup orders) until tested in game.
