# Extra Inventory Sections (partial port)

Port of *Extra Inventory Sections* (Genpretz, Nexus 1838), rebuilt from its Nexus description, on the engine's mod equipment slots
(`ModContext.AddEquipmentSlot`, [docs/modding.md](../../docs/modding.md) "Equipment slots").

- **Sections**: `eyes` 3x1 cells for ARMOUR/ITEM records with FCS slot ATTACH_EYES (4); `hands` 2x2 for ATTACH_GLOVES (10); `neck` 2x2 for
  ATTACH_NECK (11). Each is worn gear like a vanilla clothing slot (armour stats, look, trade, looting, locks); animals get none.
- **Places**: the six *X*/*Y* settings, in layout pixels from the character window's top-left corner at UI scale 1, read at start (restart after a
  change). Defaults put the three in the window's top right, above the main grid.
- **Labels** (setting *Section labels*, on; restart to apply): "Eyes", "Hands", "Neck" just above each section in the character and loot
  windows (`AddScreenControls` anchored `Above` the section's widget, which the engine names by the section; `AddFrame` for the UI scale).
- **Missing** (partial): the Eyes section's ATTACH_HAT and ATTACH_BELT cells (those FCS slots map to the vanilla head and belt sections; Meitou
  refuses them), the Hands section holding up to four items (a mod slot holds one), translations of the labels. Vanilla data has few or
  no items of these slots: the sections are for item mods.
- **Unknown**: not checked in game, including whether the default places and the labels above them are free of the window's own widgets, and the original's label
  texts and look.
