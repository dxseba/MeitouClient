# Dynamic Wasteland

Port of *Dynamic Wasteland* 1.2.0 (Workshop 3788937435): the in-game side of its geopolitical simulation, from the original's native
`DynamicWasteland.dll` (Ghidra decompile), its `DynamicWasteland.ini`, `Heroes.ini` and strings. The original's data mod
(`DynamicWasteland.mod`: the Rising Heroes, their squads, dialogue and starts) loads as any data mod when the original is enabled.

- **Nations**: every faction that holds a town or village (not the player's, not `not real`, not excluded), with military, economy,
  stability, war exhaustion, aggression, legitimacy, a treasury, a government, a state faith and a leader.
- **Wars**: every `TickHours` (12) game hours random pairs may declare war by the original's formula (+4 for a holy war between a militant
  faith and another). War sets both factions' war flags, so their people fight on sight; up to two allies of the defender join.
- **Offensives and conquest**: a nation at war sends one of its ASSAULT_TOWN campaigns against the enemy's nearest town (`RequestCampaign`);
  the battle is decided when the army arrives (hired mercenaries add power). A won town changes owner once it is not loaded
  (`ApplyTownVersion` with an occupied TOWN version made in the data phase), is held against the game's overrides and garrisoned with the new
  owner's squads; battles kill part of the town and send refugees to the old owner's towns.
- **Peace and alliances**: truces of Truce days, halved exhaustion; friendly nations swear mutual defence; diplomatic marriages between
  ruling houses raise relations. Wars that dialogue declares or ends between two nations are followed (relation observer).
- **Food and caravans**: villages farm, towns eat (1 food feeds 10 a day); a hungry town gets a caravan of 200 food from the nation's
  best-fed town, walked by a real squad of the nation's trader template (spawned, held and sent by the mod; delivered on arrival) or arriving
  after the route's length; war raiders take part of it. Famine starves people and raises unrest; military and stability fall.
- **Population and migration**: towns grow with food; unrest of 30 sends 5 % of the people to the nation's calmest fed town.
- **Mercenaries**: up to four free companies; nations at war hire them; unpaid three times they leave or switch sides. The player can found a
  company and hire it out to a nation (paid each step).
- **Crises** (one at a time, 90 days apart, 3 % a day): drought, plague, exodus, trade boom, mine collapse, slave revolt, beasts (a real
  Wildlife squad near the town), ruin find, charismatic tribune, black famine; each with its effects for 20-40 days.
- **Governments**: monarchy, theocracy, republic (elections every 120 days), oligarchy, dictatorship, tribal confederation; reforms,
  coups under low stability, succession when a leader dies of age (a year per 40 days), civil wars (the nation's rebel faction seizes a third
  of its towns and fights it), disloyal governors breaking villages away as free cities, goat ministers.
