# SlaverMod

Port of *SlaverMod* (Cappuccino-SKT, Workshop 3828708454), from its shipped `SlaverMod.ini` (every key commented), its `lang/_template.txt`,
its `SlaverMod.mod` (read with a scratch FCS dump) and its DLL (Ghidra decompile: imports, strings, the action-name table). The original is a
required item: its `SlaverMod.mod` (collar, shackle kit, the caravan squads and package, the "Slave Hunters" start), `SlaverMod_races.ini` and
`SlaverMod_lines.ini` are used from its folder.

- **Enslave** (order menu: select a member, right-click someone down or caged): the member walks over and locks shackles on (handed a pair when
  it carries none), then the target joins your squad "Slaves" as a slave of your faction. With **shackle kits** on, each enslavement uses up a
  kit (or loose shackles) of the enslaver or a free member nearby; freeing gives a kit back; slave traders sell kits in the trade window.
- **Submission** 0-100 on the character panel: watched slaves (a free, awake member within 500, or caged) gain 2 a day up to 60 (the race's
  cap), caged ones 10 more up to 75; unwatched lose 5; race traits from `SlaverMod_races.ini`.
- **Escape**: below 50, daily 0.5 x (1 - submission/50), x0.1 watched, x0.5 shackled, x the race's; a runaway becomes an escaped slave in a
  squad of its old faction; only strength 50 or lockpicking 40 slips the shackles; recapture costs 10 submission.
- **Punish** (+15, halved, thirded... within a day, and 10 blunt damage on a random limb, more each time), **Reward** (+5 for 100 cats once a
  day), **Free** (90+ stay; the others go home), **Lock in a cage / Let out of the cage**, and **slaves sleep in cages** (21:00 to 07:00, free
  unlocked cages of yours within 3000).
- **Rebellion** every 7 days: free = free members x their best strength, slaves = defiant slaves x theirs; if the slaves weigh more, a chance
  of (slaves - free) / slaves (at most 0.9). Rebels (strength from 10 at submission 0 up to 20 near 50) break their shackles and join the
  Anti-Slavers with an iron club of the worst grade; the Anti-Slavers declare war; rebels never rejoin through dialogue.
- **Slave trade window** on slave traders (and the caravan): buy their slaves (they join your slave squad shackled, at the race's start + 15)
  or sell yours (they leave as the trader's slaves). Worth = (300 + 12 x (strength + toughness + dexterity + melee attack + melee defence +
  labouring)) x race Value x (0.75 + submission / 200), -25% per arm or leg gone or crushed (at least 10% left); buy x2, sell x1. Traders no
  longer pay a fixed 400 for someone you carry.
- **Slave caravan** every 7 days at your first town: a warning a day before; the Slave Traders' caravan comes in, stays 6 hours (kept within
  3000) and leaves.
- **Skeletons** (off by default): an unconscious robot gets Reprogram instead of Enslave: 20% + 0.75% per robotics level (at most 95%).
  Success: a free member of your faction (optionally of `ThrallRace`); failure: destroyed.
- **Protection**: factions not at war with you never fight your slaves (fighters and turrets), and other slavers never process them. Shackled
  slaves can only walk. Speech bubbles from `SlaverMod_lines.ini`. Settings on the Mods page.
- **"Slave Hunters" start**: the original's NEW_GAME_STARTOFF record in its `.mod` (a leader and five hunters at a random slaver camp). Meitou
  reads starts and their `town` lists from the data, so it needs no code.

## Facts (Verified: `SlaverMod.ini` defaults and comments, unless marked)

- The defaults and formulas above (`Submission*`, `Escape*`, `Punish*`, `Reward*`, `Cage*`, `Rebel*`, `Rebellion*`, `Trade*`, `SlaveCaravan*`,
  `Thrall*`, `ShackleKit*`, `RequireShackles`, `DisableCarriedSale`, `ProtectSlaves`, `SparedByNeutrals`, `ShacklesNoRunning`).
- **Verified** (`SlaverMod.mod`): `6-SlaverMod.mod` "SlaverMod slave caravan" (faction `1084-gamedata.base`, slaves `5-SlaverMod.mod`,
  package `7-SlaverMod.mod`); `13-SlaverMod.mod` "Shackle kit" (value 150); `1-SlaverMod.mod` the start (500 cats, the slaver camps' towns).
- **Observed** (decompile): imports of `changeSlaveOwner`, `setChainedMode`, `setSlaveState`, the AI slave finders, `isEnemy`,
  `GunClassTurret::scoreTarget`, `setDesiredSpeedOrders` (the shackled cap sets the speed order to walk) and `Dialogue::_doActions` with the
  action names `ENSLAVE_TARGETS_CARRIED_PERSON` and `GIVE_MONEY`; the caravan's call lines.
- **Unknown**: whether the worth's race and submission factors multiply the base value too (the port multiplies the whole); the original's
  hourly step of submission and escape (the port applies a 24th of the daily rate each game hour); the rebels' refusal text (the port's own).

## Harmony

- `Meitou.Simulation.Player.Orders.TopSpeed` (postfix): the shackled walk cap.
- `Meitou.Simulation.Kidnap.CaptiveSystem.FreeSlaveToProcess` and `CaptiveSystem.ProcessTarget` (postfixes): other slavers leave your slaves
  alone.
- `Meitou.Simulation.Combat.CombatSystem.ChooseTurretTarget` (postfix): turrets of factions not at war with you pass over your slaves.
- `Meitou.Simulation.Dialogue.DialogueSystem.RecruitRefusal` (postfix): rebels never rejoin.
- `Meitou.Simulation.Dialogue.DialogueSystem.RunActions` (prefix): the fixed sale of a carried person does nothing.

## Port

- Shackles go on through the game's `PutShacklesOrder`; slaves join through `PopulationSystem.JoinPlayerSquad`, then `FixtureRegistry.Enslave`
  (owner the player's faction, master the slave squad). Leaving the player (freed, escaped, rebel, sold) uses `DialogueSystem.Leave`
  (reflection) into an NPC squad, as the game's squad split does.
- Fighters' protection is an `ICombatEngageFilter`; cages use `GetInCageOrder` / `GetOutOrder`; the caravan is a `SpawnPlatoon` held in the
  mod's name, walked in with move orders (`World.GiveOrder`) and taken out with `DespawnPlatoon`.
- Punish damage is `MedicalState.PunchDamage`; `ThrallRace` sets the appearance's race (reflection on its init-only property) and rebuilds
  the look (`CharacterRefresh`); the health panel follows after a save and load, as in the original.
- Punish, reward and reprogram act within 100 of the owner, at once (no walk, progress bar or animation). State is saved in the mod's sidecar.

## Missing

- The owner's kneeling, progress bar and animations, the inventory button, the camera on the caravan's arrival, bounty clearing on enslaving,
  the slave food and native slave work options, and the test hotkeys: not ported yet; none needs an engine change.
- **Unknown**: not checked in game.
