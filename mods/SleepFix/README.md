# SleepFix (partial port)

Port of *SleepFix* (workshop 3787375792), rebuilt from its Workshop description.

- **To bed**: every 90th tick a player character with a part (its own limb, not a stump or prosthetic) under *Wounded below* (90%), awake, idle
  or on one of its jobs (`RunningTask.Source == Job`), not fighting, carrying, carried or held, gets the player's bed order
  (`World.GiveOrder(id, TaskType.UseBed, site)`, the order a right-click on a bed gives) to the bed the engine's own search picks
  (`FixtureRegistry.FindBed`: own faction's beds first, indoors and inside walls weigh more). With *Wait for treatment* (on) a bleeding character
  is not sent until bandaged. A failed search is retried after 900 ticks.
- **Up again**: one this mod sent gets up once every part reaches *Healed at* (100%), by a move order to where it lies (a sleeper gets up), and
  its jobs take it back. Off: it stays in bed.
- **Missing** (partial): Skeleton Repair Bed handling (robots are sent to whatever bed the search finds), the cfg file, waking for a raid. Who was
  sent is not saved: after a load the healed are not got up by the mod.
- **Unknown**: not checked in game.
