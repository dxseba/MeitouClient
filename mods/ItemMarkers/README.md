# Item Markers (port)

Port of *Item Markers (QoL)* (Workshop 3816608451, Nexus 2224), from the original's DLL (`ItemMarkers.dll` v7, Ghidra decompile), its
`ItemMarkers.defaults.ini`, `README.txt` and `sdk/`.

Marks on item icons in every item grid (inventory, bags, limbs, loot, trader, containers and storages, equipment slots, the held item):

- **Grade colour** of armour, weapons, crossbows and limbs, as in the item tooltip, from `From grade` up (default 2, blue). Styles: frame (four
  glow strips, default), fill, gradient from the bottom, star in a chosen corner. Frame, fill and gradient lie under the item's picture, the star
  over it.
- **Stolen**: the game's steal pointer (`pic_PointerSteal`) in the top left; **Faction uniform**: the game's hand pointer (`pic_PointerHand`)
  after it.
- **Eye** in the lower left of a learned blueprint, and of a location map whose listed places are all discovered.
- **Trade bag** in the top right of an item that changed hands in the open trade: gold sold, green bought.
- **Meitou** class weapons take `ItemGradeMeitou` (else `ItemGrade5`, else grade 4's) on the icon and in their tooltip's grade lines.
- **Custom grades**: `grades/*.ini` profiles (this mod's folder and the original's) and code mods through `ItemMarkersApi.RegisterProvider`.
- **Show key** (`AddKeyAction`, unbound): bound, the marks show only while it is held; unbound, always.

The installed original is a required item: its pictures (`gui/gfx/itemmarkers_*.png`) and catalogues (`item_markers`: en_GB, ru_RU, zh_CN)
are read from its folder. Without it the texts are English, frame, fill and star are plain colour, the gradient is a fill, the bag is the steal
pointer tinted, and the eye is left out.

## Facts (Verified: decompile of `ItemMarkers.dll`, its marking, grade, settings and profile code, unless marked)

- **Settings** (MCM page, ini keys, defaults, ranges, labels and hints as the port's): Enabled 1; HoldKey NONE; Rarity 1; RarityStyle 1 (0 fill,
  1 frame, 2 gradient, 3 star); StarCorner 2 (0 top left, 1 top right, 2 bottom left, 3 bottom right); RarityStrength 6 (1-10); MinGrade 2 (0-5);
  Stolen 1, StolenSize 16 (8-40), StolenStrength 9 (1-10); TradeBags 1, TradeSoldColour `#e8b830`, TradeBoughtColour `#5fd35f`; Faction 1,
  FactionSize 16 (8-40), FactionStrength 9; Blueprint 1; MapsKnown 1; a "Reload custom grade rules" button; Debug 0.
- **Graded** (`FUN_180009bd0`): item type ARMOUR (3) with `Armour::hasArmourCoverage`, WEAPON (2), CROSSBOW (107), LIMB_REPLACEMENT (111).
  The port reads coverage as any ARMOUR `part coverage` entry above 0 (**Observed**: equivalent assumed, not compared).
- **Grade** (`FUN_180009c30`) of the item's level (`+0x224`): below 16 → 0, 36 → 1, 51 → 2, 66 → 3, up to 80 → 4, above → 5. Colours: the palette's
  `ItemGrade%d`; the vanilla palette has no `ItemGrade5` (**Verified**, `data/gui/colours/kenshi_colours.xml`), which falls back to grade 4.
- **Meitou** (`FUN_180009ce0`): the item's material record id is `52293-rebirth.mod`; colour `ItemGradeMeitou`, else grade 5's. The port compares
  the cell's material (**Observed**: assumed to be the weapon's model record in Meitou too).
