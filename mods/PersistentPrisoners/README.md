# PersistentPrisoners (port)

Port of *PersistentPrisoners* 0.2.8 (workshop 3798215878, GPL-3.0-or-later, RE_Kenshi plugin). Rules from its README, `languages.ini` and DLL
(decompiled); its source zip is not in the Workshop item.

- **Keeping**: an `IRemovalVeto` answering `PlatoonDespawned` and `PlatoonOrphaned` keeps a platoon that holds a prisoner in a player-owned
  cage (loaded squad or stored members), so it is neither despawned when it unloads nor by the off-screen orphan timer. Meitou's
  own *Squads keep their people* switch (on by default) already keeps most of them; this covers it off and the Faithful rules. A corpse in a
  player cage is kept when its zone unloads (`IRemovalVeto`, `CorpseUnloaded`); its 12-hour decay is the game's. Releasing the prisoner (or
  taking the corpse out) ends the protection. The original hooks 12 game functions (`setPersistentSquad`, `reCheckPersistenceOnUnload`, `~Platoon`, ...).
- **Player cages**: learned from the loaded fixtures (`FixtureKind.Cage`, owner = the player's faction) with their names and saved with the
  world (mod sidecar v2), so a cage whose zone is out still counts. Any cage-kind fixture counts (cages, poles, BF_CAGE mods).
- **Hunger** (**Verified**, DLL: a hook on `Character::getMagicHungerSetting`, the free-food floor): for a living non-player character whose race
  hungers, carried by a player character: -10 (no free food, it starves); in a player cage: -10 with *Allow starvation* (default on), else
  Minimum Nu (1-300, default 100) / 100, so the game's own free food feeds it towards that. Port: `CharacterCold.FedTo`, set every 30 ticks and
  taken back when it no longer applies. Player squad hunger stays native.
- **Overview** (key action *Prisoner overview*, Ctrl+Shift+P by default as in the original, a chord taken before
  photo mode's P; rebindable on the Controls page or the window's shortcut row): "Prisoners: N
  Corpses: N Unloaded: N", columns Name, Faction, Cage, Nu (hunger x 100), Condition, Location, Status, sortable by a click on a title;
  conditions Dead, Dying, Unconscious, Alive, Starving (Nu below 100 for a race that hungers) and the "/ Starving" pairs; statuses Player squad,
  Player / Unloaded, Unloaded, Protected; "Unknown" faction, "Unknown location"; the unloaded and feeding hints (**Verified**, DLL strings).
  Port: the player's own characters held anywhere are listed too; location is the nearest town within 3000.
- **Settings row** (**Verified**, DLL strings and languages.ini [en]): under the overview, "Settings", the "Allow starvation: ON/OFF" toggle,
  "Minimum Nu:" box "(1 - 300)", "Diagnostic logging", the shortcut ("{shortcut}  |  Change", a click: "Press a shortcut (Esc: cancel)") and
  Apply ("Minimum Nu must be a whole number from 1 to 300." / "Saved. Settings are now active."); port: the capture takes the next key with the
  modifiers held (`AddKeyInput`; a modifier alone waits, Esc cancels), Apply binds it (`ModKeyAction.Rebind`, kept as the Controls page keeps
  it; refused: "Invalid keyboard shortcut."; none: "Choose a shortcut first.") and sets the Mod settings page's own values, and logs "PP:
  Settings saved. ...". The shortcut shows the language's Ctrl/Shift/Alt/Tab names joined by "+" (**Observed**, the port's reading of those
  keys). Until the first gathering the counts line reads "Gathering prisoners ...".
- **Language** (**Verified**, languages.ini header: the game's startup language, no selector in the window; `settings.language` rows only for
  older builds): every window text, the column weights and `font.height` come from the installed original's `languages.ini` (its `.local.ini`
  winning) for `context.Language`: the section of that code ignoring case, else the first of the same language part (`en`, `de` short ids),
  over `[en]`, over the port's English (`OriginalTexts`; nothing shipped). The section rule is the port's (**Unknown** in the DLL).
- **Diagnostic logging** (`Diagnostics`, off): "PP [diagnostic]: " lines in the mod log: an occupant of a player cage found, its protection
  added (captive) or a caged corpse protected, its protection removed when it leaves (port: diffed every 30th tick, its own wording).
- **Column widths** (**Verified**, 0.2.8's languages.ini and its comments): `width.name` ... `width.status` weights 1-1000 (18/19/15/5/13/18/12
  in every section), shares of the list, each column at least 24 px; else the port's own. `font.height` 0 keeps the font size, 10-32 sets the
  text height (Korean 28): port: text scale = height / the default font's measured line height, rows at least height + 6.
- **"Checking"** (**Verified**, DLL): the status of a loaded non-player occupant whose protection flag is not set yet; the port decides protection
  when the game asks (`IRemovalVeto`), so it has no such state and never shows it.
- **Dying**: `MedicalSystem::isProbablyDying` (`MedicalState.ProbablyDying`, **Verified** in the engine) or two medical fields the port reads
  as no blood while bleeding (**Unknown** what they are). Not checked in game. **Observed** (2026-11-19, headless sandbox fight): an earlier
  build ran 3000 ticks without a fault.

## Missing

- The second Dying field pair's meaning (above); nothing else known.
