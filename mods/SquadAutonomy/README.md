# Squad Autonomy (partial port)

Port of *Squad Autonomy* (Au2942, workshop 3800729618, GPL-3.0; [Au2942/KenshiPlugins](https://github.com/Au2942/KenshiPlugins)), on Meitou's
autonomous player squads ([docs/modding.md](../../docs/modding.md), "Player squads on AI packages"). The original's `SquadAutonomy.mod` (its AI
packages) arrives with it as a required item.

- **Window** (the AUT button; *Show button on Mainbar*): one player squad at a time. **Button** (the original's `BtnLeft` 0.69, `BtnTop` 0.788
  as shares of the screen, `BtnWidth` 1.5 and `BtnHeight` 3.58 per cent of it, `BtnFontSize` 12, `LockPosition` on; **Verified**, its shipped
  cfg and source: placed in the main bar's root, which fills the screen): Mod settings; with the position unlocked a drag moves it and keeps the
  place. The font's pixel height at text scale 1 is taken as 18 (**Unknown**).
  - **Enable Autonomy**: `SetSquadAutonomy(squad, on, Owner: mod id)` (removing the mod gives the squad back), with the original's message
    "<squad>: Autonomy enable / disable".
  - **Home:** *Set Squad Home Building* (the building the HUD inspects; "Select a building!" without one) or *Clear Buildings* ("Buildings
    cleared"): `SetSquadHome`.
  - **AI packages with priorities**: *Priority* (0-5), *Add AI Package* (a picker with the original's *Search*), *Clear AI Packages*. They go on
    the board by priority as slots (`SetSquadPackages`, saved with the squad), as the original's `Blackboard::_addPackage(package, priority)`:
    the engine tries the highest slot first. Checked every second.
  - **Start / End Work Time** (0-24, default 0 and 24) and this port's **Off-work package**: off work the squad runs that package instead.
  - **Basic**: *Rest Until Healed When Wounded* (on), *Rest Health Threshold* (50) and *Healed Health Threshold* (90, 0-100 in steps of 5
    here), *Allow Sleep Outside Work Hours* (off), saved with the plan.
  - **Combat**: *Attack Enemies*, *Protect Allies*, *Do Medic*, *Rescue Downed Allies* (on), *Man Turrets* (off), saved with the plan.
- **Goal scores** (a chooser desire filter, `IGoalDesireFilter`, on the goals and plan steps of an autonomous squad's members; **Verified**
  against the original's shipped DLL, decompiled, and its source). `Tasker::score` on list goals: stay in home and sit around 0 while the
  member has player orders queued, else x0.0001 at work; manning a turret (on a building too) 0 with Man Turrets off or off work; bodyguard and
  follow the squad leader 0 while the leader lies in a bed or its current goal is relaxing in town; the gate, the home building's and home
  town's guard nodes, territorial aggression, attack enemies and the mines 0 off work, the two attacks also with Attack Enemies off; protect
  allies (in town too) and protect the own squad 0 with Protect Allies off, and while the member sleeps with no melee attacker on it; medic and
  robot repair jobs 0 with Do Medic off, else twice the member's own first aid need for its race's kit (as the original: the medic's own
  need); patrolling the town 0 off work inside a building unless it is already the member's goal; relaxing in town 1 for a member whose
  leader relaxes in town; find and rescue (with beds, the leader) 0 with Rescue off; acquiring food at home and grabbing one food x10.
  `scoreGoToBed` (both bed goals; AI slot 30) replaces the game's: with Rest Until Healed, an organic member whose rested level (medical
  `restedState`, `MedicalState.RestedLevel`) is under Healed / 100 and whose first aid need is under 0.1 scores 5 asleep or off work, 20 at
  or under Rest / 100; then with Allow Sleep Outside Work Hours, an awake member off work scores by the time since it last slept (under 2 h 0,
  8 h 0.01, 12 h 0.1, 18 h 0.5, else 1); otherwise 0. `scoreGetOutOfBed` (getting out of bed if it is an emergency; AI slot 28) while the
  member's goal is a bed goal: 1 under melee attack, 0 while it rests until healed, 1 at work with Allow Sleep Outside Work Hours, else the
  game's. The filter reads a member table the slow step publishes every second (and when the world is ready).
  **Observed** stand-ins: asleep = lying in a bed; "under melee attack" = a running attack on the member from within 40 units (the game's
  immediate-threat flag is not open to mods); the last sleep = when the port last saw the member in a bed (saved with the plans by lasting id;
  never seen: long awake), as Meitou keeps no `lastSlept`; player orders = its order queue not empty; inside = `SightMap.Inside`.
- **Logging** (*Enable logging*, *Log severity* 0-4, *Log spam*; the original's `EnableLogging`, `LogSeverity`, `LogSpam`, defaults off, 0,
  off): plan changes, autonomy switches and package sends at info, each score the filter changes as spam, into the mod log with the original's
  INF / DBG prefixes.
- **Packages**: *Listed* are the AI_PACKAGE records tagged `<SquadAutonomy>` or named in a package list (the installed original's
  `SquadAutonomy.cfg` `<Packages>`, quoted names, read from its folder beside this one; and this port's `packages.txt`); *All* (this port's) lists
  every package. Names show without the tag.
- **Verified** (`SquadAutonomy.mod` read with `meitou-tools record`): 10 AI packages, 9 tagged (Relax at home, Wandering from town to town, Guard
  gate, Guard town, Labour at home, Guard building, Man Turrets, Hang out in a town, Solo Squad) plus the untagged base *Relax at home squad*,
  and 3 AI tasks (pickup downed 105, pickup downed urgent 105, Repair Skeleton non-urgent 57). **Observed** (by reading): their `inherits
  from` chains resolve in `AiDatabase.Package`; a goal naming a record no loaded mod has (`57336-__dont commit.mod`) is skipped.
  **Verified** (original DLL decompiled): the texts above; priority slider 0-5; work time 0 and 24 by default, off work when start equals end
  or the hour is outside [start, end); per-squad defaults Rest Until Healed on, Rest 50 / Healed 90 health, paid beds and sleep off, labour
  priorities 1, Attack Enemies / Protect Allies / Do Medic / Rescue on, Man Turrets / Stay Inside Gate / Close Gate off.
- **Missing** (engine gaps): the patrol rule's score decay while patrolling is the current goal (the original rewrites the AI's held goal
  score, max(0.0001, x 0.95)); Allow Using Paid Beds (its bed finder); its own labour finder with per-stat priorities and hauling
  (`choosePermaJob`, `findMineToWorkAt`); Stay Inside Gate and Close Gate (gate tasks, door closing); a work building separate from the home
  (no `SetSquadWork`); the AUT button in each squad's cell of the squad screen (*ShowInSquad*: no per-squad anchor); its translations
  (`locale/*.mo`, no per-mod translation API).
- **Observed** (2026-11-19, headless sandbox fight, earlier version): loads, 3000 ticks without a fault. The package slots, the goal scores and
  the original's packages are not checked in game; squad ids across a save and load are matched by name as a fallback.
