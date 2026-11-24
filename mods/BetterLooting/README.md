# Better Looting (port)

Port of *Better Looting* (workshop 3816121198, MCM fork release 8; rules and defaults from its GPL-3.0 source and decompiled DLL).

- **Looting key** (a key action, bound on the Controls page, O by default) with a character's inventory window open (its owner as the
  interface picks it: the selected character who opened the loot or container window, else the first selected; key actions never fire while
  a text field has the keys): every dead or unconscious non-player character with items within
  *Loot radius* (20 m = 200 units, 1..100 m, 3D) of that character, nearest first, is looted into it, except bodies one of the player's
  characters carries (`CarriedBy`). Per body: the loose items, the bag's contents and the worn gear (never locked-on gear, fitted limbs,
  bags or death items), in *Best items first* order (value/kg, weightless as value x 10^6, then value, highest first). Rules in the
  original's order: category allowed; stolen skipped unless *Allow stolen items*; unit value >= *Minimum value* (500); above 0.001 kg, value/kg
  >= *Minimum value per kg* (300). Placed into the worn bag then the main grid (*Prefer backpack*), or the reverse; never equipped. Money
  goes into the purse (`PlayerState.Money`, at the record's `value` per unit, "{N} cats added." as the engine's own cash step) and counts as
  one item. A notice says how many items from how many sources.
