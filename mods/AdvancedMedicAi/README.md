# Advanced Medic AI (port)

Port of *Advanced Medic AI 2.0* (Svarog, workshop 3742288543, `AdvancedMedicAI.dll` v2.0.0), from the original's DLL (Ghidra decompile), its
ini files, changelog and description. The original needs KenshiExtensionPlugin (KEP) only for its settings tab; the port does not port KEP: its
settings are on the Mods page.

- **Pulse** (`MedicSystem`, a tick system at the end): every 15 s of game time, unless orders went out in the last 15 s, the player characters
  with the Medic job and a kit get the game's First Aid order on downed characters nearby (`World.GiveOrder`, the player's order queue: the
  same as a right-click Heal), one patient per medic. A notice "Medic AI: medics sent to give first aid: N".
- **Your orders win**: an order a player character gets that the mod did not give (the player's, or the engine's own) leaves that character
  alone for *Respect manual orders* seconds (Harmony postfix on the order queue).
- **Faction rules** (*Faction rules...* button): a window listing the data's factions with Use general rule / Always heal / Never heal; rules
  for factions the data lacks are kept and listed last.
- **Texts**: English; Ukrainian and Russian read at start from the installed original's DLL (`OriginalTexts`), for `uk` / `ru` locales.
- **Tuning** (`MedicTuning`): the original's `AdvancedMedicAI.ini`-only keys are read at start from the installed original's ini; a key it
  lacks keeps the DLL's default.

## Facts (Verified: decompile of `AdvancedMedicAI.dll`, `startPlugin`, its four hooks and the pulse `FUN_180011ad0`, unless marked)

- Hooks `Character::addOrder` (manual orders, door orders), `GameWorld::_NV_mainLoop_GPUSensitiveStuff` (clock and pulse), `resetGame` and
  `loadAllPlatoons` (state cleared). Time is the summed frame time passed to the main loop.
- **Medics**: player characters, with the player, able to take orders, not in combat (unless combat medic), not down (vfunc `0x30` or
  `isDown`), not carrying (`+0x348`), not ordered by hand within the grace, with job 58 in the perma-job list (`requireMedicJob`, default on),
  a kit of function 1 (organic) or 12 (robot) by `Inventory::hasItemFunction`, else in any item's own inventory. A medic is used at most once
  per 5 s (`perActorCooldownSeconds`).
