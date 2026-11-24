# Organize the Trader (port)

Port of *Organize the Trader (MCM fork)* (workshop 3813852189, LEOPARD's fork of Emkej8's original, Nexus 1886; GPLv3), rebuilt from the
fork's DLL (Ghidra decompile), README, Workshop description, `mod-config.defaults.json` and `.mo` catalogues.

- **Search** in the trade window's bottom row, where ARRANGE was (ARRANGE is hidden while the controls show): the query box ("Search
  items...", an `x` clear button inside), then the counter `{shown} / {listed} | {shown units}`. The stock grid shows only the items whose name
  contains the query, packed from its top-left without gaps; a blueprint also matches on its research's name and the names of what it unlocks,
  and a query starting `b:` keeps blueprints only.
- **Sort** at the row's right: "Sort", the preset list (Default, By name, Unit price, Stack value, Unit weight, Stack weight, Price / weight;
  opens upward, Esc closes it) and the direction button (`^` ascending, the default; `v` descending). A preset packs the shown items in its
  order (ties in the trader's order); Default keeps the trader's layout (with a query: the trader's order, packed).
- **Items moved, as the original**: each change of the query or sort sends `OrganizeStock` (the stock's order: the shown items in their order,
  then the hidden ones largest first) and the game's own `ArrangeStock`; `StockOrderSystem` (`SystemAnchor.Before(StandardParts.Trade)`, an
  `IInventoryArrangeFilter`) answers that ARRANGE with the items packed first-fit row by row in that order, or every item kept in place when
  they do not fit. Only commands and the world decide it, so runs repeat (paused too: both are paused commands). Nothing is sent for an empty
  query with Default. Until the move lands, and for the hidden items, screen overrides on the cells move them or leave them out; when the
  shown items do not fit the grid the matches stay where they lie (the original's "search packing skipped"). Prices, money and trade rules
  are unchanged.
- **Header**: "Trader: c.N" in the money plate (in place of the native "Money:" line), "Deal: ..." centred (the trader's money when the window
  opened less its money now: a gain in Good #a8b774 with `+`, a cost in Bad #59231a with `-`) and "Player: c.N" at the right.
- **Layout**: the box takes Search field width (shrunk to fit, at least 120); the preset button Sort list width, or its longest translated
  preset; a row too short drops the "Sort" caption, then puts the sort on a row of its own just above, the stock's scroll view shortened to
  end 2 px above it (Harmony postfix, from the next frame).
- **Keys**: Ctrl+F or / while the window shows puts the cursor in the box; Ctrl+Shift+F8 turns the controls off and on for the session (left to
  the game and other mods). Auto-focus puts the cursor in the box when a trade window opens. A new trade starts with an empty query and a new deal.
- **Settings** (Settings, Mods page, the fork's MCM page and order): Enabled, Show entry count, Show quantity count, Show clear button,
  Auto-focus search (on), Search field width (260, 120 to 600), Sort list width (0 = fit, 0 to 400), Deal balance (on), Debug logging (off).
- **Translations**: `context.AddTranslations("organize_the_trader", <installed fork>)` reads the fork's catalogue (en_GB, ru_RU, zh_CN; e.g.
  English "Sort" shows "Sort by", "By name" shows "Name"); nothing of it is shipped. The fork is the required item.
- **Hooks**: `UiRegistration.AddScreenOverrides("trader", ...)` (rectangles of `Content`, `datapanel`, `scrollview_backpack_content`,
  `ArrangeButton`; the `datapanel-*` money line and ARRANGE hidden; `trade-item-*` cell parts moved or hidden), `AddScreenControls("trader",
  "Content", Inside)`, `AddKeyInput`, `AddFrame` (the open trade, the deal's start), `AddSystem` (`StockOrderSystem`, the arrange filter).
- Harmony: `Meitou.Game.Ui.TradeWindows.BindTraderGeometry` (postfix: shortens `scrollview_backpack_content` for a second row) and
  `Meitou.Game.Ui.ScreenOverrides.Ask` (transpiler: its 512-widget cap becomes 8192 for this mod's own override sources only, every other
  source keeps 512). Both checked with a scratch program against the built `MeitouClient.dll` (one 512 constant; both patches apply). A fault
  keeps the engine's behaviour, logged once.

## Facts

- **Verified** (decompile): presets and their log names (`name`, `unit_price`, `stack_value`, `unit_weight`, `stack_weight`, `value_weight`,
  `default`, `_asc` / `_desc`), the list's captions and order, the direction captions `^` / `v` with ascending the default (`DAT_1800a0fe8` 0);
  the counter `{v} / {t}`, `" | "`, `{units}` by the two count settings; header captions `Trader: ` and `Player: `; ARRANGE hidden and its row
  used; Ctrl+F, / and Ctrl+Shift+F8 as in the crafting fork; defaults Search field width 260 (`DAT_1800a0fa4`), Sort list width 0.
- **Verified** (fork README and Workshop text): the deal = trader's money at opening less now (plus a gain), Good/Bad colours from the
  palette (values: vanilla `kenshi_colours.xml`), "b:" for blueprints, blueprints found by what they unlock, matches packed at the grid's start.
- **Observed** (decompile, read in part): the fork measures captions in the real font, drops the Sort caption, shrinks gaps and fonts and finally
  wraps to two rows reserving room by shrinking the stock viewport; the port measures with `UiFrameContext.Measure` and wraps as above.
- **Observed** (fork README): the fork moves the items in the grid itself, the hidden ones packed into the rest, largest first, and restores
  the cells when a move is refused; the port's identity answer stands for that restore.
- **Unknown**: the original's order within the row (search left, sort right here).

## Port choices

- A second change sent before the first move's stock is published orders by the earlier indices (every item is still placed once); the
  next change corrects it.
- Ctrl+Shift+F9 (the fork's diagnostics snapshot) is not ported.

## Missing

- **Unknown**: not checked in game.
