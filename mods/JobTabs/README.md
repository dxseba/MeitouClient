# Job Tabs (partial port)

Port of *Job Tabs* (jacobericson/JobTabs, workshop 3813790518), rebuilt from its Workshop description.

- **Tabs**: *Tabs* (2 to 4, default 4) numbered buttons in a row above the Jobs list (`AddJobPanelButtons`) while a player character is
  selected; the shown character's active tab is bracketed (`[ 2 ]`, the hook has no button style), and each button's tooltip lists that tab's
  jobs by type (no sites). A click switches every selected player character to that tab: its job list (`CharacterCold.Jobs`, the entries
  themselves, target handles included) is kept in the active tab and the chosen tab's put in its place, then its next decision is asked for
  (`DecisionSystem.Request`). A new character starts on tab 1 with its current jobs.
- **Saved** with the world (mod sidecar), keyed by the lasting character id (`World.LastingIds`); removing the mod leaves each character with the
  jobs of the tab it was on.
- **Missing** (partial): more than 4 tabs (the engine allows 4 job panel buttons across all mods; another mod's buttons take from them),
  a lit active-tab button, the sites in a tab's job list, the cfg file. A Bodyguard or Staying-close job restored from the sidecar after a load
  keeps its saved handle only (the engine matches handles when it loads its own job lists; **Unknown** whether it finds the target again from a
  tab).
- **Unknown**: not checked in game, the buttons above the Jobs list and their tooltips included.
