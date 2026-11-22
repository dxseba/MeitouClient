# Building Search Bar (partial port)

Port of *Building Search Bar* (workshop 3786979230, GPLv3), rebuilt from its Workshop description.

- **Search window**: while the build window is open, a small window at the top of the screen holds a text box (`UiNodeKind.TextBox`, up to 40
  characters), how many unlocked buildings match, and an X button that clears the query. Click the box to type; Enter or a click elsewhere ends
  typing. Closing the build window clears the query.
- **Filter**: postfixes on `BuildMenu.Categories` and `BuildMenu.Groups` keep only the unlocked buildings (`BuildingBuildData`) whose name,
  group, category or string id contains the query (any case), and the categories and groups that still have one. Unresearched buildings stay
  hidden, as in the original. Whether the build window is open is read from `PlayerInterface.OpenScreens` by a postfix on `BindUi`.
- **Missing** (partial): moving the search window, click-to-pick results, searching the Shift+F12 build mode's list.
- **Unknown**: not checked in game: the window's look and place beside the build window, that typing reaches the box and not the game's keys,
  whether the menu re-lists at once, and non-Latin text input (the box takes what MyGUI's edit box takes).