- **Always take these items**: a text setting (the original's `[Rules] RequiredItems`, at most 4096 characters), read at every press:
  entries split by `;` or `|`; `name:` matches the shown or base name ignoring case, `id:` the record id
  exactly, a bare entry either; at most 128 entries, over 8191 UTF-8 bytes disables it. They skip the category and price rules, never the
  stolen rule, storage protection, radius or room.
- **Categories**: item function first (fcs-mod.md "Item categories": 5 weapons, 6 armour, 3/15 food, 1/2 medicine, 9 tools, 11
  blueprints, 16 ammo, 12 robotics, 13 books, 8 narcotics, 17 severed limbs), an ITEM with `artifact` set counting as a book; then
  materials (an ITEM with function 0, the stand-in for KenshiLib's `getResourceItems`); then the type (WEAPON/CROSSBOW, ARMOUR, BLUEPRINT,
  LIMB_REPLACEMENT robotics); else other. Defaults on but narcotics and severed limbs.
- **Theft**: an item is stolen when `ItemInstance.IsStolenFor` the looter or taking from that body is `CrimeSystem.IsLootTheft`. A take
  that `MarksTheft` marks the item stolen from the body (`StolenMark.Of`) unless it has an owner, and a theft take files the engine's
  Looting crime (`CrimeSystem.Loot`). A body that lost worn gear gets the engine's equipment refresh (`CharacterRefresh.Refresh`, `Equipment`).
- **Alt + looting key** (deposit): the character's loose main-grid and bag items (no worn gear, bags or money), all of them, or with
  *Deposit only by looting rules* (off) those that pass the rules, go into the complete storage buildings of the player's faction within the
  radius whose filter takes them (`MachineData.Accepts`): first the storage the press looted that record from (ORIGIN), then storages limited
  to that item or its record type, then general ones, each nearest first.
- **Session memory** (**Verified**, decompiled DLL: `ORIGIN_PROVENANCE_*`, `AUTODEPOSIT_PROTECT_*`, `AUTODEPOSIT_FILTER`; not saved, as the
  original's): each loot take remembers the units per looter and record and, from a storage machine (*Protect player storage* off), where they
  came from; the deposit's ORIGIN is the first such take with units left (none when that storage is no longer a target: `ORIGIN_REJECT`),
  and each unit stored leaves the takes, first ones first. Each take also protects its units: a new record starts at the count before the
  take, a known one whose count is still above that baseline adds the take, else starts again; while the character holds more than the
  baseline, its stacks of the record, in inventory order (main grid, then bag; **Observed** as the game's list), stay out of the game's
  excess-loot carry-off (`IDitchItemFilter`, `DitchKind.Resources`) until the protected units are used, each stack taking its units (at
  least one); a count back at the baseline clears it (checked each tick).
- **Protect player storage** (on) keeps the player's storage buildings and containers out of looting; off, they are looted too.
  **Pick up ground items** (off) queues up to 32 `PickUpItemOrder`s (`Queued`, `Alt` = *Allow stolen items*) for qualifying ground items among
  the 256 nearest within the radius, nearest to the character first, then nearest to the previous item, never money, death items or a container holding something; another faction's item counts as stolen.
- **Preset slots** (the original's five MCM slots): each a name (free text) and Save / Apply / Clear buttons (`ModSettingKind.Action`) on a
  named preset of the settings group (`SavePreset`, `ApplyPreset`, `DeletePreset`; kept in `meitou.user.json`); Apply leaves the slots' names
  as they are. A refused save or an empty slot shows as the Settings page's notice.
- **Translations**: the installed original's `better_looting` catalogues (`locale/<lang>/LC_MESSAGES`, en_GB, ru_RU, zh_CN;
  `AddTranslations`, folder found as `OriginalFolder`): the setting labels, sub-groups, buttons and the key's label use the original's
  English names, so they translate; hints and notices are the port's own words and stay English.
- **Hooks**: `AddKeyAction` for the key; the request is a paused command (`IPausedCommand`) run by the mod's `LootSystem`
  (`IPausedCommandHandler`), so it applies between ticks while paused. No Harmony or reflection.
- **Verified** (original DLL, decompiled): ini sections, keys and defaults (`[General] LootRadius=20.0`; `[Rules] MinValue=500`,
  `MinValuePerKg=300.0`, `AllowStolen=0`, `RequiredItems`; `[Categories]` all 1 but `Narcotics`, `SeveredLimbs` 0; `[Behavior]
  BestItemsFirst=1`, `PreferBackpack=1`, `ActivationVirtualKey=0x2D` (Insert), `DepositKey=ALT+INSERT`, `DepositByRules=0`,
  `IgnorePlayerStorage=1`, `PickupGroundItems=0`; `[Debug] Log=0`); character targets are non-player, dead or down, not carried by a player
  character, with items; building targets are only the player faction's buildings of function 2, 10 or 13 with items, and none while
  storage protection is on (a building whose furniture parent chain, up to 8, is the player's counts as protected); targets sorted nearest
  first.
- **Debug logging** (*Advanced*, off; the original's `[Debug] Log`): each press writes its steps to the mod log with the original's tags
  (`CONFIG`, `AUDIT_RULES`, `RADIAL_RUN START` / `END`, `RADIAL_TARGET`, a `PLAN` line per judged item with its category, value, weight and
  decision `LOOT` / `SKIP_CATEGORY` / `SKIP_STOLEN` / `SKIP_MIN_VALUE` / `SKIP_VALUE_PER_KG`, `TRANSFER` with `NO_ENGINE_SLOT` for an item with no
  room, `STORE_ALL START` / `MOVE` with its tier `RESOURCE` or `GENERAL` / `NO_DESTINATION`, the deposit summary, `GROUND_PICKUP`). Texts are
  built only while it is on.
- **Emergency spill**: not needed. **Observed** (decompiled DLL): the original stores an item in the nearest player storage (function 2 or 13,
  within the radius, a section that takes it) only after the game refused an item it had found room for and the rollback put it back on the
  body; an item with no room is skipped (`NO_ENGINE_SLOT`) there as here. The port's move finds room and places in one step, so that failure
  cannot happen.
- **Missing**:
  - The original's Insert key (and Alt+Insert): Insert opens the developer console on the Vulkan host and is reserved, so the action defaults
    to O; the deposit is Alt with the same key, not a key of its own.
  - The slots' status words (Empty, Saved, Modified, Incomplete): a settings row has no live caption. The hints and notices in Russian and
    Chinese: the catalogue's hint texts are the author's, not copied.
- **Unknown**: in-play behaviour (window detection, paused application, crime and marks, refresh, pickup orders, the presets page, the
  translations, the carry-off guard) until tested in game.
