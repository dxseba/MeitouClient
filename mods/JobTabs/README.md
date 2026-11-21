# Job Tabs (partial port)

Port of *Job Tabs* (jacobericson/JobTabs, workshop 3813790518), rebuilt from its Workshop description.

- **Tabs**: *Tabs* (4) numbered buttons labelled "Jobs" in the HUD overlay while a player character is selected; the first selected
  character's active tab is lit. A click switches every selected player character to that tab: its job list (`CharacterCold.Jobs`, the entries
  themselves, target handles included) is kept in the active tab and the chosen tab's put in its place, then its next decision is asked for
  (`DecisionSystem.Request`). A new character starts on tab 1 with its current jobs.
- **Saved** with the world (mod sidecar), keyed by the lasting character id (`World.LastingIds`); removing the mod leaves each character with the
  jobs of the tab it was on.
- **Missing** (partial): the buttons beside the Jobs list (the panel has no mod slot: *Buttons X* / *Buttons from bottom* place them), the hover
  list of a tab's jobs, the cfg file. A Bodyguard or Staying-close job restored from the sidecar after a load keeps its saved handle only (the
  engine matches handles when it loads its own job lists; **Unknown** whether it finds the target again from a tab).
- **Unknown**: not checked in game.
