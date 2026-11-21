# Building Search Bar (partial port)

Port of *Building Search Bar* (workshop 3786979230, GPLv3), rebuilt from its Workshop description.

- **Search buildings** (`AddKeyAction`, N by default, rebindable): with the build window open, starts typing a query. Letters, digits, space and
  dash add, Backspace removes, Enter ends typing (the filter stays), Escape clears it; closing the build window clears it too. The keys are
  taken before the game's (a Harmony prefix on `PlayerInterface.UiKey`). A line at the top of the screen shows the query and how many
  unlocked buildings match.
- **Filter**: postfixes on `BuildMenu.Categories` and `BuildMenu.Groups` keep only the unlocked buildings (`BuildingBuildData`) whose name,
  group, category or string id contains the query (any case), and the categories and groups that still have one. Unresearched buildings stay
  hidden, as in the original.
- **Missing** (partial): the original's separate, movable search window with a text box and click-to-pick results (Meitou's mod interface has no
  text box; the query is typed blind into a label), searching the Shift+F12 build mode's list, non-Latin text input.
- **Unknown**: not checked in game: whether the key action fires while the build window is open, and whether the menu re-lists at once.
