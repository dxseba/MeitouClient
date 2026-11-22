# Fix Shopping Wages

Port of *Fix Shopping Wages* (workshop 3775703318), rebuilt from its description and the behaviour of its DLL (decompiled; no code copied).

- **Daily wage**: when the game day changes (not on the first day seen after a load), every loaded, living non-player character's wage is added
  to its money (its squad's purse, `Squad.Money`, which NPC shoppers pay from, [NPC purchases](../../docs/game/economy.md#npc-purchases)), but never
  past *Max savings multiplier* (10) x that wage; members are paid one after another into the shared purse, each against its own cap.
- **A character's wage** (the original's tiers): none without a CHARACTER `wages` field or with `wages` -1; a non-zero `wages` itself; with
  `wages` 0, none when `money max` is missing or -1, else a deterministic roll in `money min`..`money max` when min is below max (a roll of 0
  falls through), else the universal wage: *Override wage value* with *Override base wage* on, else Dried Meat's average price (its value at level 0
  with the category rows), else *Base wage fallback* (78).
- **Settings** (Mod settings; the original's `mod-config.json` keys and Mod Hub labels): defaults are the shipped `mod-config.json`'s, so *Override
  base wage* is on with value 0, which leaves the Dried Meat and fallback tiers unused until changed (the DLL's own defaults are override off and
  value 0). Maxima as the original clamps: 100000, 100000, 1000.
- **Missing**: the original's debug hotkeys and verbose logs.
- **Verified** against the original DLL (decompiled): the `dailyUpdates` hook, tiers, roll range, cap formula, defaults and maxima; against the
  shipped `mod-config.json`: the defaults. **Observed** (by reading `SquadWages`): a squad with a waged member also gets the engine's daily
  `wages` pay, which sets the purse first, so it ends a day at twice its wage. **Unknown**: not checked in game.
