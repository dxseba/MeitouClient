# StackSort (port)

Port of *StackSort* (workshop 3813810734). The window's ARRANGE (a prefix on `PlayerSystem.ApplyActionCommand` for `ArrangeInventory`)
groups items of one record, merges split stacks and offers the five best of six candidate layouts (three sort keys, filled by rows or by
columns), ranked for the goal: Height (used rows, top first) or Width (used columns, left first).

- The original's four controls (`< StackSort N H/W >`, right-click undo) cannot be added to the game's button, so they ride on it: click applies
  the best layout and further clicks cycle the five; Shift+click switches H/W; Ctrl+click restores the layout before the first click. The
  modifier is read when the interface says the command (`PlayerInterface.Say` prefix). A notice shows "StackSort N/M (H)".
- A cycle ends when the grid changes by other means. No fit: vanilla arrange. Other callers of `InventoryCommands.Arrange(Inventory)` get the
  best layout for the *Width goal* setting.
- Not ported: background precomputation (layouts are computed on the click) and `StackSort.cfg`.
- **Observed** (2026-11-18, windowed sandbox, user play): before the cycle, ARRANGE grouped like items. **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play):
  cycling, the goal switch and undo work.
