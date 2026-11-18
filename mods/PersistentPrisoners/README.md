# PersistentPrisoners (partial port)

Port of *PersistentPrisoners* (workshop 3798215878, GPL-3.0-or-later), rebuilt from its Workshop description.

- **Keeping**: a Harmony postfix on the population's keep test (`PopulationSystem.Kept`) keeps a platoon that holds a prisoner in a
  player-owned cage (loaded squad or stored members), so it is neither despawned when it unloads nor by the off-screen orphan timer. Meitou's
  own *Squads keep their people* switch (on by default) already keeps most of them; this covers it off and the Faithful rules. Releasing the
  prisoner ends the protection (the test reads `IsImprisoned` and the cage).
- **Player cages**: learned from the loaded fixtures (`FixtureKind.Cage`, owner = the player's faction) and saved with the world (mod sidecar),
  so a cage whose zone is out still counts. Any cage-kind fixture counts: cages, poles and modded restraints alike.
- **Overview** (Ctrl+Shift+P): name, faction, nutrition (0-300), condition, nearest town; prisoners in player cages (loaded and away) and the
  player's own characters held anywhere.
- **Starvation** (on by default): off feeds the player's caged prisoners up to *Minimum nutrition* (1-300) at 25 per game hour.
- **Missing** (partial): the original's per-language ini, column and font settings, diagnostic logging, a configurable shortcut key (on/off
  only) and caged corpses (a dead platoon is not kept; **Unknown** whether Meitou removes caged bodies).
- **Unknown**: not checked in game; whether Meitou ever despawns a caged prisoner with the default switches. **Observed** (2026-11-19): builds.
