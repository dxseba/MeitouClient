# StackSort (port)

Port of *StackSort* (workshop 3813810734). The original's strip `<  StackSort [n]  H/W  >` covers the ARRANGE button of a player character's
inventory and bag windows (`AddScreenControls("inventory" / "bag", "ArrangeButton", ...)`); a press sends the mod's `StackSortArrange`, a paused
command (`IPausedCommand`) that its `StackSortSystem` (`IPausedCommandHandler`) runs through StackSort's cycle (between ticks while paused too):
split stacks merged (*Merge stacks*), then the original's search lays the grid out and the best *Layouts in the cycle* (5) are offered, one
per target (rows kept free at the bottom for Height, columns at the right for Width).

- **Strip**: *StackSort [n]* applies the best layout and further presses cycle forward (`n` the shown layout); `<` and `>` step back and forward;
  *H/W* switches the goal and sorts again; a right click on *StackSort [n]* (`UiPointerEvents.Buttons`) restores the layout before the first press, as
  the original's. The strip's root takes the pointer, so a press between its buttons does not reach the covered ARRANGE (**Unknown** in game). A notice shows `StackSort [N] H`. A cycle ends when the grid changes by other means. No fit: vanilla
  arrange. Every other arrange (`IInventoryArrangeFilter` on `StackSortSystem`: a machine's, container's or trade stock's ARRANGE,
  making room for a held item, the stock grid being built) gets the best layout for the *Width goal* setting, with *Merge stacks* through
  the engine's merge first (`InventoryArrangement.MergeFirst`); a character window's ARRANGE is left to the strip. No Harmony or reflection.
- **The original's files** (a required item, read from its installed folder beside this one): `StackSort.cfg`'s `default_dim` (H),
  `consolidate_stacks` (true) and `top_targets` (5) are the settings' defaults; `arrow_button_width` (30), `dim_button_width` (40),
  `sort_button_width` (180), `button_gap` (2) and `body_x_offset` (-40) lay out the strip; `lang\<code>.txt` gives `SORT_BUTTON` / `DIM_H` /
  `DIM_W` for the game's language (zh_CN / zh_TW, else the code's language part). **Verified** (original cfg, lang files and DLL strings): those
  keys, ranges and defaults, the caption `StackSort [{n}]` with `{n}` the layout number. **Verified** (DLL decompiled): the strip's buttons
  (`Kenshi_Button1`) in the order back, sort, goal, forward, at ARRANGE's top and height, centred on it, shifted by `body_x_offset` for the
  character's own window only; ARRANGE is hidden.
- **Log and cfg checks**: `log_level` (1) is the *Log level* setting's default: 1 writes each sort (items, layouts found, the layout applied,
  an undo, another window's arrange) to the mod log, 2 also each offered target's score and rectangle, 0 none. A cfg line that is a section header, has no
  `=`, names an unknown key or holds a value that does not parse or lies outside its range is logged and ignored, and once a game runs the
  player is told how many (`WARN_CONFIG` from the language file, as the original's one-time message). **Verified** (original cfg comments
  and DLL: the keys, ranges, the -1 of the search budget keys, the one-time `showPlayerAMessage` with the count); the lines are this port's.
- **Search** (`Search/`, rebuilt from the decompiled `Packer::Search::PackAnnealed` as facts, StackSort.md in the research notes): per
  target, a constructive maximal-rectangles layout first (also used to reserve the widest block of the kept rows the items still fit
  around), then restarted late-acceptance hill climbing over item orders, each decoded by the original's skyline with a list of wasted
  rectangles (smallest fitting hole first, else the spot resting highest, then least 3 x shut-in cells - same-record contact, then
  leftmost), scored by the largest empty rectangle (area squared, a bonus when at least the target tall), the free space's concentration,
  stranded cells and the clustering of like and related items (same record type, item function, food crop / trade item flags, measured by
  the original's contact rule). Moves: swap, shift, and the hole move (the first item in order that fits the box around a random stranded
  cell goes to the front). Then path relinking between elite orders (refinement only), a comparison with the constructive packer when a
  block was reserved, and three polish passes that keep the covered cells: reorder stacks of up to 5 like-width items, swap an item with a
  block filling a same-size window, swap same-footprint items. The Width goal searches the transposed grid. Targets 1 .. size - 1 are swept
  warm-started (a target whose block is smaller than an earlier answer's rectangle takes the constructive layout alone), those not already
  ideal refined (at most 64), and ranked by score for the cycle. Budgets are the original's iteration counts (first pass 16 x 4000, plateau
  1500; refine 24 x 8000, history 200, plateau 2000; `first_pass_*` / `refine_*` cfg keys, -1 compiled); no wall clock. One generator,
  seeded from the item count and target, makes every draw in the original's order, so a grid always gets the same layouts. **Verified**
  (decompile, read): the decoder, packer, contact, metrics, score, grouping, move and draw order, hole move, elites, relinking, polish
  passes, sweep, shortcut, refine rule and ranking; the original's caches, pruning bounds and incremental decoding change no result and are
  left out. **Observed** (scratch programs): the decoder matches an independent column-top reading of the same facts on 2000 random cases;
  every layout is valid; runs repeat exactly; over 5 synthetic grids the rectangle holds all or all but 2 free cells (the old heuristic's
  75-96 %), 0.2-1.8 s per grid and goal on 4 workers. Not run against the original DLL. The heuristic (six candidates) remains the fallback
  when no target is complete or a grid exceeds the original's limits (126 cells a side, 255 items).
- **Precompute**: the search reads a copy of the grid (merged on the copy with *Merge stacks*), never the live inventory. While a strip is
  drawn, its grid is swept in the background once unchanged for `mutation_debounce_ms` (150, as steps of the tick length), at most
  `resnapshots_per_frame` (3) grids started per step, `num_workers` (4) sweeps at once and threads per refinement; a press uses the result,
  waits for a running sweep or computes it. Every other arrange searches with `sync_restarts` x `sync_iters` (4 x 1000, elites on as the
  original's synchronous search) and no refinement (grids over 400 cells or 80 items: the heuristic).
- **Missing**: the press timing, by choice. The original, pressed before its background job has stored the shown target, searches that
  target alone with the sync budget, so its layout then depends on how fast the workers ran; this port waits for the sweep (up to about 2 s
  on 4 workers), so a grid always gets the same layout. Layouts match the original's only as far as the facts were read right: nothing here
  was run against the original DLL.
- **Observed** (2026-11-18, windowed sandbox, user play, earlier version on ARRANGE itself): ARRANGE grouped like items; cycling, the goal switch
  and undo work. The strip is not checked in game.
