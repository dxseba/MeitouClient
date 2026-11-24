# Building Search Bar (port)

Port of *Building Search Bar* (workshop 3786979230, GPLv3), rebuilt from its description, `.po` texts and decompiled DLL.

- **SEARCH window** while the build window is open (also the Shift+F12 level build, which uses the same window): "Search", a text box (up
  to 40 characters), a clear (X) and a Next (>) button, a status line and a results list. Results are the buildings the build window offers
  (its listed categories, their groups and each group's variants; unresearched buildings are not offered) whose name, group, category or
  string id contains the query, ignoring case, in the window's own order, at most 80, each shown as "Name [category]". Clicking one, or Next
  (wrapping round, its row scrolled into view: `UiRegistration.ScrollTo`), chooses that building in the build window's lists (category, group, variant), as clicks there would; the status reads
  `n` matches, or `i/n` once one was chosen. The build window's own lists are not filtered. The caption's fold button shrinks the window to
  a SEARCH button, which opens it again. Closing the build window clears the search.
- **Place**: as the original, the window's left edge at the build window's (at least 6 px), its bottom 8 px above the build window's top
  (its top at least 20); folded, the button 30 px right of that edge and 28 px above the top. Dragging the caption moves the window (and the
  folded button) from there for the session. Without the build window's place the window sits at the left.
- **Hooks**: `ModContext.Interface` (`IsOpen` / `ScreenBounds(ModScreen.Build)`, polled in `AddFrame`; `BuildEntries`, `ChooseBuild`); the
  results are a `ScrollView` (bar and wheel), back at the top for each new query. No Harmony or reflection.
- **Verified** (original DLL, decompiled): widget names and sizes (window `BuildSearch_Window` `Kenshi_WindowCX` 468x430 captioned
  "SEARCH"; toggle `Kenshi_Button1` 170x26; label "Search" 16,16 56x24; `Kenshi_EditBox` 92,16 286x30; clear 384,16 and next 420,16 30x30;
  status 16,52 436x22; results `Kenshi_ListBoxEmpty` 16,76 436x306); the placement rule above; the list cap of 80; a result's text is the
  name plus ` [` field `]` when that field is set (the category, below); the status `i/n` or `n`; Next wraps; the index is rebuilt from the build window's visible
  categories and groups; the haystack is lower-cased (ASCII `tolower`). Translations (en, de, es, fr, ja, ko, pt, ru, zh) hold only
  "SEARCH" and "Search".
- **Bracketed field**: **Observed** (decompiled DLL): a result keeps the category name the index copied from the build window's category
  object and its group's list row name separately; the row text appends the category (` [` ... `]`, only when not empty).
- **Translations**: `context.AddTranslations("search_bar", <installed original>)` (`OriginalFolder`: the Workshop folder 3786979230 beside
  this one, else the code-less mod "Search bar") reads the original's catalogue for the game's language, so the window's "SEARCH" and
  "Search" show through it; nothing of it is shipped. **Verified** (installed `locale/*/LC_MESSAGES/search_bar.po`: two messages each, e.g.
  de "SUCHE" / "Suche"); shown in game **Unknown**.
- **Missing**: nothing known.
- **Unknown**: look and typing in game untested.
