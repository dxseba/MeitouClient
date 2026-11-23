# StackSort (port)

Port of *StackSort* (workshop 3813810734). The original's strip `<  StackSort [n]  H/W  >` covers the ARRANGE button of a player character's
inventory and bag windows (`AddScreenControls("inventory" / "bag", "ArrangeButton", ...)`); a press sends the game's `ArrangeInventory`, tagged
with what the button asks, and a prefix on `PlayerSystem.ApplyActionCommand` runs it through StackSort's cycle (in the paused interface step too):
like items grouped, split stacks merged (*Merge stacks*), and the best *Layouts in the cycle* (5) of six candidate layouts (three sort keys,
filled by rows or by columns) offered, ranked for the goal: Height (used rows, top first) or Width (used columns, left first).

- **Strip**: *StackSort [n]* applies the best layout and further presses cycle forward (`n` the shown layout); `<` and `>` step back and forward;
  *H/W* switches the goal and sorts again; Ctrl+press on *StackSort [n]* restores the layout before the first press (the original's right click,
  which screen controls do not receive). A notice shows `StackSort [N] H`. A cycle ends when the grid changes by other means. No fit: vanilla
  arrange. Other callers of `InventoryCommands.Arrange(Inventory)` (a prefix) get the best layout for the *Width goal* setting.
- **The original's files** (a required item, read from its installed folder beside this one): `StackSort.cfg`'s `default_dim` (H),
  `consolidate_stacks` (true) and `top_targets` (5) are the settings' defaults; `arrow_button_width` (30), `dim_button_width` (40),
  `sort_button_width` (180), `button_gap` (2) and `body_x_offset` (-40) lay out the strip; `lang\<code>.txt` gives `SORT_BUTTON` / `DIM_H` /
  `DIM_W` for the game's language (zh_CN / zh_TW, else the code's language part). **Verified** (original cfg, lang files and DLL strings): those
  keys, ranges and defaults, the caption `StackSort [{n}]` with `{n}` the layout number. **Verified** (DLL decompiled): the strip's buttons
  (`Kenshi_Button1`) in the order back, sort, goal, forward, at ARRANGE's top and height, centred on it, shifted by `body_x_offset` for the
  character's own window only; ARRANGE is hidden.
- **Missing**: the original's search (restarted late-acceptance hill climbing, `first_pass_*` / `refine_*` budgets) and its background
  precomputation on open (layouts are computed on the press by this port's heuristic); the right click (Ctrl+press instead); `num_workers`,
  `mutation_debounce_ms`, `resnapshots_per_frame` and `log_level`.
- **Observed** (2026-11-18, windowed sandbox, user play, earlier version on ARRANGE itself): ARRANGE grouped like items; cycling, the goal switch
  and undo work. The strip is not checked in game.
