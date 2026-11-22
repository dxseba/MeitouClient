# PersistentPrisoners (port)

Port of *PersistentPrisoners* 0.2.8 (workshop 3798215878, GPL-3.0-or-later, RE_Kenshi plugin). Rules from its README, `languages.ini` and DLL
(decompiled); its source zip is not in the Workshop item.

- **Keeping**: a Harmony postfix on the population's keep test (`PopulationSystem.Kept`) keeps a platoon that holds a prisoner in a
  player-owned cage (loaded squad or stored members), so it is neither despawned when it unloads nor by the off-screen orphan timer. Meitou's
  own *Squads keep their people* switch (on by default) already keeps most of them; this covers it off and the Faithful rules. Releasing the
  prisoner ends the protection. The original hooks 12 game functions (`setPersistentSquad`, `reCheckPersistenceOnUnload`, `~Platoon`, ...).
- **Player cages**: learned from the loaded fixtures (`FixtureKind.Cage`, owner = the player's faction) with their names and saved with the
  world (mod sidecar v2), so a cage whose zone is out still counts. Any cage-kind fixture counts (cages, poles, BF_CAGE mods).
- **Hunger** (**Verified**, DLL: a hook on `Character::getMagicHungerSetting`, the free-food floor): for a living non-player character whose race
  hungers, carried by a player character: -10 (no free food, it starves); in a player cage: -10 with *Allow starvation* (default on), else
  Minimum Nu (1-300, default 100) / 100, so the game's own free food feeds it towards that. Port: `CharacterCold.FedTo`, set every 30 ticks and
  taken back when it no longer applies. Player squad hunger stays native.
- **Overview** (key action *Prisoner overview*, O by default; the original's default is Ctrl+Shift+P, and P is photo mode here): "Prisoners: N
  Corpses: N Unloaded: N", columns Name, Faction, Cage, Nu (hunger x 100), Condition, Location, Status, sortable by a click on a title;
  conditions Dead, Dying, Unconscious, Alive, Starving (Nu below 100 for a race that hungers) and the "/ Starving" pairs; statuses Player squad,
  Player / Unloaded, Unloaded, Protected; "Unknown" faction, "Unknown location"; the unloaded and feeding hints (**Verified**, DLL strings).
  Port: the player's own characters held anywhere are listed too; location is the nearest town within 3000.
- **Unknown**: the original's Dying test (`MedicalSystem::isProbablyDying`, or two medical fields); the port shows Dying for no blood while
  bleeding. Not checked in game. **Observed** (2026-11-19, headless sandbox fight): an earlier build ran 3000 ticks without a fault.

## Missing

- Caged corpses across an unload: Meitou removes a corpse whose zone is not loaded (`BodySystem.SlowWorld`, "corpse unloaded"); keeping one needs
  an engine hook there (a keep test for held corpses).
- The in-window settings (Apply, shortcut capture, diagnostic logging): settings are on the Mod settings page; translations for the nine game
  languages and the `languages.ini` widths and font height; the "Checking" status.
