# Medic Button Splint Rigging (port)

Port of *Medic Button Splint Rigging* (workshop 3781354452, `MedicButtonSplintRigging.dll` v1.0.0.6): Shift with the HUD's Medic button also
gives the characters the Splint Rigging job. A tick system (`SplintRiggingSystem`, at the end) reads the engine's own Medic button command in its
`Inputs` (every system gets every due command): no new hook and no Harmony. The click handler needs no patch, because the command already
carries the Shift state (`MedicJobCommand.Job`).

## Facts (Verified: Ghidra decompile of the DLL, `startPlugin` and its two hooks, unless marked)

- It hooks `PlayerInterface::addJobSelectedCharacters` (always calling the original) and `GameWorld::_NV_mainLoop_GPUSensitiveStuff`. When the
  added job is 58 (Medic), or 57 (Robotics) with nothing pending, it stores the interface and the position argument and arms a 2-frame delay;
  a 57 that follows a pending 58 only restarts the delay. When the delay runs out it calls `newPlayerTaskSelectedCharacters(249, null target,
  no building, the position, true)` once. So the Splint job comes after the button's Medic and Robotics jobs.
- 249 is the Splinting order (`TaskType.SplintOrder`); given as a job it becomes 250, Splint Rigging (Meitou `PermaJobs`: the TaskData row's
  job field; the description names that job). The port gives `SplintOrder` through `PermaJobs.Give`, as a Shift order does: a second Splint
  Rigging job is refused, an animal gets none.
- **Observed** (by reading `PlayerInterface.UiAction` and `FirstAidSystem.PressMedic`): Meitou's Medic button sends `MedicJobCommand(selection,
  Shift)`; with Shift the first-aid system gives 58 (and 57 when the active squad has a robot) as jobs; the port's system runs after it in the
  same tick, so the job order matches the original's.
- The DLL tests no Shift itself. **Unknown**: whether the game's plain (no Shift) click also goes through `addJobSelectedCharacters`; the port
  follows the description (Shift only). Also **Unknown**: whether other paths that add a 58 job in the original (a Shift treat order) trigger
  it; the port reacts only to the Medic button command (the button and its key).
- No settings, no translations. **Observed**: the `.mod` (719 bytes) holds only the description.

## Missing

- Nothing. **Unknown**: not checked in game.
