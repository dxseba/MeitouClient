# StackSort (port)

Port of *StackSort* (workshop 3813810734). The window's ARRANGE (a prefix on `PlayerSystem.ApplyActionCommand` for `ArrangeInventory`)
groups items of one record, merges split stacks (*Merge stacks*) and offers the best *Layouts in the cycle* (5) of six candidate layouts (three
sort keys, filled by rows or by columns), ranked for the goal: Height (used rows, top first) or Width (used columns, left first).

- The original's four controls (`< StackSort [N] H/W >`, right-click undo) cannot be added to the game's button, so they ride on it: click applies
  the best layout and further clicks cycle forward; Shift+click switches H/W; Ctrl+click restores the layout before the first click. The
  modifier is read when the interface says the command (`PlayerInterface.Say` prefix). A notice shows the original's caption, `StackSort [N] H`.
- A cycle ends when the grid changes by other means. No fit: vanilla arrange. Other callers of `InventoryCommands.Arrange(Inventory)` get the
  best layout for the *Width goal* setting.
- **The original's files** (a required item, read from its installed folder beside this one): `StackSort.cfg`'s `default_dim` (H),
  `consolidate_stacks` (true) and `top_targets` (5) are the settings' defaults; `lang\<code>.txt` gives `SORT_BUTTON` / `DIM_H` / `DIM_W` for the
  game's language (zh_CN / zh_TW, else the code's language part). **Verified** (original cfg, lang files and DLL strings): those keys and
  defaults, the caption `StackSort [{n}]` with `{n}` the layout number.
- **Missing**: the original's search (restarted late-acceptance hill climbing, `first_pass_*` / `refine_*` budgets) and its background
  precomputation on open (layouts are computed on the click by this port's heuristic); the `<` back arrow and the button strip (needs a control
  slot on the inventory window); `num_workers`, `mutation_debounce_ms`, `resnapshots_per_frame`, the button geometry keys and `log_level`.
- **Observed** (2026-11-18, windowed sandbox, user play, earlier version): ARRANGE grouped like items; cycling, the goal switch and undo work.
