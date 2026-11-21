# Fix Shopping Wages (partial port)

Port of *Fix Shopping Wages* (workshop 3775703318), rebuilt from its Workshop description.

- **Daily allowance**: when the game day changes (not on the first day seen after a load), every NPC squad whose members have no fixed
  `wages` gets the sum of its members' daily pay added to its money (`Squad.Money`, the purse a shopper pays from,
  [NPC purchases](../../docs/game/economy.md#npc-purchases)), up to *Savings cap (days)* (5) days of it.
- **A member's pay** (the original's tiers): CHARACTER `wages` of 1 or more is left to the engine, which already sets the squad's money to it
  every day (`SquadWages`), so such a squad is skipped; else a deterministic roll in `money min`..`money max` when `money max` is above 0; else
  the Dried Meat ITEM's `value` (or *Fixed wage* with *Use a fixed wage* on); else *Fallback wage* (78).
- **Skipped**: the poor by design (`wages` -1, or `wages` 0 with `money max` -1), the player's faction and player characters.
- **Missing** (partial): the local (market) Dried Meat price (the record's base `value` is used); the original's JSON file (settings under Mod
  settings instead); squads off screen are not paid (only the loaded world's squads). The original's savings cap default is not published.
- **Verified** by `meitou-tools record "Dried Meat"`: vanilla ITEM `42334-changes_otto.mod` has `value` 150 (also logged at start).
- **Unknown**: not checked in game.
