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
- **Desire filter** (`IGoalDesireFilter`, registered through `IChooserOwner`; the original's `AI::scoreGoToBed` and
  `AI::scoreGetOutOfBedOnceFullyHealed` hooks), for a living player character that is not carried and (*skipCombat*) not fighting: a bed goal
  (`GoHomeAndGoToBed`, `GoHomeAndGoToBedSecure`) while not healed weighs x *bedMult* (15); under *forceBed* (30) it is raised to *forceBed* when
  no cut waits for a bandage (*waitForTreatment*; else the game's desire) and a free bed of its faction is about (*ownBedsOnly*; off: any bed).
  The plan step *get out of bed once healed* weighs 0 while it lies in bed not healed (*stayInBed*), else x *getUpMult* (1). The filter reads a
  table the slow step publishes every pass (and when the world is ready), so all threads of a tick see the same values. **Verified** (original
  DLL decompiled): those branches and defaults. **Observed** (by reading the engine): it asks a bed goal only when its own bed search found a
  bed, so the original's "no bed" branch (the multiplied desire) never comes up. **Verified** (KenshiLib's 1.0.68 address of `AI::scoreGoToBed`,
  0x5A0880, is AI slot 30, the desire of both bed goals in Meitou's task table): it scores exactly `GoHomeAndGoToBed` and
  `GoHomeAndGoToBedSecure`.
- **Log** (*Logging* settings; **Verified**, original DLL decompiled: its cfg keys `debug`, `logLimit` (2000), `probeBeds`, `dumpParts` and its
  per-category line cap with repeat folding): the settings line at start, then with *Debug log* each bed order and each release with its
  reason, with *Log bed searches* each search's bed, with *Log body parts* the part table of each character sent; each category stops after
  *Log limit* lines. Into the mod log, not `SleepFix.log`.
- **Settings**: the Mod settings page, applied at once, in place of the original's `SleepFix.cfg` (re-read a few seconds after saving).
  **Verified** (decompiled cfg reader): `pauseJobs`, `skipJobs`, `skipSelected`, `forceGoal` are not read from the cfg (only printed in its
  status), so they are not player settings.
- **Not ported**: `dumpHex` and `probeOrder` (hex dumps of the game's own order and part structures; there are no such structures here).
- **Observed**: not checked in game. Timers count game seconds where the original's count real milliseconds (the simulation has no wall clock);
  the pass runs every 30 ticks, the original every AI update.
