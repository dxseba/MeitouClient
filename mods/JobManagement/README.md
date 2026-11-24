# Job Management (port)

Port of *Kenshi Job Management (MCM fork)* (workshop 3818460323), a fork of SirixKC's
[KenshiJobManagement](https://github.com/SirixKC/KenshiJobManagement) (GPL-3.0-only); behaviour read from its source, code our own.

- **Window**: the window key (a key action, *Job Management window* on the Controls page; its default is the fork's `Hotkey`, a free-text
  setting read at start: a key name with optional `CTRL+`, `ALT+`, `SHIFT+`, default `CTRL+J`, `NONE` for none, as a chord, which fires before
  the game's own binding of the key, e.g. the Rescue key's second binding J; a key the page keeps, such as F6 or Escape, falls back to Ctrl+J) or the **JM** button
  under the time panel (overlay, left 486, top 44). Opening holds the pause (`context.Interface.HoldPause()`); Close releases it, which puts the
  speeds back unless the player chose one meanwhile. Built from `UiNode`s in `UiSlots.Overlay`; steps run once a frame on the shown snapshot
  (`AddFrame`); the running world's `JobBoardSystem` (told by `IWorldReady`) marks a new world, which closes the window and drops its steps. No
  Harmony or reflection.
- **Squad Jobs**: every player squad as a group, the original's header "[-]  {squad}  |  Members: {n}" ([+] while closed; a click opens or
  closes it); the game's current squad is marked CURRENT SQUAD and is the one open when the window opens (the others closed; toggles kept until
  it closes). An open group lists each member's jobs in priority order. A job and the companion the engine added after it (152+124, 148+106,
  45+31/289, 137+138, 223+224, 273+274) form one unit for Up, Down, X and drags. Click a job to pick it (Ctrl+click adds or drops more, from
  any members), then **Move here** / **Copy here** on a member who holds none of them (every job is given first and a refused one stops the
  edit; a move removes them from their owners once the receiver shows them all), or **Remove Job(s)**. Drag (`UiNode.Pointer` drag and
  release past 5 pixels on either axis, the drop target from `UiActionContext.Over`) carries the picked jobs, or the held one alone when it is
  not picked: onto **DROP TO REMOVE** (in the bar while dragging) removes them; one job on its own list moves its unit to the row it lands on
  (up: before it, down: after it); onto a member who owns none of them moves them there (one job: a refused give keeps it; several: the ones
  the receiver would refuse, or that repeat one already given, stay in place, "Skipped duplicate bundles: N."); onto a member owning one of
  several: "Move cancelled: choose a member that does not own any selected source job.". The mouse wheel scrolls the list one line a notch
  (the fork's `NormalizeMouseWheelNotches` fix). Each member has the original's **Jobs: ON / OFF** switch (the orders panel's Jobs toggle for
  that character) and **Clear Queue**, which asks "Remove all N permanent jobs from X?" (Yes / No) before emptying the list. Rows whose target
  is gone say so.
- **Recipients** (the batch buttons' targets): picking jobs makes their owners the recipients; a member's name (the original's portrait)
  makes it the only one, Ctrl+click adds or drops it; the **SQUADS** selector under the list (a button per squad, `<` `>` page it) makes a
  squad's members the recipients and scrolls to it, Ctrl+click adds the squad or, when all its members already are, drops it. The picked
  squads' groups open and the others close (none picked: the current squad's alone). Recipients are marked; none: "Select at least one
  recipient first.".
- **Add Medic/Robotics**: adds each missing Rescue (148, with its 106), Put in Bed (106), Medic (58), Robotics (57), Splint Rigging (250), then
  removes a doubled healing row and prioritizes. **Prioritize Healing**: stable reorder Rescue, Put in Bed, Medic, Robotics, Splint, Engineer (125),
  then the rest. **Remove Invalid Jobs**: removes rows the world last found without a target (below). All three act on the recipients
  (animals skipped; Remove Invalid includes them).
- **Stations**: the player's complete machines (`WorldSnapshot.Machines`) by group: turret Defense, farm Farming, bench Crafting, function 3
  Research, 4/14 Refining, 1/27 Mining (a water mine is Storage / Hauling), 7 Training, storage Storage, any other operable machine Other; the rest
  (generators, lights, beds) are no station. A group's header, "[-] {group}  |  {n} stations  |  {u} unassigned" (u: its stations without
  a worker), opens or closes it (all open at first; kept while the game runs). Each line: name, town (else Wilderness), workers with a job
  there / `max operators`, blockers
  DISMANTLING, BROKEN, POWER OFF, NO POWER / LOW POWER (power factor below 0.99), NO WATER. Clicking one lists assigned and available people (name
  and total jobs, as the original's "N total jobs") by its `stat used` (else farming, science, labouring, turrets by group), highest first, training
  lowest first; **Assign** gives the job its own Shift verb gives (234 via 146, 97, 152, 87, 124), **Remove** drops that person's units on the site.
- **Hooks**: all edits are the engine's `AddJob`, `AddOrderJob`, `RemoveJob`, `MoveJob`, which `World.ApplyPausedInterfaceCommands` applies while
  paused. An edit's steps each plan on a fresh snapshot and wait for the next one; clicks are ignored meanwhile. A move gives first and removes from
  the giver only once the receiver's list shows the job. A tick system (`JobBoardSystem`, every 30 ticks) publishes skills and invalid rows: a site
  job whose player building, fixture or seat is gone, or a follow job whose character is gone; rocks, town work sites, unmatched handles and the
  original's protected types (healing, engineering, intruders, foraging, beds) are never judged.
- **Settings**: enabled, the window key's default (the fork's `Hotkey`, free text; the Controls page rebinds it), JM button, *Window size (%)*
  (the fork's `UiScale`, 70-200, default 100: every size and text of the window times it, taken when the window opens), *Debug logging* (the
  fork's `Debug`: each message and each step's commands in the mod log), the nine station groups (defaults as the original: Training, Storage,
  Defense off). The window's filter buttons and **Reset Default** (those defaults) change the same values for the session; only the Settings
  page keeps them.
- **Messages** tab: the original's "Recent Job Manager Messages", the 10 most recent non-empty status messages of the session, timestamped
  (wall clock), oldest first; "No recent messages in this session." while there are none.
- **Pictures**, read from the installed original's `gui` folder (none shipped; without it the texts stand alone): its HUD button picture on the JM
  button; each station group's picture (`station-<group>.png`) on its header; on a job row the picture its job card shows by task type
  (**Verified**, decompiled DLL: 57 `station-skeleton-limbs`, 58/105/106/148 `job-medic`, 125 `job-engineering`, 158/223/224/273/274/283
  `job-escort-out`, 238/277/278/279 `job-foraging-animals`, 250 `job-splint`).
- **Differences**:
  - Mod-side: LOW POWER and NO WATER are additions; closed station groups and squad toggles are not kept across restarts (the fork keeps
    the station groups in its own settings file).
  - Engine: no portraits (an image node draws a record's portrait, not a live character's; the member's name stands in, also for
    picking recipients); a selector click does not make the squad the game's current squad (no mod call selects one); no Tab squad cycling (no key that only a mod
    window open takes); no Ctrl+C / Ctrl+V clipboard (the host's own keys; Move / Copy here stand in); English only (the fork's Russian and
    Chinese catalogues: no per-mod translation API); the stations list has no natural resource nodes or town buildings (the snapshot lists
    the player's machines only; jobs on them still show and move).
  - Not portable: the per-station pictures (ores, plates, crossbows...): the fork picks them by comparing building record ids in its code, not
    from a table the port could read.
  - The window's size is the setting alone (the fork's 100 follows the screen height); invalid rows on rocks are not detected (Meitou may not
    have placed the rock's zone yet); skills and invalid rows are those of the last running second (none right after a load until the game
    runs).
- **Verified** against the fork's DLL (decompiled strings and ini reads): the `General` keys `Enabled`, `Hotkey` (`CTRL+J`), `HudButton` (1),
  `UiScale` (100, 70-200), `Debug` (0); the station group picture per group, the job pictures above, the message log's texts; the per-member Jobs
  toggle, Clear Queue prompt and station worker line texts; the squad and station group header texts, the CURRENT SQUAD caption, the selector's
  SQUADS label, and the recipient, drag and duplicate messages quoted above. **Observed** (decompiled logic, read not run): squads not toggled are
  closed and the current one open; picking jobs replaces the recipients by their owners; a portrait click (Ctrl: toggle) and a selector click
  (Ctrl: add, or drop when all members are recipients) as above, the picked squads' groups opening; a drag past 5 pixels carries the picked
  jobs (the held one alone when not picked), several only onto a member owning none of them, one within its list to the row it lands on;
  station groups open by default (`Collapsed_<n>` 0). **Unknown**: what the fork's "unassigned" counts (the port: stations without a worker);
  all play behaviour (not run in game): the chord, the pause, the commands' effect, picking, drags, the pictures and the layout at small
  resolutions and other window sizes. Builds (0 warnings).
