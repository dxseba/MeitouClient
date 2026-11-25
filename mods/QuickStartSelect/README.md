# Quick Start Select

Port of *Quick Start Select* (Workshop 3813761725, Nexus 2201), from the original's DLL (`QuickStartSelect.dll`, Ghidra decompile), its
`QuickStartSelect.defaults.ini`, README and catalogues. The original is a required item: its `locale/*/quick_start_select.mo` are read at
runtime from its Workshop folder beside this mod (`AddTranslations`).

- **The list window** replaces "Choose your beginning" (the game's `new-game` window, on the title and from the in-game menu). The game's
  widgets are hidden (screen overrides) and the list is drawn in their place, centred, sized as a share of the screen (wide 74 %, compact 32 %,
  height 86 % by default). Wide: the list on the left, details and options on the right. Compact: a narrow window, details under the list,
  and an "Advanced options" / "Description" button that swaps them.
- **Sorting:** A-Z / Z-A by name, EASY-HARD / HARD-EASY by difficulty. Pressing the active button reverses it. The choice is kept as a setting.
- **Search** by name, ignoring case and ё/е. X clears the search and the race filter. "n / m" shows how many starts are listed.
- **Race filter:** a popup of the races of the starts' people, each with its number of starts.
- **Details:** name, difficulty in its colour, cash, play style, town, characters, relations, then the description (scrolls).
  - A start with several towns shows the town as a button: "Random" (the game's draw) or one town, which is then the only one the game may
    start in. The pick is dropped when the window opens again.
  - Relations open a popup of factions and values.
- **Advanced options:** the game's own options (sliders by 0.05, the ticks) beside the details or in place of them. Pointing at an option's name (or
  pressing it) shows the game's help and its default. A right click resets it to the game's default.
- **Random start** picks a listed start other than the chosen one. **Begin**, or a double click on a row, asks first (setting) and then presses
  the game's own Begin with that start chosen, so the game starts it as usual. In play the game's own question is asked instead.
- **Settings** (Mod settings): the original's ini keys and MCM labels (Enabled, Confirm before starting, Right click resets an option,
  Text colour, Window size, Custom difficulties, Detailed log), plus Layout, Sort and Reverse order, which the window's buttons change.

## Facts (Verified: decompile of `QuickStartSelect.dll` and its string table, unless marked)

- Ini section `QuickStartSelect`. Defaults: Enabled 1, Layout wide, Sort az, SortDesc 0, ConfirmBegin 1, RightClickReset 1, TextColour
  auto, Debug 0, DifficultyVeryEasy … DifficultyVeryHard empty. WideWidth 74 (50–100), CompactWidth 32 (25–70) and WindowHeight 86 (50–100) are
  read with `GetPrivateProfileIntA` and clamped to those ranges.
- Difficulty rank: the text is lower-cased and checked against the custom lists first. Each comma-separated entry is matched as a substring,
  and the six levels rank 10/20/30/40/50/60. Then a word table, the first substring found winning: default 30, обычн 30, very easy 10,
  очень лег 10, easy/hard 38, normal/hard 38, смешан 38, двое 38, hardest 70, very hard 60, очень тяж 60, выше средн 40, harder 40,
  посложнее 40, сложнее 40, dodgy 45, щекотл 45, slightly 35, kinda 35, easier 15, полегче 15, easy 20, лег 20, hard 50, тяж 50, жестк 50,
  normal 30, doable 30, умерен 30, средн 30, нормал 30. No match ranks 30. (Table at `.rdata` 0x18002bc90, wide strings with ranks.)
- Colour by rank, steps at rank below 16/26/33/46/56 and above (RGB):
  - (.55, .74, .45)
  - (.68, .76, .47)
  - (.80, .74, .50)
  - (.85, .62, .40)
  - (.85, .48, .36)
  - (.86, .34, .32)
  - In dark-text mode each channel is multiplied by 0.55 (`FUN_180008120`, `FUN_180009af0`).
- Races: for each start `squad` entry, the characters of its `leader` and `squad` lists, each character's `race` list. A character without one is
  "Human (random)".
- Characters: for each entry, leader plus squad counts (each found reference counts its value, at least 1), plus `num random chars` (below 1
  is 0). The upper bound uses `num random chars max` when larger. Animals are the sum of `animals`, shown as ", animals: %d".
- A start whose squad list is empty is hidden ("has no squad, hidden").
- Text: "Begin a new game with this start?", "Default: %s. Right click resets it.", "Random", "Town:", "Relations:", "All races" and the rest
  are the catalogue's msgids (read from the shipped `.mo`).
- **Observed** (original README): the list opens at the top with the first start chosen; the chosen town is the start's only town for that
  start, and the old list returns at the next opening; Random start never repeats the current start and changes only the start.
- **Unknown**: the original's exact control sizes and fonts, and how it detects a dark interface palette (it reads
  `gui/colours/kenshi_colours.xml` of an interface mod). The port's "auto" is dark text in the Faithful look and light text in Meitou's.

## Harmony

- `Meitou.Game.Title.FrontendScreens.BindNewGame` (postfix): keeps the front-end screens instance. The window then sets its private
  `_startIndex` and calls its `NewGameAction` with the game's `BeginButton` / `CancelButton`, by reflection.
- `Meitou.Simulation.Population.PopulationSystem.StartPlace` (prefix): with a town picked for that start, the start is passed with only that town.
- `Meitou.Game.PlayerInterface.ArrangeUi` (prefix): reads the interface's `Mouse` for hover help (screen controls get no hover).
- `Meitou.Game.PlayerInterface.UiScroll` (prefix): a wheel step over the open window scrolls its list, popup or description and is not
  passed on (screen controls get no wheel).
- Reflection, no patch: `PlayerInterface.AdvancedOptions.NewGameOptions` and `NewGameSettings.Options` / `Ticks` / `Set` / `SetTick` /
  `Ticked` / `Item` for the options, `AdvancedOptions.AdvancedHelpCatalog` for the help, and `PlayerInterface.Confirm` for the question.

## Port choices

- The list also has a bar (arrows, a press or drag on the track) beside the wheel.
- A CHARACTER entry in a start's squad list, which Meitou starts as that many characters, counts in Characters and Races too.
- The rows are the options Meitou's panel has: 7 sliders and 2 ticks. The DLL also holds the labels "Difficult healing", "Animals eat" and
  "Makes life unfair". **Unknown**: whether the game's panel shows them; Meitou has no such options, so the port shows none.

## Missing

- **Unknown**: not checked in game.
