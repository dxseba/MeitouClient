# Job Tabs (port)

Port of *Job Tabs* (jacobericson/JobTabs v0.1.0, workshop 3813790518).

Facts, **Verified** from the original DLL (decompiled strings and code) and its shipped `JobTabs.cfg`:
- `tabCount` 5 (2-9) tabs in a column beside the Jobs list; `activeStyle` `frame` (skin `JobTabs_ActiveFrame`) or `dot` (tick buttons).
- A click switches every selected character's job list (hooks on `OrdersPanel`), refreshes the orders and plays `General_Click` when one changed.
- Tooltip: "Tab N: K jobs" ("job" for one), then up to 8 job names ("\n..." for more); a job's name is its task name, plus ": " and the
  target's name for a job per target. An empty tab: "Empty: hold Shift while giving an order to add jobs".
- Saved in the vanilla save, in each character's orders record (`OrdersReceiver::serialise`): keys `jt_active`, `pjob…`, `pjobT…`.

Port: *Tabs* (2 to 4, default 4) numbered buttons in a row above the Jobs list (`AddJobPanelButtons`) while a player character is selected;
the active tab is bracketed (`[ 2 ]`). A click switches every selected player character: its job list (`CharacterCold.Jobs`, the entries
themselves) is kept in the active tab and the chosen tab's put in its place, then its next decision is asked for (`DecisionSystem.Request`).
A new character starts on tab 1 with its current jobs. Saved in the mod sidecar (version 2), keyed by lasting character id; a kept job per
target keeps its target's lasting id and names the target again when restored, also after a load. Removing the mod leaves each character
with the jobs of the tab it was on.

Missing:
- 5 and more tabs (the engine allows 4 job panel buttons across all mods; another mod's buttons take from them), the column beside the
  list, the active frame or dot, the cfg file (the Settings screen has *Tabs*).
- Not in the vanilla save (`jt_active`/`pjob` keys): a save taken to the original game loses the other tabs.
- **Unknown**: not checked in game, the buttons and their tooltips included.
