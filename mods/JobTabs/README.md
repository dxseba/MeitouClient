# Job Tabs (port)

Port of *Job Tabs* (jacobericson/JobTabs v0.1.0, workshop 3813790518).

Facts, **Verified** from the original DLL (decompiled strings and code) and its shipped `JobTabs.cfg`:
- `tabCount` 5 (2-9) tabs in a column beside the Jobs list; `activeStyle` `frame` (`Kenshi_Button1` buttons and a frame widget of its own
  skin `JobTabs_ActiveFrame`, a 9-slice of `Kenshi_UI.png` registered from XML in its DLL, over the active one) or `dot` (`Kenshi_TickButton1`
  buttons); the active button `setStateSelected` in both; an empty tab's button at alpha 0.45.
- A click switches every selected character's job list (hooks on `OrdersPanel`), refreshes the orders and plays `General_Click` when one changed.
- Tooltip: "Tab N: K jobs" ("job" for one), then up to 8 job names ("
..." for more); a job's name is its task name, plus ": " and the
  target's name for a job per target. An empty tab: "Empty: hold Shift while giving an order to add jobs". English only (texts in the DLL,
  no catalogue).
- Saved in the vanilla save, in each character's orders record (`OrdersReceiver::serialise`): int `jt_v` 1, int `jt_active`, and for each job
  i of each tab t but the active one int `jt<t>_pjob<i>` (task type), handle `jt<t>_pjobT<i>` (target) and string `jt<t>_txt<i>` (the job's
  tooltip name). Read back: `jt_active` above 8 is 0; a job whose handle does not read is skipped.

Port: *Tabs* (2 to 9, default 5, read at the start) numbered buttons in a column left of the Jobs list (`AddJobPanelButtons` with
`UiJobButtonArrangement.Columns`) while a player character is selected; the active tab is lit (`Lit`); *Active tab style* `Dot` gives the
buttons the `Kenshi_TickButton1` skin (`UiJobButton.Skin`). The buttons are `Silent`: the system plays `General_Click`
(`world.PostInterfaceSound`) only when a press changed a list. A click switches every selected player character: its job list
(`CharacterCold.Jobs`, the entries themselves) is kept in the active tab and the chosen tab's put in its place, then its next decision is
asked for (`DecisionSystem.Request`). A new character starts on tab 1 with its current jobs.
Saved in the mod sidecar (version 2), keyed by lasting character id; a kept job per target keeps its target's lasting id and names the target
again when restored, also after a load. Also written under the original's keys (`world.SaveFields`, `CharacterSaveRecord.Orders`) at each
switch and when a world is ready (handle parts `TYPE`, `C`, `CS`, `I`, `S` as the game writes a handle, **Observed**), so a save taken to the
original keeps the tabs; a loaded character the sidecar does not know (a save from the original) takes its tabs from those keys, a
character target matched by its save handle. Removing the mod leaves each character with the jobs of the tab it was on.

`JobTabs.cfg`: read once at the start from the installed original's folder (the port ships none) as the original reads it, **Verified**
against its DLL (decompiled): `key=value`, `#` starts a comment, `tabCount` a whole number (`strtol`, base 10, nothing after it) in 2..9, else
5 with a warning; `activeStyle` `frame` or `dot`, else `frame` with a warning. They become the *Tabs* and *Active tab style* defaults (a
value set on the Settings page wins); the warnings go to the mod log.

Missing:
- The frame style's `JobTabs_ActiveFrame` frame: its skin is the original's own (from its DLL), not in the game's resources, and a mod cannot
  register a skin; the lit button stands in. Empty tabs are not dimmed (a job panel button has no opacity). Engine gaps.
- In the original's keys, a building job on another tab keeps the handle it was loaded with (none for one given in Meitou), as a mod cannot
  ask for a building's save handle; such a job read from an original save keeps its handle but no Meitou site, so it shows as without a
  target until re-given. Engine gap.
- The engine allows 9 job panel buttons across all mods, so another mod's buttons take from them.
- **Unknown**: not checked in game, the buttons, their skins and tooltips, the cfg read and the save keys in the original game included.
