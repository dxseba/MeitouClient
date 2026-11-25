# Kenshi Cheat Menu

Port of *Kenshi Cheat Menu (9 Languages)* 1.11.2 (Kandro78, Workshop 3833747223), from the original's DLL (`CheatMenu.dll`, Ghidra decompile, and
its `.data` tables read with a script), its README and Workshop description. The original is a required Workshop item: the port reads its
translations from it at start. Its plugin is never loaded.

- **F8** (key action "Cheat menu", rebind on the Controls page or with the window's Menu key button; Esc cancels) opens a window in the game's
  skins (`Kenshi_WindowCX`, `Kenshi_Button1`). The main page holds Quick cheats, the category buttons and the menu key; each category is a page
  with a Back button. Esc closes a page, then the window.
- **Quick cheats:** God mode (squad), No hunger, No encumbrance, Heal squad, +10,000 cats, Set cats to the amount typed.
- **Squad:** Weather immunity, Perfect stealth, Weightless items, Infinite consumables; Move speed, Damage and Skill XP multipliers, XP per
  skill (one multiplier per stat); Restore lost limbs, Wake up, Fill hunger, Clear bounties, Free slaves and remove shackles; Max squad size.
- **Selected character:** Max skills, Edit skills (type a level, click a skill), Edit appearance and race (the character editor), Rename
  character or squad (type the name), No hunger for the selected, Teleport to the camera, Clone, Animal: next age stage, Recruit (into the active
  squad or a new one), Knock out, Remove limbs, Kill, Restock trader, Despawn.
- **Base and research:** Construction, Production and Research speed, Instant research, Free research (no artifacts), Learn all blueprints,
  Free construction (no materials), Generators (full power, no fuel), Fill storages, Finish buildings, Grow farms, Ruin buildings and Repair
  ruins near the camera.
- **World and travel:** Travel to town (searchable list), Reveal all towns, Game speed (1, 5, 10, 20), Skip to morning (7:00) or night (21:00),
  Next weather here, Unlock doors near the camera, Spawn squad at camera (searchable list of squad templates).
- **Spawn items:** tabs Items, Weapons, Armour, Other; search; a quality grade (Prototype to Masterwork), or for weapons a manufacturer and a
  level (1-99); Give 1 / Give 10 to the first selected character.
- **Game rules:** Bleeding, Blood recovery, Wound healing, Knockout time, Time until starving, Item prices, Carry weight, Prison time.
- **Factions:** +25 / -25 per faction (both ways, held to -100..100), Ally with all factions.
- **Fun modes:** 100-skill beasts, weaklings, invincible, fast (x3) or strong (x10) NPCs, Random skills, Chaos (random relations every 60 s),
  Raids (a random squad every 180 s at the camera), All factions at war, Everyone hates you; Heal, Recruit, Knock out, Kill or Remove limbs of
  everyone within 300 of the camera.
- **Experimental:** Level editor, Free camera, Next season here, Set time to the typed hour, Remove every NPC's limbs, Take over town of
  selected, Town alarm on / off (selected).

Switches and multipliers are kept with the save (the mod's save data) and start off in a new game; the original kept them while the game ran,
across loads, and never saved them. Changes the buttons make (skills, money, relations, items, recruits, limbs, research) stay in the save, as
in the original.

## Facts (Verified: decompile of `CheatMenu.dll` 1.11.2 and its data tables, unless marked)

- Texts: one table of rows of nine string pointers in `.data`, English then `de_DE`, `es_ES`, `fr_FR`, `ja_JP`, `ko_KR`, `pt_BR`, `ru_RU`,
  `zh_CN` (the locale table just before it); the language is the game's `settings.cfg` `language=`. A text not in the table shows in English.
- Menu key: default VK 0x77 (F8), saved as `menu_key=<vk>` in `Kenshi\CheatMenu_key.cfg`. The port keeps the key with the game's bindings.
- Multipliers (caption `%s: x%g`, "off" at the first value): Construction, Production, Research speed, Skill XP and Damage 1, 2, 5, 10, 50, 100;
  Move speed 1, 2, 5, 10; Game speed 1, 5, 10, 20 (`GameWorld::setGameSpeed`).
- Game rules ("normal" at the first value), each a factor on fields of the game's constants: Bleeding 1, 0.5, 0.25, 0.1, 0 (offset 4); Blood
  recovery 1, 2, 5, 10 (60); Wound healing 1, 2, 5, 10 (108, 112); Knockout time 1, 0.5, 0.25, 0.1 (100); Time until starving 1, 2, 5, 10 (140);
  Item prices 1, 0.5, 0.25, 0.1, 2, 5, 10 (200 to 224 and 160); Carry weight 1, 2, 5, 10 (300); Prison time 1, 0.5, 0.25, 0.1 (364). Max squad
  size: off, 50, 100, 200 (offset 0x178). The port maps them by name: `BleedRate`, `BloodRecoveryRate`, `HealRateMult` and `RestingHealRateMult`,
  `KnockoutTimeBase`, `CarryWeightMult`, `PrisonTime`, `MaxSquadSize`; Time until starving and Item prices are filters. **Unknown**: whether
  offsets 100 and 108/112 are exactly those Meitou fields (inferred from the field order).
- God mode: once a frame every player character's parts get their maximum health and 0 at `+0x44`, blood its maximum (`getMaxBlood`).
  Invincible NPCs does the same to every other character. No encumbrance: `calculateEncumberanceMult` answers 1 for player characters.
- Every NPC is a 100-skill beast / a weakling: once a second stats 1 to 38 of every non-player character set to 100 / 1.
- Strong NPCs: an NPC's attack damage (cut, blunt, pierce) x10; Damage (squad) multiplies a player character's. Fast NPCs: max run speed x3;
  Move speed multiplies a player character's (`calculateMaxRunSpeed`).
- Skill XP: `increaseStat` called the multiplier's number of times.
- Weather immunity: `gettingAcidRain`, `gettingAcidWater`, `gettingAcidFeet`, `gettingGassed`, `gettingWindyface`, `gettingBurnt` answer false
  for player characters.
- Factions: each button adds +25 or -25 to the relation, held to -100..100, and sets it both ways.
- Near the camera: radius 300 (the character and building sweeps; 150 for one other sweep).
- Raids: every 180 s a random SQUAD_TEMPLATE that has a faction; Chaos: every 60 s; Free construction: every second.
- Item spawner: tabs Items, Weapons, Armour, Other; grades Prototype, Shoddy, Standard, High, Specialist, Masterwork; weapons take a
  manufacturer and Level (1-99); Give 1, Give 10.
- XP per skill: one multiplier per stat (1, 2, 5, 10, 50, 100; stat 14 has none); a gain repeats the larger of it and Skill XP.
- Perfect stealth: `notifyICanSeeYouSneaking` is skipped when exactly one of looker and sneaker is a player character.
- Weightless items: each inventory section's weight multiplier 0 for player characters. Infinite consumables: 1,000,000 charges added to the
  item around `drainMedkit`, `eatItem` (not a limb, item function 17) and `reloadCheck`, then taken back.
- Free slaves: for each player character, the slave owner cleared, the slave AI job off, the chained-mode shackles removed.
- Free construction: every second the player's unfinished buildings within 1000 of the camera get all their materials delivered (Finish
  buildings does the same at 300 and completes them). Generators: output max(output, the machine's +0x3bc) and fuel use 0 for player machines.
  Free research: two research cost checks forced true. Fill storages and Grow farms: radius 300 (`FarmBuilding::timeSkip(farm, +0x53c, true)`).
  Ruin and Repair: radius 150, any faction. **Unknown**: what `+0x53c` holds (the port skips growth time / yield) and the original's "can be
  ruined" test (the port skips walls and gates).
- Unlock doors: objects within 300 of the camera, each lock's locked byte cleared. Next weather: the camera region's weather list moved to its
  next entry. Next season: the season index stepped, its end set to now plus its length. Restock: the clicked NPC's platoon
  `_forceRefreshInventory`. Take over: the selected character's town set to the player's faction. Town alarm: the town's alarm set on or off.
  Free camera: `CameraClass::setFreeCameraMode`. Level editor: `PlayerInterface::toggleLevelEditorOnSelectedTown`.
- **Observed** (README): skip time, weather and season buttons need the Steam 1.0.65+ executable.

## Port choices

- Most one-shot buttons are the game's own debug commands (`DebugHeal`, `DebugMoney`, `DebugWake`, `DebugClearBounties`, `DebugMaxSkills`,
  `DebugSetStats`, `DebugTeleport`, `DebugKnockOut` for 60 s, `DebugKill`, `DebugUnlockResearch`, `DebugSetRelation`, `DebugAnimalAge`,
  `DebugGiveItem`). While paused they wait for the next tick, except Give items; the cheat system's own commands apply at once.
- Learn all blueprints finishes every research (`DebugUnlockResearch`), not only those with a blueprint item.
- Armour and gear grades are qualities 0, 20, 40, 60, 80, 100. **Unknown**: the original's grade-to-quality mapping.
- Skill XP multiplies the amount of each gain (held to the curve's 20 per gain), not repeated gains.
- Production and research speed scale an operator's time step at the machine, so the work's experience scales too.
- Clone makes a new character of the same record in the squad, not a copy of the selected one.
- Recruit uses the engine's join (`PopulationSystem.JoinPlayerSquad`), with no dialogue line needed; the squad caps still apply.
- Random skills: each stat 1 to 100 from the world's seed. **Unknown**: the original's range.
- Animal: next age stage adds 0.25 to the age, wrapping from 1.5 to 0.25. **Unknown**: the original's stages.
- Despawn removes NPCs only (not the player's).
- Unlock doors also unlocks containers and cages within 300 (`Fixtures.SetLocked`).
- Take over replaces the town's record with one naming the player's faction (`PopulationData.SetReplacement`), kept with the save and applied
  again after a load; the town's people keep their own faction.
- Town alarm raises Meitou's town alarm (crime Assault, its 8 game seconds); off ends the town's alarms.
- Restock makes the NPC squad shop's restock due now; the trade system refills it on its next pass.
- Next weather and Next season act on the host's weather for the camera's region (Next season makes the season due now); the world reads it
  as before, as the original's weather manager.
- The level editor opens Meitou's own (Shift+F12) editor, not on the selected town.
- Free slaves uses the game's own `DA_REMOVE_SLAVE_STATUS` step (`FixtureRegistry.FreeSlave`): its "Free!" bark and the shackles' lock broken.
- Weightless items and Infinite consumables follow the player's characters' inventories as of the last tick.
- Free research marks the research paid before the game's own cost test; a research not queued after all is unmarked.
- A ruined player site is marked destroyed and becomes a half-built ruin at its next builder's touch; storages that name no goods are not
  filled.
- Remove limbs makes every arm and leg still the body's own a stump (`MedicalState.Sever`), then refreshes the gear.

## Harmony

- `Meitou.Simulation.Bodies.BodySystem.ActOn` (postfix): Move speed and Fast NPCs scale the top speed the body step just set.
- `Meitou.Simulation.Production.ProductionSystem.WorkStep` (prefix): Production and Research speed, Instant research.
- `Meitou.Simulation.Construction.ConstructionSystem.BuildStep` (prefix): Construction speed.
- `Meitou.Simulation.Weather.WeatherSystem.RegionWeather`, `LocalEffects`, `AcidUnderfoot` (prefixes): Weather immunity. A player character
  skipped there also loses the weather's sight and notice effects. **Unknown**: whether the JIT inlines these three into `Update`.
- `Meitou.Simulation.Perception.SightMap.Sees` (prefix): Perfect stealth.
- `Meitou.Simulation.Items.Inventory.ContentWeight(bool)` (postfix): Weightless items.
- `Meitou.Simulation.FirstAid.FirstAidSystem.Drain`, `Meitou.Simulation.Items.FeedSystem.Eat` (prefix and postfix),
  `Meitou.Simulation.Combat.Ammunition.UseOne(Inventory, string, out bool)` (prefix): Infinite consumables.
- `Meitou.Simulation.Production.ProductionSystem.Queue` (prefix and postfix), `ProductionSystem.Affordable` (postfix): Free research.
- `Meitou.Simulation.Production.ProductionSystem.BurnFuel` (prefix), `ProductionSystem.Output` (postfix): Generators; each tick the system also
  keeps their fuel full, should the JIT inline `Output`.
- `Meitou.Game.Host.WorldSession..ctor` (postfix): keeps the session's `GameClock` for Skip and Set time.
- Reached by reflection on the host thread (no patch): `Meitou.Game.Mods.ModFrame._host`, `GameHost.Gpu.Weather`, `GameHost.Session`,
  `Meitou.Game.Host.Cameras.CameraRig.Toggle`, the `Meitou.Data.World.WeatherRegion.SeasonEndDay` setter, `LevelEditorHost.OpenLevelEditor`.
- Called by reflection (no patch): `ConstructionSystem.Finish` (Finish and Repair buildings), `ProductionSystem.TimeSkip` (Grow farms),
  `Locks.FixtureRegistry.SetRuin` (town buildings), `ResearchState._paid` (Free research), the init-only setters of `GameConstants` (Game rules).

## Missing

- **Unknown**: not checked in game.
