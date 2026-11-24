# Organ Harvesting (port)

Port of *Organ Harvesting* (Rokeshi, Workshop 3778746024), from the shipped source `HarvestOrgans.cpp` (read for facts only) and DLL strings.
Right-click a downed, living human: **Harvest Organs** sends the selected characters to kill it where it lies, and the one who lands the
killing blow gets a random organ: Heart, Lung, Kidney or Liver. The organ items are the original's data: its `HarvestOrgans.mod` must be
enabled (the original is a required item). No Harmony.

## Facts (Verified: `HarvestOrgans.cpp`; its DLL holds the same strings, unless marked)

- **Row** (`ContextMenu::showContextMenu` hook, button "Harvest Organs"): on a character that is unconscious and not dead, not an animal,
  whose race is not a robot and whose chest part does not heal itself (RACE `self healing`), while the selected character's chest does not
  heal itself either (robots can cut).
- **Order**: `newPlayerTaskSelectedCharacters(KILL_CAGE_OCCUPANT)` (task 134) on the target for the selected characters; the target becomes
  the one pending victim (a new harvest replaces it); no credit yet.
- **Credit** (`Character::_NV_hitByMeleeAttack` hook): each melee hit on the pending victim by a player-faction character whose current
  goal is task 134 makes it the killer. **Award** (main loop hook): once the victim is dead, the last killer gets one of
  `10-`, `11-`, `12-`, `13-HarvestOrgans.mod` at random (`rand() % 4`), made by the item factory and added to its inventory; then nothing
  is pending.
- Organ items (**Observed**, strings of `HarvestOrgans.mod`): ITEM records named Heart, Lung, Kidney and Liver (12 Kidney, 13 Liver), each
  with an icon path `./mods/HarvestOrgans/icons/<Organ>.png` and the ration mesh; values not read.
- **Port**: the row is `AddCharacterMenuVerbs` (the selected character's race noted once a frame from the snapshot); the order is a mod
  command. Meitou has no order row for task 134 on a body on the ground (its kill-prisoner job is for caged captives), so each worker gets
  a move to the body (`World.GiveOrder`, row 29) and, within 20 of it, plays the captives' `chop down static` clip and strikes once per chop
  clip at 0.55 of it with `CombatSystem.Execute`, the engine's execution blow of task 134 (no reach, block or dodge test; no crime). A worker
  whose victim got up, was picked up or died, or who was given another order, stops. The roll is the world's seeded one with the mod's purpose.
  Not saved (the original's pending victim is not either).
- A harvester with no room: the organ is lost, with a notice (**Unknown** whether the original's `addItem` drops it on the ground).
- **Unknown**: whether the original's task 134 counts as a crime on a body outside a cage; not checked in game.

## Missing

- Nothing that needs the engine. With an order row for task 134 on a downed character the port could hand the kill to the engine's own
  job.
