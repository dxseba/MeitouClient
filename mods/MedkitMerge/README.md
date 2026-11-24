# Medkit Merge (port)

Port of *Medkit & Bolt Merge* v0.5.2 (Workshop 3803535940), from the shipped DLL (Ghidra decompile), its README and its diagnostics log.
Partial first-aid kits, splint kits, robotics repair kits and crossbow bolt packs of one kind are poured together in a player character's
inventory and the containers in it (the worn backpack included): after the Sort button, and when the character receives one (picked up,
traded by a squad mate, bought). No supplies are made or lost; the receiving item keeps its owner and stolen mark. No settings (the original
has none). No Harmony.

## Facts (Verified: decompile of `MedkitMerge.dll` 0.5.2, unless marked)

- **Items**: `item function` 1 (first aid), 2 (splints), 12 (robot repair) or 16 (ammo); quantity 1; not worn; not unique; finite charges;
  capacity (`max charges`) above 0; charges above 0 and below the capacity. Bolt counts and capacities must be whole numbers from 0 to 2^24.
- **Match**: same function, same record, same quality, same capacity; owner and stolen mark ignored.
- **Where**: the character's own inventory, then each container in it (the worn backpack among them) depth first, at most 4 deep, items in
  order.
- **Pour** (one donor): kits fill the matching items in that order, each taking min(what the donor has left, capacity - charges). Bolt packs
  fill the fullest first (ties: the earlier pack, by address in the original, by place here), and only a pack holding at least as many as the
  donor still has (an equal one only when it comes first). An emptied donor is removed (the removal refused rolls the pour back). 50 + 20 of
  100 gives 70; 80 + 50 gives 100 + 30.
- **Triggers**: Sort (`Inventory::autoArrange` of a player character's inventory): every eligible item is a donor, in order, then the
  inventories are arranged again. Receiving (`Inventory::_NV__sectionAddItemCallback` with a supply item, a player character's inventory): a
  kit is the only donor; a bolt pack makes every eligible pack matching it a donor. Both run on the next world update, sorts first.
- **Combat**: while the character is in ranged combat or reloading (character +0x2f0 flags +0x36, +0x80) the original keeps one bolt in each
  pack and finishes the merge about once a second (wall clock) once both clear, to avoid deleting a pack its reload holds. Meitou's reload
  takes its bolt from whatever pack it finds at that moment and holds none, so the port merges at once (port choice).
- **Port**: Sort is the game's `ArrangeInventory` seen in the mod system's `Inputs` (after every system, also while paused through
  `IPausedCommandHandler`) and re-arranged with `InventoryCommands.Arrange` (the filters of other mods asked as for the button). Receiving is a
  comparison with the supply items the character held at the last look (every 6 ticks and after any command); a character seen first, as after
  a load, only notes what it holds.
- **Log**: the original appends `MedkitMerge-diagnostics.log`; the port writes its `merged player=... trigger=... record=... type=...
  transferred=... donor-left=...` line to the mod log.
- The Workshop description (older) says splints and robotic repair kits are not supported; the shipped DLL supports them and bolts.
  **Observed** (description against DLL).
- **Unknown**: not checked in game.

## Missing

- Nothing that needs the engine.