- **Layout** (`FUN_18000b3c0`), pixels in the cell (the port multiplies the fixed sizes by the interface scale):
  - frame: strips of `clamp(min(w, h) / 4, 6, 14)` along each edge, `itemmarkers_glow_{top,bottom,left,right}.png`, alpha strength/10;
  - fill: `itemmarkers_fill.png` over the cell at alpha strength/10 × 0.5 (the 0.5 a float constant at `0x180023740`); gradient:
    `itemmarkers_gradient.png` at strength/10;
  - star: `itemmarkers_star.png`, 16 px on a cell narrower than 80, else 20, 2 px in from its corner, not faded;
  - badges from x = 2 (star size + 4 when the star sits top left), y = 2: stolen (StolenSize, alpha StolenStrength/10), then 1 px on, uniform
    (FactionSize, FactionStrength/10);
  - trade bag: StolenSize, at x = w − (2, or star size + 4 when the star sits top right) − size, y = 2, `itemmarkers_bag.png` tinted (without it
    `pic_PointerSteal`);
  - eye: `itemmarkers_known.png`, `clamp(min(w, h) × 2 / 5, 14, 28)`, at (2, h − size − 2).
  - Grade marks at depth 10000 (under the icon's picture), the star and badges at −10000 (over it).
- **Stolen**: `InventoryItemBase::isStolen(true)`; the port uses the cell's stolen mark for its holder (`StolenFrom`).
- **Uniform**: the item's vfunc `0x2f8` (non-null); the port reads `ItemInstance.Uniform` (**Observed**: assumed the same fact).
- **Learned blueprint** (`FUN_18000aed0`): type BLUEPRINT (90) whose research `Research::isFinished` or `hasPaidFor`. The port uses
  `ResearchSnapshot.Known` (finished, or requirements met: Meitou's blueprint tooltip state) for the research the blueprint carries as its company.
  **Unknown**: whether "paid for" and Meitou's "requirements met" always agree.
- **Known map** (`FUN_18000b120`): type MAP_ITEM (102) with a non-empty `towns` list of at most 256, each a TOWN (13) record found discovered.
  **Unknown**: how the original treats a town placed more than once; the port takes its first placed site.
- **Trade bag** (`FUN_180009d80`): `InventoryItemBase::getBuyBackGUIInfo` gives rows (the item shows "Buyback"); bought when the item's holder
  chain (at most 4 levels) reaches a player character, else sold. The port reads the trade view's `Buyback`: a stock item sold, a player's item
  bought.
- **Profiles** (profile loader and `sdk/README.txt`): sections `[Profile]` (`Enabled` 0/1), `[Grade.name]` (`Rank` 0-5, `Colour` `#RRGGBB`,
  optional `Palette`), `[Rule.name]` (`ItemID`, `MaterialID`, `Type` AND-ed, one at least; `LevelMin`/`LevelMax` gear only; `Priority`
  ±10000; `Grade`); limits 64 files of 64 KiB, 128 grades per file, 512 rules, 511-byte lines, 127-byte ids, 63-byte grade names; any bad file
  rejects the reload and keeps the previous rules; higher priority first, ties by file name then section order. **Observed**: the accepted
  palette roles (the port allows `ItemGrade0`-`5` and `ItemGradeMeitou`).
- **Providers** (`sdk/ItemMarkersApi.h`): asked before profiles, then the game's grades; highest priority first, ties in registration order; at
  most 32, unique names; a fault or invalid answer disables the provider. The port's C# form is `ItemMarkersApi` (a dependency on
  `dxseba.itemmarkers`); its `notify_changed` is not needed as marks are worked out each time a cell is laid out.

## Harmony

- `Meitou.Game.Ui.ItemCells.AddItemCell` postfix: inserts the grade marks after the cell's background and adds the star and badges after its
  controls, for every item grid. The screen-control API (`UiScreens.ItemCells`) builds only for character windows and draws over the picture.
- `Meitou.Simulation.World.ItemViewOf`, `Meitou.Simulation.Production.MachineSnapshot.StorageView` and
  `Meitou.Simulation.Trading.TradeSystem.StockViews` postfixes: note, as the world publishes, which item views are a uniform (a weak table on the
  view; display only, nothing feeds the world). The snapshot's views carry no uniform.
- `Meitou.Game.Ui.TooltipRuntime.ItemLines` postfix: the Meitou colour on a Meitou weapon's grade-tone tooltip rows.
- Each postfix catches its faults, logs once and leaves its part off; a patch Harmony refuses leaves that part off too.

## Port choices

- Marks are recomputed each time a cell is laid out instead of once per icon; the original's 0.4 s refresh of visible icons is not needed.
- "Enabled" applies at once (the original's needs a restart).
- The show key is a Controls-page key action instead of the ini's key text.
- The detailed log lists the first 200 marked records (kind, level, grade, stolen, uniform, marks); it has no marking timings.
- An open town container's items get no uniform badge (its view is made inline in `World.OpenContainerView`).

## Missing

- **Unknown**: not checked in game.
