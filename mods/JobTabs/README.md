# Job Tabs (port)

Port of *Job Tabs* (jacobericson/JobTabs v0.1.0, workshop 3813790518).

Facts, **Verified** from the original DLL (decompiled strings and code) and its shipped `JobTabs.cfg`:
- `tabCount` 5 (2-9) tabs in a column beside the Jobs list; `activeStyle` `frame` (skin `JobTabs_ActiveFrame`) or `dot` (tick buttons).
- A click switches every selected character's job list (hooks on `OrdersPanel`), refreshes the orders and plays `General_Click` when one changed.
- Tooltip: "Tab N: K jobs" ("job" for one), then up to 8 job names ("\n..." for more); a job's name is its task name, plus ": " and the
  target's name for a job per target. An empty tab: "Empty: hold Shift while giving an order to add jobs".
- Saved in the vanilla save, in each character's orders record (`OrdersReceiver::serialise`): keys `jt_active`, `pjob…`, `pjobT…`.

Port: *Tabs* (2 to 9, default 5, read at the start) numbered buttons in a column left of the Jobs list (`AddJobPanelButtons` with
`UiJobButtonArrangement.Columns`) while a player character is selected; the active tab is lit (the skin's selected state, as the frame). A click
switches every selected player character: its job list (`CharacterCold.Jobs`, the entries themselves) is kept in the active tab and the chosen
tab's put in its place, then its next decision is asked for (`DecisionSystem.Request`). A new character starts on tab 1 with its current jobs.
Saved in the mod sidecar (version 2), keyed by lasting character id; a kept job per target keeps its target's lasting id and names the target
again when restored, also after a load. Removing the mod leaves each character with the jobs of the tab it was on.

`JobTabs.cfg`: read once at the start from the installed original's folder (the port ships none) as the original reads it, **Verified**
against its DLL (decompiled): `key=value`, `#` starts a comment, `tabCount` a whole number (`strtol`, base 10, nothing after it) in 2..9, else
5 with a warning; `activeStyle` `frame` or `dot`. Its `tabCount` becomes the *Tabs* default (a value set on the Settings page wins); the
warnings go to the mod log.

Missing (engine):
- The `dot` style and the `JobTabs_ActiveFrame` skin: a job panel button has no skin of its own, so the lit state stands in for both; a
  `dot` in the cfg is logged. The engine allows 9 job panel buttons across all mods, so another mod's buttons take from them.
- The click sound plays on every press (the original's only when a list changed): the job panel button always clicks.
- Not in the vanilla save (`jt_active`/`pjob` keys): a save taken to the original game loses the other tabs; no hook writes fields into a
  character's orders record.
- **Unknown**: not checked in game, the buttons, their tooltips and the cfg read included.
