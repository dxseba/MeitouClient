# StackSort (port)

Port of *StackSort* (workshop 3813810734), simplified: the inventory's ARRANGE (prefixes on both `InventoryCommands.Arrange` overloads; the window's button calls `Arrange(Inventory, bool bag)`) groups items of one record
together, merges split stacks and keeps the candidate layout (by area, height or width) whose used rows end highest, leaving the free space at the
bottom. No fit: vanilla arrange. Not ported: cycling five layouts, H/W goal, undo. **Observed** (2026-11-17): loads. Patching only `Arrange(Inventory)` left the window's ARRANGE vanilla (medkits and stones interleaved); With both patched, ARRANGE grouped like items (**Observed** 2026-11-18, windowed sandbox, user play).
