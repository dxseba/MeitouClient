# SleepFix (partial port)

Port of *SleepFix* (workshop 3787375792). Every second, for each living player character (`SleepSystem`):

- **Release** a bed order (`OrderQueue` row `UseBed`, whoever gave it) by a move order to where the character is (a sleeper gets up, its
  jobs take it back): once healed when the order was held while wounded (*clearBedOrder*); after *bedReachTimeout* s without lying down (the bed
  is passed over next time); after *stuckRelease* s of a wounded character's rating not rising by 0.005; once, for a robot due for repair lying
  in a normal bed.
- **Send** a wounded character with no bed order, not selected, carried, held or out cold, not fighting (*skipCombat*), with no move order of the
  player's queued (*respectMoveOrder*), no unbandaged cut (*waitForTreatment*) and *bedOrderCooldownMs* since its last order: the player's bed
  order (`World.GiveOrder(id, TaskType.UseBed, site)`) to the bed `FixtureRegistry.FindBed` picks, leaving out other factions' beds
  (*ownBedsOnly*), beds other watched characters are sent to and the one it gave up on; a robot due for repair goes to the nearest free Skeleton
  Repair Bed of its faction.
- **Verified** (original DLL decompiled; defaults read from its data section): settings and defaults `autoBedOrder=1`, `healThreshold=1.0`,
  `stayInBed=1`, `clearBedOrder=1`, `waitForTreatment=1`, `ownBedsOnly=1`, `bedReachTimeout=60`, `stuckRelease=300`, `wearThreshold=5.0`,
  `repairRobotHp=0.90`, `bedOrderCooldownMs=15000`, `respectMoveOrder=1`, `skipCombat=1`, `ignoreRobotParts=1`, `forceBed=30`, `bedMult=15`,
  `getUpMult=1`; wounded = the lowest part health (clamped 0-1; 1 if any reads above 1.05) under `healThreshold - 0.0005`; robot parts count only
  by wear (from `wearThreshold`, 1 - wear/max) or health under `repairRobotHp`, and only when the faction has a repair bed; an unbandaged cut is
  flesh + bandage more than 1 under the part's maximum; selected characters are never sent; the order is Kenshi's 258 (Sleep) to the bed.
- **Engine gaps**: the original also rescales the AI's own goal scores (`AI::scoreGoToBed` x15, floored to *forceBed* 30 when an own free bed
  exists and no cut is unbandaged; `scoreGetOutOfBedOnceFullyHealed` x1, 0 while not healed with *stayInBed*). Needs a desire filter on the
  engine's bed and get-up goals. *stayInBed* holds anyway for a bed order (it lasts until replaced). Not ported: the cfg file (settings page
  instead), `debug`/`logLimit` and the diagnostic probes.
- **Observed**: not checked in game. Timers count game seconds (the original's count real milliseconds).
