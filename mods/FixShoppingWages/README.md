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
- **Debug** (a *Debug* sub-group; the original's `verboseDebugLogging`, `limitVerboseDebugLogging`, `developerDebug`, all off; output in the mod
  log): *Verbose debug* logs each daily pay (`dailyUpdates START` with day and hours, the universal wage and its source, a `DAILYUPDATE DIFF` or
  `NODIFF` line per paid character with name, id, `wages`, `money min`, `money max` and its purse before and after, `END` with the counts) and
  once an in-game hour the non-player purses that changed (`HOURLY DIFF`), plus squads new or gone unless *Limit verbose*. *Developer hotkeys*
  makes four key actions act (Controls page; defaults the original's chords) on the HUD's character, else the first selected: Ctrl+T logs its
  purse and wage fields, Ctrl+Shift+T gives it 2000 (its squad's purse), Ctrl+R dumps its record's int, float, string and bool fields,
  Ctrl+Shift+R logs Dried Meat's record (by name, else string id `46130-DriedMeat`), value and average price. They run as paused commands.
- **Differences**: the hourly diff is per squad purse (Meitou keeps NPC money per squad), not per character, and the original's per-character
  state dump (stealth, prone, chained...) is not logged.
- **Verified** against the original DLL (decompiled): the `dailyUpdates` hook, tiers, roll range, cap formula, defaults and maxima, the debug
  keys and labels, the hotkeys (Ctrl and Shift and T or R by `GetAsyncKeyState`, each once per press) and their actions; against the
  shipped `mod-config.json`: the defaults. **Observed** (by reading `SquadWages`): a squad with a waged member also gets the engine's daily
  `wages` pay, which sets the purse first, so it ends a day at twice its wage. **Unknown**: not checked in game (the hotkeys and log included).
