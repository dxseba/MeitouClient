# Job Management (port)

Port of *Kenshi Job Management (MCM fork)* (workshop 3818460323), a fork of SirixKC's
[KenshiJobManagement](https://github.com/SirixKC/KenshiJobManagement) (GPL-3.0-only); behaviour read from its source, code our own.

- **Window**: Ctrl+J or Alt+J by the setting (a prefix on `GameHotkeys.Key` takes the J before the Rescue key's second binding) or the **JM** button
  under the time panel (overlay, left 486, top 44). Opening saves the clock (`SimulationClock.SaveSpeeds`) and pauses; Close puts it back unless the
  player changed the speed meanwhile. Built from `UiNode`s in `UiSlots.Overlay`; the session comes from a `PlayerInterface.BindUi` postfix.
- **Squad Jobs**: one squad at a time (`<` `>` cycle, opens on the active squad); each member's list in priority order. A job and the companion the
  engine added after it (152+124, 148+106, 45+31/289, 137+138, 223+224, 273+274) form one unit for Up, Down and X. Click a job to pick it, then
  **Move here** / **Copy here** on another member (any squad). Each member has the original's **Jobs: ON / OFF** switch (the orders panel's Jobs
  toggle for that character) and **Clear Queue**, which asks "Remove all N permanent jobs from X?" (Yes / No) before emptying the list. Rows whose
  target is gone say so.
- **Add Medic/Robotics**: adds each missing Rescue (148, with its 106), Put in Bed (106), Medic (58), Robotics (57), Splint Rigging (250), then
  removes a doubled healing row and prioritizes. **Prioritize Healing**: stable reorder Rescue, Put in Bed, Medic, Robotics, Splint, Engineer (125),
  then the rest. **Remove Invalid Jobs**: removes rows the world last found without a target (below). All three act on the shown squad's people
  (animals skipped; Remove Invalid includes them).
- **Stations**: the player's complete machines (`WorldSnapshot.Machines`) by group: turret Defense, farm Farming, bench Crafting, function 3
  Research, 4/14 Refining, 1/27 Mining (a water mine is Storage / Hauling), 7 Training, storage Storage, any other operable machine Other; the rest
  (generators, lights, beds) are no station. Each line: name, town (else Wilderness), workers with a job there / `max operators`, blockers
  DISMANTLING, BROKEN, POWER OFF, NO POWER / LOW POWER (power factor below 0.99), NO WATER. Clicking one lists assigned and available people (name
  and total jobs, as the original's "N total jobs") by its `stat used` (else farming, science, labouring, turrets by group), highest first, training
  lowest first; **Assign** gives the job its own Shift verb gives (234 via 146, 97, 152, 87, 124), **Remove** drops that person's units on the site.
- **Hooks**: all edits are the engine's `AddJob`, `AddOrderJob`, `RemoveJob`, `MoveJob`, which `World.ApplyPausedInterfaceCommands` applies while
  paused. An edit's steps each plan on a fresh snapshot and wait for the next one; clicks are ignored meanwhile. A move gives first and removes from
  the giver only once the receiver's list shows the job. A tick system (`JobBoardSystem`, every 30 ticks) publishes skills and invalid rows: a site
  job whose player building, fixture or seat is gone, or a follow job whose character is gone; rocks, town work sites, unmatched handles and the
  original's protected types (healing, engineering, intruders, foraging, beds) are never judged.
- **Settings**: enabled, the window key (the fork's `Hotkey`: `CTRL+J` default, `ALT+J`, `NONE`), JM button, the nine station groups (defaults as
  the original: Training, Storage, Defense off). The window's filter buttons change the same values for the session; only the Settings page keeps
  them.
- **Differences**: no drag and drop, multi-select, clipboard or portraits (buttons and a pick instead); no wheel scrolling (Scroll up/down); no Tab
  squad cycling, collapsing or window size setting (`UiScale` 70-200%); no message log window; a hotkey other than Ctrl/Alt+J; English only (the
  fork ships Russian and Chinese catalogues); the stations list has no natural resource nodes or town buildings (jobs on them still show and move);
  the batch buttons act on the shown squad, not a selection; invalid rows on rocks are not detected (Meitou may not have placed the rock's zone
  yet); skills and invalid rows are those of the last running second (none right after a load until the game runs); LOW POWER and NO WATER are
  additions.
- **Verified** against the fork's DLL (decompiled strings and ini reads): the `General` keys `Enabled`, `Hotkey` (`CTRL+J`), `HudButton` (1),
  `UiScale` (100, 70-200); the per-member Jobs toggle, Clear Queue prompt and station worker line texts. Builds (0 warnings). **Unknown**: all play
  behaviour (not run in game): the key, the pause, the commands' effect and the layout at small resolutions.
