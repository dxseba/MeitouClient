# Transmog (port)

Port of GHOST's *GHOSTs Transmog* (Workshop 3841482484, RE_Kenshi plugin `Transmog.dll` 0.8.0), from its DLL (Ghidra decompile), its
`locales/*.json` and its Workshop description. The original is a required item: its texts and icon are read from it at runtime.

- **Workbench** (`UiSlots.Overlay` window; opened by a button left of ARRANGE in a player character's inventory, `AddScreenControls`, the
  original's `ghosts_transmog_icon.png` when installed): Transmog on/off (and Weapon transmog on/off with *Enable independent weapon
  transmog*), the slots Head, Shirt, Armor, Pants, Boots, Belt, Backpack, the four limbs and Inventory weapon 1 and 2, a slot's appearances
  (up to 32; a limb or weapon slot has one), Clear slot, Copy equipped gear, Clear all gear, Copy to selected characters (the outfit sent to
  every selected player character).
- **Lists**: Equipment (the items that fit the slot: ARMOUR of its section, CONTAINER bags, the LIMB_REPLACEMENTs of that limb plus the natural
  limb, WEAPON and CROSSBOW records), Favorites (a star on each row) and Presets (name, Add preset, Apply, Delete), with search. Item rows and
  appearances show the game's own inventory icon (a saved icon file, or the renderer's picture of the mesh once ready; the first 60 rows).
  Favorites and presets are kept across saves in `%LOCALAPPDATA%\Meitou\Transmog\favorites.json` and `presets.json` (host thread only).
- **Look** (Harmony postfix on `World.FillIdentity`): with the transmog on, the published look is rebuilt from the character's own loadout with
  each set slot's item swapped for its first appearance; further appearances are laid over from looks built with that item alone (their parts,
  body layers and hidden body regions). Weapons: WEAPON I and II dress the first and second carried weapon (hip, back, then crossbow) at its
  place; *Enable rendering* off hides it; *Use original weapon material* keeps the real weapon's manufacturer and model, else a chosen
  manufacturer's first model. Built once per change of the own look, the outfit or the settings. The character, its inventory, stats and trades
  keep the real items; nothing is hashed.
- **Dye**: Colour 1 and Colour 2 as `#RRGGBB`, a colour picker (18 hues at five shades and nine greys; the channel chosen with its label), or
  stepping through the game's COLOR_DATA presets, Reset colour. A dye is a COLOR_DATA made in memory (never added to the data) set on the
  appearance's parts and body layers; it shows on materials with a `color map`.
- **Model offset**: position (0.05 steps), rotation (5 degree steps) about x, y, z and scale (0.05 to 5), Reset Offset, Copy/Paste of an
  appearance's dye and offset. A rigid part (weapons, attachments) takes it as its `CharacterPart.Local`. A skinned head piece (hat and eyes
  slots) is redrawn on the head bone (placed by the inverse of the head's binding pose, then the offset in the head's frame), so it follows the
  head and shows in portraits. Other skinned gear takes it in the character's frame through the renderer patch below.
- **Settings** (Mod settings): *Only replace configured equipment slots*, *Always show dye controls*, *Enable independent weapon transmog*,
  *Debug mode* (a Spawn button on each item row: a real item through the game's `DebugGiveItem`).
- **Save**: the outfits are the system's `IModSaveData`, keyed by lasting ids; changes are `IPausedCommand`s, so they apply while paused.
- **Texts**: each `locales/en_GB.json` text paired with the game language's file and added as the mod's translations.

## Facts

- **Verified** (decompile strings): slot table `shirt`, `head`, `armour`, `legs`, `boots`, `belt`, `backpack_attach`, `limb0`-`limb3`, plus
  weapon slots `WEAPON I`/`WEAPON II`; settings file keys `independentWeapons`, `selectedSlotsOnly`, `showAllDyeControls`, `debugSpawn`; layer
  limit message "32 appearances"; it keeps `presets.json`, `favorites.json` and `settings.json` under `%LOCALAPPDATA%\Kenshi\Transmog`, and a
  per-save snapshot by a save fingerprint; it hooks `AppearanceBase::attachItem/detachItem/createAttachedObject/updateHiddenParts`.
- **Verified** (`locales/en_GB.json`): the interface texts the port uses.
- **Unknown**: the original's setting defaults, its offset ranges and steps (the port's are its own), its exact dye application per material,
  its colour picker's layout, and its favorites and presets file formats (the port's are its own; the original's files are not read).

## Harmony

- Harmony: `Meitou.Simulation.World.FillIdentity` (postfix): publishes the transmog look.
- Harmony: `Meitou.Game.Rendering.Characters.CharacterContent.AddItemParts` (prefix and postfix): the prefix moves adjusted skinned head
  pieces onto the head bone (the look's parts and a `dxseba.transmog:head` attachment point, once per look, locked); the postfix notes the
  asset parts of the other adjusted skinned appearances (paired with the look's parts by the part label).
- Harmony: `Meitou.Game.Rendering.Characters.CharacterRenderer.PoseLive` (postfix): multiplies those parts' placed `_partFrames` matrices.
- Harmony: `Meitou.Game.Ui.UiHost.Control` (postfix): gives the workbench's preview image nodes the renderer's item picture texture.
- Reflection, no patch: `CharacterSnapshot.Appearance` setter, `GameRecord`'s internal constructor and `IntTable`, `CharacterAppearance.HiddenParts`
  setter, the renderer's `_live`, `_liveCount`, `_partFrames` and `AssetPart.Label`, `ModFrame._host` to `GameHost.Player`, its `UiPainter` and
  `ItemCells.ItemIcons`. Each patch body is wrapped: a fault is logged once and the vanilla look (or a name without a picture) is drawn.

## Port choices

- The button sits left of ARRANGE (the original's: the inventory's top-left corner); text-only without the original.
- Unset limb slots keep the real limbs whatever *Only replace configured equipment slots* says.
- Skinned gear other than head pieces (shirts, armour, bags, limbs) is offset in the character's frame, as it bends with several bones.
- A head piece moved onto the head bone ignores the head bone's body-shape scale.

## Missing

- Co-op capture (the original's Shared Wastelands support): Meitou has no co-op.
- Offsets of skinned non-head gear in portraits: the portrait path places its parts inside `CharacterRenderer` with no seam a prefix or
  postfix reaches.
- Not checked in game.
