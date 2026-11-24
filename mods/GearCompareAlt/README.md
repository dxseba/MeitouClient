# Gear Compare Alt (port)

Port of *Gear Compare Alt (QoL)* (workshop 3812150687), from its plugin (`GearCompare.dll`, Ghidra decompile), its `GearCompare.defaults.ini`,
`README.txt` and `locale/*/LC_MESSAGES/gear_compare.mo`. The original is a required item: at start the port reads its catalogues (English,
Russian, Simplified Chinese), its `GearCompare.ini` (else `GearCompare.defaults.ini`) as the settings' defaults and its
`gui/gfx/gearcompare_fill.png` for the crafting marks; without it the texts stay English and the defaults are the port's (the defaults file's).

- **Hover panel**: hold ALT (setting `HoldKey`: any key, mouse button or chord in the original's syntax, or NONE for always) over armour, a
  weapon or a crossbow in an inventory, a bag, a shop or a container, and a second panel in the game's tooltip look appears beside the item's
  tooltip: "Worn (slot): name", then for every property that differs "worn → new" and the change (new minus worn), in the better or worse
  colour; equal properties are left out. Weight (coloured), price, length and combat weight show but count neither way. Then the main axis
  (Protection (with coverage), Damage (cut + blunt) or Damage (per shot)) in percent of the worn one's, "n better / m worse", the weight
  added, an optional total and the verdict: better, worse, mixed or equal.
- **What it compares with**: armour with what is worn in its slot; a weapon with a worn weapon of the same class (crossbow with crossbow), else
  with the weapon in the slot it would go to (Weapon I, the back slot, first), and when that slot is free with the one in the other slot. An
  empty slot shows no panel unless *Compare with an empty slot* is on. Whose gear: the player character holding the item, else the selected
  one. Hovering the worn item itself shows no panel.
- **Crafting tab**: right-click a line of the Items list: it is marked green (A), a second blue (B), each by the original's tinted fill
  picture, and the panel shows beside the list, "B against A". A marked line's right click unmarks it, a third line replaces B, closing the
  overview drops the marks. A mark follows its recipe, not its line. Armour, crossbows and prosthetics compare at Masterwork; weapons at the
  line's model. Other items take no mark.
- **Settings** (Mod settings, the original's keys, labels and hints; defaults from the original's ini): Enabled, Show while held (`HoldKey`,
  text; its key or chord also binds the Controls row "Show while held [Gear Compare Alt]"), Compare with an empty slot, Compare recipes
  (`CraftCompare`), Total percentage (`ShowTotal`), Per property % (`TotalPerProperty`), Per extra kg % (`TotalPerKg`), Better / Worse
  colours (`auto` or `#RRGGBB`), Detailed log.

## Facts (Verified: decompile of `GearCompare.dll` and the bytes of the Workshop build, unless marked)

- Properties per kind and their print kinds (0 percent, 1 multiplier `x%.2f`, 2 signed `%+d`, 3 weight, 4 integer, 5 `%.2f`) and directions:
  armour cut, blunt resistance (0, +), harpoon resistance (5, +), one `Coverage: <part>` row per part (0, +), acid, gas, burning, dust storm, rain
  protection (0, +), attack, defence, perception, martial arts bonus (2, +), stealth, assassination, athletics, dexterity, dodge, combat speed,
  damage, ranged skills multipliers (1, +), fist injuries (1, -); weapon cut, blunt damage, bleeding (5, +), attack, defence, indoors (2, +);
  crossbow damage `(min + max) x 0.5`, range, shot speed, aim speed, shots (5, +), spread and reload time `(min + max) x 0.5` (5, -); combat
  weight (5, none; weapons and crossbows) and, for every item, weight (3, -) and price (4, none). Rows of kind 3 are never counted, so the
  weight takes a colour but no part in "better / worse". Parsed from the game's tooltip rows: cut resistance efficiency, armour penetration,
  weapon length and damage against robots, humans, animals and each race (multipliers).
- Main axes: armour `sum over covered parts of coverage x (cut + blunt) x 0.5`; weapon cut + blunt; crossbow the per-shot damage.
- Compare step: a row shows when `|new - worn| >= 0.0001`; a property one item lacks reads 1 for a multiplier, else 0; a row with a direction is
  better or worse by the sign of `dir x (new - worn)`. Main percent `(new - worn) / worn x 100` when the worn main is above 0.0001.
- Verdict (`FUN_18000d790`, constants 2, -2, 0.5, -0.5): `d` = better - worse, `p` = main percent. Better when `p > 2` and `d >= 0`; worse when
  `p < -2` and `d < 1`; mixed for any other `|p| > 2`; else better for `d > 0`, worse for `d < 0`, and at `d = 0` by the weight added: worse
  above 0.5 kg, better below -0.5 kg, equal between.
- Total (`ShowTotal`): `(better - worse) x TotalPerProperty + main percent - TotalPerKg x kg added` (constants 3 and 2 as defaults), coloured
  above 2 and below -2. The weight line is coloured outside ±0.05 kg.
- Texts: domain `gear_compare`, catalogues `locale/<en_GB|ru_RU|zh_CN>/LC_MESSAGES/gear_compare.mo` (the panel's words, the settings' labels and
  hints, `worn`, `new`, `change`, `Worn`, `nothing`, the slot names).
- The ini keys and defaults are those of `GearCompare.defaults.ini` (Enabled 1, HoldKey ALT, CompareEmptySlot 0, CraftCompare 1, ShowTotal 0,
  TotalPerProperty 3, TotalPerKg 2, ColourBetter/ColourWorse auto, Debug 0).
- Weapon slots (`FUN_18000b590`): the section `back` is Weapon I, `hip` Weapon II (further weapon sections III and IV after them); the
  label is `Weapon` + " I" etc.
- HoldKey names (its key table): ESC, BACKSPACE, RIGHT, LEFT, DOWN, UP, PAGEDOWN, PAGEUP, END, HOME, DELETE, INSERT, ENTER, TAB, SPACE,
  RSHIFT, LSHIFT, RCTRL, LCTRL, RALT, LALT, SHIFT, CTRL, ALT, X2, X1, MMB, RMB, LMB, joined by `+`; NONE for always.
- Crafting marks (`FUN_180015420`): two ImageBoxes of `gearcompare_fill.png` inside the line, not taking the mouse: the whole line in the mark's
  colour at alpha 0.28 and a bar 4 pixels wide at its left at full alpha; A is (0.36, 0.78, 0.36), B (0.36, 0.62, 0.95).
- Settings file (its README and startup): `GearCompare.ini` in its folder holds the player's settings; missing, it is a copy of
  `GearCompare.defaults.ini`.
- **Observed** (its README): crafting compares armour and limbs at masterwork, weapons at the line's model; the mark follows the recipe.
- **Unknown**: the main axis line's colour thresholds (the port colours by its sign); not checked in game.

## Port choices

- The numbers are computed from the merged data at the item's level (the same formulas the game's tooltip uses), not parsed back out of the
  tooltip's text. Combat weight is the weapon weight Meitou's combat uses (`WeaponStats.Weight`; a crossbow's item weight); price is the
  value the game's tooltip prints for one unit.
- Until the fill picture is loaded (or without the original) a marked line's text takes its colour instead.
- Crafted weapons compare at the model's level of the first manufacturer listing the model, with the neutral manufacturer; prosthetics with
  their multipliers named by the stat's name.
- HoldKey: a key (with modifiers, a chord) becomes the default of a key action, so it is also shown and rebindable on the Controls page
  and changing the setting rebinds it; a modifier alone is read from the frame; L/R sided modifiers count either side; a mouse button binds
  plain with its modifiers tested apart; LMB, RMB, Escape and F6 cannot be a mod action's key and fall back to ALT (logged).
- Settings apply at once (the original's Enabled waits for a restart). The original's ini gives the defaults; the port does not write it
  back (Meitou keeps the player's values in `meitou.user.json`).

## Harmony

- `Meitou.Game.Ui.TooltipRuntime.ItemLines` (postfix): notes the item the tooltip shows.
- `Meitou.Game.Ui.TooltipRuntime.PlaceTooltip` (postfix): places the comparison beside that item's tooltip, through the same method.
- `Meitou.Game.PlayerInterface.MouseDown` (prefix): a right press on a Crafting tab line marks it (taken; any other press goes on).
- `Meitou.Game.Ui.ProductionWindows.AddCraftContent` (postfix): lays the fill pictures under the marked lines and places the A/B panel
  beside the Items list.
- Read by reflection (not patched): `TooltipRuntime.CombatOf`, `PlayerInterface.PricesOf`, `TooltipRuntime.ItemTip.Of`,
  `PlayerInterface.FaithfulScreens`, `UiPainter`, `TooltipRuntime`.
- Each logs once on a failure and leaves the game's tooltip, click or tab as it is; a member not found turns its feature off with a log line.

## Missing

- Nothing that needs an engine change. Not checked in game.