- **Religion**: state faiths seeded from the factions (Okranism, Kral, the hives', Narko), towns converting to their owner's faith,
  persecution unrest, schisms of weak faiths, NPC cults (150 days apart), the player's own faith (merit 150, influence 50).
- **Emergent factions**: famine, unrest, cults, revolts and free cities raise one of twelve pool factions with warbands near the town; it
  takes a village after a while, and ends when its warbands are gone.
- **Career**: enlist with a nation (WORLD), earn merit from its victories, your kills of its enemies and missions; ranks Recruit to Marshal
  with pay; Captain may levy, Commander decrees war (breaking a truce costs legitimacy) and treaties.
- **Missions** (MISSIONS): bounties (a real enemy squad near a town; paid when it is beaten), tributes and famine relief.
- **Intrigue and romance** (PEOPLE): spy (24 h cooldown, 45 %), expose (scandal, trial), blackmail (leverage and Cats, or a backlash);
  courting leaders and governors to friendship, dating and marriage (+20 relations with their nation), jealousy, NPC scandals.
- **Panel** on the key action "Dynamic Wasteland panel" (Ctrl+F4 by default): WORLD, WARS, NEWS, POWER, PEOPLE, MISSIONS, SOCIETY.
  **Political map**: a territory blob in the owner's colour (BlobAlpha) under each held town, the town tinted and labelled with its owner.
- **Settings** (Mod settings): the ini keys used, with the shipped defaults. The state is saved in the mod's save data and hashed.

## Facts

**Verified** (decompile of `DynamicWasteland.dll`; tables read from its `.rdata`):
- `WarIntensity` (default `aggressive`): `calm` 0, `balanced` 1, `relentless` 3, anything else 2; each table below is indexed by it.
- Declaration: pairs tried [3, 7, 12, 20]; a nation at war tries another only with aggression above 85 and [0, 5, 10, 25] %; allies, nations
  at war and truces are skipped. Chance = [-2, 2, 8, 14] + 1 + aggression / 18, +5 when the relation is below -20, +3 when its military
  is above the target's + 20, +3 with a claim, +4 for a religious war, − exhaustion / [15, 20, 30, 40], − [6, 4, 2, 0] when stability is
  below 30, − [10, 6, 3, 1] with two wars or more, − [14, 10, 5, 2] when bankrupt, − [5, 3, 1, 0] below 5000 Cats; capped at
  [8, 20, 32, 50]. Reasons: relation below -50 "old hostility", else military above the target's + 20 "territorial opportunism", else
  "border tension".
- Call to arms: up to two allies not already at war with the attacker join; each side loses 2 stability.
- Battle: attacker pays 800-1999 Cats, defender 500-1499; scores are power + military − 4 × exhaustion (the defender's plus the town's
  defence) + a roll of −40 to 40. Winner loses 2-9 military, loser 2-9 plus 3-8; exhaustion +3-8 for the winner, +7-14 for the loser. Town
  defence: size / 3 + 40, 65 for the capital.
- Peace halves both sides' `warExhaustion`. Score defaults: military 50, economy 50, stability 60, warExhaustion 0, aggression 50,
  corruption 20, foodSupply 50, manpower 40.

**Observed** (the original's ini, `Heroes.ini` and strings): the ini sections and defaults used (crisis kinds and pacing, YearDays 40,
cult cooldown 150 days and 4 %, spy cooldown 24 h and 45 %, emergent MaxActive 12, CooldownDays 14, SpawnChance 8 %, CaravanCapacity 200,
"1 unit feeds 10 people/day", FoundReligion merit 150 and influence 50, BlobAlpha 0.38); the news texts (English set) used as messages.

**Unknown**: the original's formulas for everything but the facts above (power, claims, peace, offensives, alliances, food, migration,
crises, elections, coups, schisms, ranks, missions, intrigue and romance are the port's, from the ini's descriptions); its colours (port: a
hue from the faction id); its town residents after a conquest (the port swaps squads naming another faction for the new owner's).

## Port choices

- The player's faction is never a nation; the player acts through the career, company, missions and intrigue.
- Political characters (leaders, governors, captains) are the simulation's own, not world characters; courting happens in the panel.
- Rebel and emergent factions are FACTION records made in the data phase (a rebel faction per nation, twelve emergent ones modelled on the
  bandits, each with copies of three squad templates), so their names in the world are the records' ("Holy Nation Rebels", "The Hungry
  Band"); a free city's name shows in the panel only.
- The panel key is Ctrl+F4 (the game's speed 3 holds F4). News popups follow game time only (no `PopupMinIntervalMs`).

## Harmony

None. Every gap had a mod API: data phase (`IModDataPhase`: town versions, pool factions and squads), `AddSystem` with `IWorldReady`,
`IModSaveData`, `IStateHashed`, `IRelationChangeObserver`, `IPlatoonObserver`, `ICharacterDeathObserver`; `PopulationSystem.RequestCampaign`,
`ApplyTownVersion`, `HoldTown`, `SpawnPlatoon`, `HoldPlatoon` / `SendPlatoon` (named), `DespawnPlatoon`, `Platoon.ModTags`;
`PlayerRelations.DeclareWar` / `EndWar` / `Add`; `world.Player.Money`; `AddUi` (overlay, `AddMapMarkers` with an image), `AddKeyAction`,
`AddSettings`, `ModContext.Enqueue`.

## Missing

- The LLM service (`AI\DynamicWasteland.AI.exe`, its own process and named pipe): not used; the original falls back to its templates
  without it, as this port does.

## Not ported yet (possible, left for later)

- The original's title screen (`gui/`), recruiting spared enemies in conversation (`[Recruitment]`) and courting world characters there:
  each would patch Meitou's front end or conversation window with Harmony.
- Rising Heroes' DLL spawn of the hero squads on old saves (`[RisingHeroes] DllSpawn`), procedural wanderers (`[Wanderers]`), branching
  story missions (`[StoryMissions]` A/B), laws and trials beyond the scandal's, cultures, parties, governments in exile, claims and
  ultimatums.
