# Organize the Crafting Stations (port)

Port of *Organize the Crafting Stations (MCM fork)* (workshop 3816121254, LEOPARD's fork of Emkej8's original, workshop 3688602277 / Nexus
2219; GPLv3), rebuilt from both DLLs (Ghidra decompile), the fork's README, `mod-config.defaults.json` and `.mo` catalogues.

- **Search panel** on the overview's RESEARCH and CRAFTING tabs (`AddScreenControls("overview", "ResearchTab" / "CraftTab", Inside)`): a `::`
  drag handle, the query box with the hint "Search items...", an `x` clear button inside the box's right end and the "shown / listed" counter.
  Typing filters the blueprint rows (research tab, current category) or the recipe rows (crafting tab, current station) by name; the matches are
  laid out from the top without gaps, scroll and hover as the tab's own rows. The tab's other lists, the queue and the research are untouched.
- **Place**: top-right of the tab page (16 from its right and top); dragging the handle moves it and its release keeps the place (hidden
  settings, as the original's `searchInputLeft` / `Top`), kept 8 inside the page.
- **Keys**: Ctrl+F or / while the panel shows puts the cursor in the box; Ctrl+Shift+F8 turns the controls off and on for the session (the
  key is left to the game and other mods, as Organize the Trader uses it too). Auto-focus puts the cursor in the box when a tab shows.
- **Settings** (Settings, Mods page, the fork's MCM page): Enabled, Show entry count, Show clear button, Auto-focus search (on), Search input
  width (220, 120 to 720), Search input height (26, 22 to 48), Debug logging (off; logs query changes, resets and drags).
- **Translations**: `context.AddTranslations("organize_the_crafting_stations", <installed fork>)` reads the fork's catalogue (en_GB, ru_RU,
  zh_CN) for the settings' labels and hints and the box's hint; nothing of it is shipped. The fork is the required item.

## Facts

- **Verified** (fork DLL decompiled, the scaffold builder and `FUN_180043f40`): panel `OTCS_ControlsContainer` (`Kenshi_GenericTextBoxFlatSkin`)
  width = field width + 50, + 82 with the counter, at most the page less 8, at least 240, height = field height + 16; default place
  (page width - width - 16, 16) (the money-widget top of the trader code finds none here: 16); a kept place is clamped to [8, page - size - 8];
  handle `Kenshi_Button1` "::" at (8, 8, 28, h); counter room = width - 170 (none below 44, at most 72), text box at (width - room - 16, 8),
  resized to max(5 x length + 6, 44, text width + 8) up to the room with its right edge kept; box `Kenshi_EditBox` at (42, 8), width =
  width - 50 - room - 10 (at least 120); hint at (52, 9, box - 16, h); clear `Kenshi_Button1` "x", side h clamped 18..32 (32 above 32), at
  (box - side + 42, (h - side) / 2 + 8). Counter text `{shown} / {total}`. Width clamp 120..720, height 22..48 (config loader).
- **Verified** (decompiled `FUN_180042370`): the parent is the common visible ancestor of `BlueprintsAvailablePanel`, `ResearchQueuePanel`,
  `QueueItemsPanel`, `CraftingStationsList`, i.e. the tab page (`ResearchTab` / `CraftTab` in `Kenshi_OverviewWindow.layout`).
- **Verified** (decompiled filter `FUN_1800357a0`): rows without a name are not counted; the matches take the rows' slots in order and the
  others are hidden; an empty query shows every row. **Observed** (decompiled `FUN_180048ef0`, read in part): query and names are lower-cased
  and runs of non-letter, non-digit characters become one space; the port does that and trims.
- **Verified** (decompile): Ctrl+F (no Alt/Shift) and / (also numpad /) focus the box; Ctrl+Shift+F8 toggles the controls (`DAT_180081ed8`,
  not saved); the query is cleared when the panel's target changes (`target_changed`) and kept otherwise.
- **Verified** (scratch program against the built `MeitouClient.dll`): each patched method has exactly one `Enumerable.ToList` of its row type
  and Harmony inserts the filter after it.
- **Observed**: the original (3688602277, v0.1.0-alpha.2) has the same panel and filter; it differs only in settings through Mod Hub /
  `mod-config.json` (auto-focus off by default), an English-only hint and a counter sized at 5 px a character.
- Harmony: `Meitou.Game.Ui.ProductionWindows.AddResearchContent` and `Meitou.Game.Ui.ProductionWindows.AddCraftContent` (transpilers: the
  sorted row list passes through the mod's filter; a fault shows every row, logged once).

## Port choices

- The query is shared by both tabs and cleared on a change of tab; the original also clears it when its window is rebuilt (**Unknown** when
  Kenshi rebuilds the overview).
- Auto-focus applies each time a tab shows the panel; the original focuses when its controls are injected.
- Ctrl+Shift+F9 (the original's widget-binding diagnostics snapshot) is not ported: the port has no widget binding to diagnose.
- / is the main row's key only: Meitou's key set has no keypad /.

## Missing

- Nothing known. **Unknown**: not checked in game (look, focus, drag).
