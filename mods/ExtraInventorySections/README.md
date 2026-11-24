# Extra Inventory Sections (port)

Port of *Extra Inventory Sections* (Genpretz, Nexus 1838), from the original's DLL (`Extra Inventory Sections.dll`, Ghidra decompile) and
files, on the engine's mod equipment slots (`ModContext.AddEquipmentSlot`, [docs/modding.md](../../docs/modding.md) "Equipment slots").

- **Sections**, named and sized as the original's (so saves keep its names): `eyes_eyes` 3x1 for ARMOUR/ITEM records of FCS
  slot ATTACH_EYES (4); `eyes_belts` 3x1 and `eyes_hats` 3x1, second belt (14) and hat (3) sections (`Secondary`: filled only by a drag or
  the loot window, while picking up, equipping and NPC kits keep using the vanilla `belt` and `head`); `gloves` 2x2 for ATTACH_GLOVES (10),
  up to four items (`MaxItems = 4`); `neck` 2x2 for ATTACH_NECK (11). Each is worn gear like a vanilla clothing slot (armour stats, look,
  trade, looting, locks); animals get none.
- **NPC gear**: the eyes, hands and neck sections are `RollFromClothing`: a generated character rolls one more item each for slots 4, 10 and
  11 from its CHARACTER `clothing` list after the game's slots, in that order, as the original's `chooseMyClothing` hook does.
- **Window** (setting *Original's window*, on): the installed original's `gui/layout/Custom_InventoryCharacterWindow.layout` (the mod "Extra
  Inventory Sections Plugin" in Kenshi's mods, found by `ModList.Discover`; nothing of it is shipped) replaces
  `Kenshi_InventoryCharacterWindow.layout` (`AddLayoutReplacement`) for the character and loot windows, so the window is the original's
  taller, reflowed one, each section at its widget and the layout's own captions. Without the original, or with the setting off, the vanilla
  window with the sections at the ten *X*/*Y* settings (defaults: the free band between the vanilla data panel and the main grid's title,
  second belt and hat stacked under the eyes) and, with *Section labels* (on), "EYES", "HANDS", "NECK" centred above eyes, hands and neck
  (`AddScreenControls` `Above` the section's widget; font `Kenshi_PaintedTextFont_Medium` in the `Kenshi_TextboxPaintedText` template's
  colour; with the layout replaced it carries them). Restart after a change.

## Facts (Verified: decompile of `Extra Inventory Sections.dll`, `startPlugin` and its hooks, and its data table, unless marked)

- `Character::_NV_init` hook: after the game's init, every character's inventory (characters, animals included) gets five sections unless it
  has them, by `Inventory::_NV_initialiseNewSection(name, w, h, slot, true, false, true, n)`: `eyes_eyes` 3x1 slot 4, `eyes_belts` 3x1 slot 14
  (ATTACH_BELT), `eyes_hats` 3x1 slot 3 (ATTACH_HAT), `neck` 2x2 slot 11, `gloves` 2x2 slot 10. The last argument is 4 for `gloves`, 1 for
  the others (**Observed**: taken as the most items a section holds, matching the Nexus text's four items for hands). An existing section of
  another size is resized (`Inventory::resizeSection`) with a log line.
- `Inventory::getSectionOfType` hook: for slot 3 it answers the vanilla `head` section and for 14 the vanilla `belt` when they exist, so
  automatic placement keeps using the vanilla hat and belt sections; `eyes_hats` and `eyes_belts` are second hat and belt sections filled by
  hand.
- `RootObjectFactory::chooseMyClothing` hook: for the CHARACTER list `clothing` only, after the game's picks it picks one more item each for
  slots 4, 10 and 11 with the game's `_chooseClothingItemFromList` and adds it, so generated NPCs wear eyes, hands and neck items their
  clothing lists hold.
- `wraps::BaseLayout::initialise` hook: `Kenshi_InventoryCharacterWindow.layout` is loaded as the mod's `Custom_InventoryCharacterWindow.layout`
  (Verified: file). That layout makes the window 0.5907 (vanilla 0.5723) of the screen high, reflows the vanilla widgets (the data panel
  shorter), drops their own text colours, and adds `eyes_eyes`, `eyes_belts`, `eyes_hats` stacked under one "EYES" caption, `gloves`
  ("HANDS") and `neck` ("NECK") between the data panel and the main grid. The animal window is not replaced. The `.mod` has no records.

## Missing

- Nothing of the original's behaviour. In a save made with this port's former section names (`eyes`, `hands`) those items are moved into
  the character's grids on load ([save](../../docs/formats/save.md)); wear them again. Vanilla data has few or no items of these slots: the
  sections are for item mods.
- **Unknown**: not checked in game: the replaced window, the four-item hands section, the secondary sections and NPC rolls; and whether the
  fallback places clear the vanilla window's widgets.
