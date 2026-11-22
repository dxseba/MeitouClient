# Squad Autonomy (partial port)

Port of *Squad Autonomy* (Au2942, workshop 3800729618, GPL-3.0; [Au2942/KenshiPlugins](https://github.com/Au2942/KenshiPlugins)), on Meitou's
autonomous player squads ([docs/modding.md](../../docs/modding.md), "Player squads on AI packages"). The original's `SquadAutonomy.mod` (its AI
packages) arrives with it as a required item.

- **Window** (the AUT button on the top bar; *Show button on Mainbar*): one player squad at a time.
  - **Enable Autonomy**: `SetSquadAutonomy(squad, on, Owner: mod id)` (removing the mod gives the squad back), with the original's message
    "<squad>: Autonomy enable / disable".
  - **Home:** *Set Squad Home Building* (the building the HUD inspects; "Select a building!" without one) or *Clear Buildings* ("Buildings
    cleared"): `SetSquadHome`.
  - **AI packages with priorities**: *Priority* (0-5), *Add AI Package* (a picker with the original's *Search*), *Clear AI Packages*. The top
    package goes on the board by `ChangeAi` (saved with the squad), the others into the board's slots by priority (the board's private `_slots`,
    by reflection), as the original's `Blackboard::_addPackage(package, priority)`: the engine tries the highest slot first. Reapplied every
    second and after a load.
  - **Start / End Work Time** (0-24, default 0 and 24) and this port's **Off-work package**: off work the squad runs that package instead.
- **Packages**: *Listed* are the AI_PACKAGE records tagged `<SquadAutonomy>` or named in a package list (the installed original's
  `SquadAutonomy.cfg` `<Packages>`, quoted names, read from its folder beside this one; and this port's `packages.txt`); *All* (this port's) lists
  every package. Names show without the tag.
- **Verified** (`SquadAutonomy.mod` read with `meitou-tools record`): 10 AI packages, 9 tagged (Relax at home, Wandering from town to town, Guard
  gate, Guard town, Labour at home, Guard building, Man Turrets, Hang out in a town, Solo Squad) plus the untagged base *Relax at home squad*,
  and 3 AI tasks (pickup downed 105, pickup downed urgent 105, Repair Skeleton non-urgent 57). **Observed** (by reading): their `inherits
  from` chains resolve in `AiDatabase.Package`; a goal naming a record no loaded mod has (`57336-__dont commit.mod`) is skipped. **Verified** (original DLL decompiled): the texts above; priority slider 0-5; work time 0 and 24 by default, off work
  when start equals end or the hour is outside [start, end); per-squad defaults Rest Until Healed on, Rest 50 / Healed 90 health, paid beds and
  sleep off, labour priorities 1, Attack Enemies / Protect Allies / Do Medic / Rescue on, Man Turrets / Stay Inside Gate / Close Gate off.
- **Missing** (engine gaps: the original hooks the AI's goal scoring, `Tasker::score`, `choosePermaJob`, the bed scores and the gate tasks): its
  own labour finder with per-stat priorities, hauling; the combat, medic, rescue and turret switches; rest until healed and sleep outside work
  hours (the original clears labour goals off work and gets members out of bed); paid beds; Stay Inside Gate and Close Gate; a work building
  separate from the home; the squad-screen button, button layout options and logging. Needs a desire filter on an autonomous member's goals and
  jobs.
- **Observed** (2026-11-19, headless sandbox fight, earlier version): loads, 3000 ticks without a fault. The package slots and the original's
  packages are not checked in game; squad ids across a save and load are matched by name as a fallback.