- **Patients**: characters down (vfunc `0x30` or `isDown`), not a medic, not carried; then the faction filter; then the building filters
  (inside a player building kept, a foreign one skipped, an unknown one kept: defaults); then the need: `scoreFirstAidNeed` organic above 0.1,
  or robot above 0 for a robot race (`+0x7c`); then reach: some medic holding the patient's kit within the field radius (robot radius for a
  robot) or, with *Whole base in reach*, in the same player town (the medic's town object with the player flag `+0x250`); then not treated in
  the last 45 s and not blocked. Row 25 (`FIRST_AID_ORDER`), 60 (`FIRST_AID_ROBOT`) for a robot.
- **Groups** (`FUN_18001ba50`): a player character 0; an animal (vfunc `0x248`) 3; else by the first medic's `isEnemy` (4), `isAlly` (1),
  else neutral 2. A faction rule (by the faction's string id) Always heal takes the patient whatever its group's switch, Never heal skips it;
  player characters have no faction rule.
- **Assignment** (`FUN_18001b3c0`): patients sorted by group, then need; each takes the nearest remaining medic (3D) that holds its kit and
  reaches it; with *Avoid hazards*, a patient under acid rain, fire or gas (`getCurrentWeatherAffectStatus` 2 to 4) needs a medic whose race
  is immune (`RaceData::isImmune`); a medic in combat only for a patient of group 0 or 1 within the combat radius. One order per medic per
  pulse (`enableMultiMedicMode`, `maxOrdersPerPulse` 0).
- **Kit fallback** (`allowMedicJobKitFallback`, DLL default off): a patient no medic holding its kit reaches is still taken when some medic
  holds that kit and any medic reaches it; such a medic (`FUN_180013860` false) may then be sent without the kit. Medics are listed whatever
  kits they hold.
- **ini keys** (DLL parser, lowercased keys; defaults from the DLL's data): `autoPulseEnabled` true, `autoPulseIntervalSeconds` 15,
  `orderCooldownSeconds` 15 (both below 1 become 15), `unitsPerMeter` 10, `recentTargetCooldownSeconds` 45 and `perTargetCooldownSeconds` 45
  (the larger applies), `perActorCooldownSeconds` 5, `requireMedicJob` true, `minFleshyFirstAidNeedScore` 0.1, `minRobotRepairNeedScore` 0,
  `skipTargetsInsideForeignBuildings` true, `allowTargetsInsidePlayerBuildings` true, `skipTargetsInsideUnknownBuildings` false,
  `enableMultiMedicMode` true (off: one order per pulse), `maxOrdersPerPulse` 0 (no limit), `allowMedicJobKitFallback` false,
  `enableDoorBlockedTargetCooldown` true, `doorBlockedTargetCooldownSeconds` 300, `doorInteractionDetectionWindowSeconds` 20,
  `enableStuckTargetCooldown` true, `stuckTargetCooldownSeconds` 300, `afterOrderCheckSeconds` 5, `minApproachProgressSq` 1000,
  `minApproachProgressRatio` 0.05, `minDistanceSqForStuckCheck` 10000, `debugLogging` false. **Observed**: the shipped ini sets
  `allowMedicJobKitFallback=true`, so with the original installed the fallback is on, as in the original.
- **Blocking**: a door order (rows 0x48 to 0x55, 0x7b, 0x8c, 0x9b, 0xa2, 0xa3, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xe2, 0x11d to 0x11f, 0x122)
  given to a medic within 20 s of its medical order blocks that patient 300 s; 5 s after an order, a medic that started at least 100 units
  away (10000 squared) and came less than 1000 squared and less than 5 % closer has its patient blocked 300 s. Door orders are no manual
  orders.
- **Defaults** (the DLL's own data, used without an ini): treat player, allies, neutrals on; enemies, wild animals off; whole base on; field
  radius 50 m, robot 100 m (10 units per meter); combat medic off, radius 15 m; avoid hazards on; manual grace 60 s; notifications on; pulse
  and order cooldown 15 s; target cooldown 45 s. Slider ranges 10-300 m, 5-50 m, 0-300 s. **Observed**: the Workshop folder ships an
  `AdvancedMedicAI_TargetFilters.ini` with enemies and animals on and radii 91.9 / 183.8 m (the author's own values; the description says
  enemies and animals are off by default); the port takes the DLL's defaults.
- **Texts**: each text is a row of three pointers (English, Ukrainian, Russian; the language index from the game's `settings.cfg`); dumped and
  checked against the decompile's table at `0x1800485d0`.
- **Observed**: builds. Play is **Unknown**: not run in game.

## Port choices

- The time base is game time (ticks), so the pulse follows the game speed and stops while paused.
- "Manual order": every order queued through `PlayerOrders.Give` for a player character, as the original hooks every `addOrder`. **Observed**
  (reading the engine): the player's right-click orders, moves and many engine orders go through it; a job the AI starts does not.
- Indoors is Meitou's sight map (`SightMap.BuildingOf`); a player-built building counts by its owner, a town building by the player's
  ownership.
- An order the game would refuse now (`World.CanOrder`) is not given; row 60 falls back to 25 when Meitou's queue does not own it.
- A kit in a bag carried in the inventory counts, as in the original; Meitou's First Aid order then looks for its kit itself and may refuse it.
- The ini's in-game keys (`treat*`, radii, `combatMedic`, `language` and the legacy `skipActorsInCombat`, `fleshyRadiusSq`, `robotRadiusSq`)
  are not read: those are the Mods page's. `skipTargetsInsideUnknownBuildings` acts on nothing: every room on Meitou's sight map has a building.
- `debugLogging` logs one line per order given.

## Harmony

- `Meitou.Simulation.Player.PlayerOrders.Give` (postfix): manual and door orders.

## Missing

- Nothing needs engine changes. **Unknown**: whether the overlay window shows while the Settings screen is open; it shows over the game
  once that closes.
