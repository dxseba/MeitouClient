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
  photo mode's P; rebindable on the Controls page): "Prisoners: N
  Corpses: N Unloaded: N", columns Name, Faction, Cage, Nu (hunger x 100), Condition, Location, Status, sortable by a click on a title;
  conditions Dead, Dying, Unconscious, Alive, Starving (Nu below 100 for a race that hungers) and the "/ Starving" pairs; statuses Player squad,
  Player / Unloaded, Unloaded, Protected; "Unknown" faction, "Unknown location"; the unloaded and feeding hints (**Verified**, DLL strings).
  Port: the player's own characters held anywhere are listed too; location is the nearest town within 3000.
- **Settings row** (**Verified**, DLL strings and languages.ini [en]): under the overview, "Settings", the "Allow starvation: ON/OFF" toggle,
  "Minimum Nu:" box "(1 - 300)", "Diagnostic logging" and Apply ("Minimum Nu must be a whole number from 1 to 300." / "Saved. Settings are now
  active."); port: Apply sets the Mod settings page's own values (kept as that page keeps them) and logs "PP: Settings saved. ...". Until the
  first gathering the counts line reads "Gathering prisoners ...".
- **Diagnostic logging** (`Diagnostics`, off): "PP [diagnostic]: " lines in the mod log: an occupant of a player cage found, its protection
  added (captive) or a caged corpse protected, its protection removed when it leaves (port: diffed every 30th tick, its own wording).
- **Column widths** (**Observed**, 0.2.8's languages.ini): `[en]` `width.name` ... `width.status` 18/19/15/5/13/18/12, percent of the list;
  read at start from the installed original (its `.local.ini` winning), else the port's own; `font.height` 0 there (the default font).
- **"Checking"** (**Verified**, DLL): the status of a loaded non-player occupant whose protection flag is not set yet; the port decides protection
  when the game asks (`IRemovalVeto`), so it has no such state and never shows it.
- **Unknown**: the original's Dying test (`MedicalSystem::isProbablyDying`, or two medical fields); the port shows Dying for no blood while
  bleeding. Not checked in game. **Observed** (2026-11-19, headless sandbox fight): an earlier build ran 3000 ticks without a fault.

## Missing

- The settings row's shortcut capture and language rows: a mod cannot rebind its key action (Controls page) and there is no per-mod
  translation API, so the nine languages, their widths and font heights are missing (engine gaps).
