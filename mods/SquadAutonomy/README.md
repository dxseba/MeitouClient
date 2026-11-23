# Squad Autonomy (partial port)

Port of *Squad Autonomy* (Au2942, workshop 3800729618, GPL-3.0; [Au2942/KenshiPlugins](https://github.com/Au2942/KenshiPlugins)), on Meitou's
autonomous player squads ([docs/modding.md](../../docs/modding.md), "Player squads on AI packages"). The original's `SquadAutonomy.mod` (its AI
packages) arrives with it as a required item.

- **Window** (the AUT button on the top bar; *Show button on Mainbar*): one player squad at a time.
  - **Enable Autonomy**: `SetSquadAutonomy(squad, on, Owner: mod id)` (removing the mod gives the squad back), with the original's message
    "<squad>: Autonomy enable / disable".
  - **Home:** *Set Squad Home Building* (the building the HUD inspects; "Select a building!" without one) or *Clear Buildings* ("Buildings
    cleared"): `SetSquadHome`.
  - **AI packages with priorities**: *Priority* (0-5), *Add AI Package* (a picker with the original's *Search*), *Clear AI Packages*. They go on
    the board by priority as slots (`SetSquadPackages`, saved with the squad), as the original's `Blackboard::_addPackage(package, priority)`:
    the engine tries the highest slot first. Checked every second.
  - **Start / End Work Time** (0-24, default 0 and 24) and this port's **Off-work package**: off work the squad runs that package instead.
  - **Combat**: *Attack Enemies*, *Protect Allies*, *Do Medic*, *Rescue Downed Allies* (on), *Man Turrets* (off), saved with the plan.
- **Goal scores** (a chooser desire filter, `IGoalDesireFilter`, on the list goals of an autonomous squad's members; the original's
  `Tasker::score` hook, its shipped source): stay in home and sit around x0.0001 at work; manning a turret (on a building too) 0 with Man
  Turrets off or off work; bodyguard and follow the squad leader 0 while the leader lies in a bed; the gate, the home building's and home
  town's guard nodes, territorial aggression, attack enemies and the mines 0 off work, the two attacks also with Attack Enemies off; protect
  allies (in town too) and protect the own squad 0 with Protect Allies off; medic and robot repair jobs 0 with Do Medic off; find and rescue
  (with beds, the leader) 0 with Rescue off; acquiring food at home and grabbing one food x10. The filter reads a member table the slow step
  publishes every second (and when the world is ready).
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
- **Missing**: of `Tasker::score`, stay in home and sit around at 0 while the member has orders, the patrol goals at 0 off work inside a
  building, protect at 0 while asleep and not under melee attack, the medic jobs' own score (first-aid need x2), relaxing in town at 1 while
  the leader relaxes (no read of those states); the bed scores (Rest Until Healed with its Rest / Healed thresholds, Allow Sleep Outside Work
  Hours: they read the rested state, first-aid need and last sleep, which the engine does not expose), paid beds; its own labour finder with
  per-stat priorities, hauling (`choosePermaJob`, `findMineToWorkAt`); Stay Inside Gate and Close Gate (gate tasks, door opening); a work
  building separate from the home (no `SetSquadWork`); the squad-screen button, button layout options and logging.
- **Observed** (2026-11-19, headless sandbox fight, earlier version): loads, 3000 ticks without a fault. The package slots, the goal scores and
  the original's packages are not checked in game; squad ids across a save and load are matched by name as a fallback.
