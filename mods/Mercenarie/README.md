# The Mercenarie

Port of *The Mercenarie* (Workshop 3798671709, plugin `GuildEscortContracts.dll`), from the DLL (Ghidra decompile, and its data read at the
addresses the code names), its `Options.ini`, `Localization/en.json` and `Guild Escort Contracts.mod` (a scratch FCS dump). The original is a
required item: its `.mod` carries the clients, caravans, scientists, wanted groups, chairs and factions. Nothing of it is shipped.

- **Contracts** from bartenders, and from clients who walk into your Guild Houses ("I am looking for a contract."): a board per town and day
  with an escort, a message delivery, a caravan escort, a scientific expedition to a ruin and, from guild level 1, a bounty hunt.
  - **Escort:** the client (civilian, merchant, soldier, family or mercenary squad) follows you to the destination town; paid on arrival.
  - **Caravan escort:** the Guild Trade Caravan (1-4 / 3-6 / 5-8 strong by distance) goes to the town and back; paid home.
  - **Scientific expedition:** the Lead Scientist goes to the ruin, researches two hours inside, and back; paid home.
  - **Bounty hunt:** a wanted group (2 to 6) waits near the destination; down them all, then report to the issuing bartender.
  - **Message delivery:** tell the destination's bartender "I have come to report a contract.".
- **Negotiation** from guild level 2: demand -20 % to +25 %; the client's budget is hidden; two attempts, then the last offer stands.
- **Guild progress:** Cats (scaled by the Contract rewards setting, 50-150 %), Guild XP, levels 0-10, prestige past level 10, reputation per
  town; the Cats to XP exchange (10000 Cats: 120 XP). Guild window: J.
- **Real estate:** select a town house for sale: **Rent this building** (weekly rent, first week at signing), **Buy out** (ten weeks), **End
  lease**; unpaid rent becomes debt and a week of debt ends the lease. Buying outright is the game's own house purchase.
- **Guild Houses:** select a building you own: **Define guild building** (level 3: one; level 4: three; one per town). With a Client Chair and a
  Waiting Chair inside it is operational and a client comes in once a day.

## Facts (Verified: decompile of `GuildEscortContracts.dll` and the DLL's data, unless marked)

- Types (`FUN_1800e60d0`): 0 escort, 1 caravan, 2 expedition, 3 bounty, 4 mail; caravan and expedition are round trips.
- Escort, caravan, mail price (`FUN_180033860`, offers tagged `;PRICE85;`): base 2500 / 3000 / 1500 + 130 / 90 / 75 Cats a km (km = 1000
  units, `DAT_1803eb0d8`; at most 100000), + 5 % per group entry past the first (at most 25 %; not mail; a multiple mail +1/3), x danger
  100 / 115 / 135 / 160 / 200 % (danger 1-5), +20 % for a flagged contract, at most 100000.
- Expedition and bounty price (same function, older path): 3750 + 50 a km, x danger 1 / 1.25 / 1.6 / 2.1 / 2.8 (`DAT_1803d4cb0`), x tier
  1 / 1.25 / 1.6 / 2 (`DAT_1803d4ca0`), +20 % flagged; at least 3000 / 1000 (escort 1000, caravan 2000, mail 750 on that path).
- Negotiation -20..+25 % (`DAT_1803d1e90`); the reward setting is clamped to 50..150 % and applied rounded (`FUN_180027670`).
- XP (`FUN_180017860`): base 35 / 45 / 60 / 35 / 25, + km travelled / 5 (round trips doubled; 40 from 200 km), x danger 1 / 1.1 / 1.25 /
  1.5 / 1.75 (`DAT_1803d3868`), +10 with no losses, + 0.5, floored.
- Levels (`FUN_180017410`, `DAT_18038f3f0`): 300, 800, 1550, 2550, 3850, 5500, 7500, 9900, 12750, 16100 XP for levels 1-10. Cats to XP
  (`FUN_180017470`): whole 10000-Cats blocks, 120 XP each.
- Generation (`FUN_180117740`): distance tiers near / medium / long; caravan size 1-4 / 3-6 / 5-8 by tier; escort clients from templates
  880101 / 880103 / 880105 / 880107 / 880109; expedition 880121; caravan 880080; bounty groups 881602-881606.
- Unlocks (`en.json`): bounties at level 1, negotiation at 2, one Guild House at 3, three at 4; a Guild House needs a Client Chair (880130)
  and a Waiting Chair (880131); bonuses change money and reputation, never XP.
- Real estate: a lease pays a weekly rent (`estate.rent_payment`), the first week at signing; a buy-out costs ten weeks (`FUN_180208b00`,
  `rent x 10`); debt blocks another acquisition; ownership moves by `Ownerships::addOwnedObject` / `removeOwnedObject` (no emptying).
- **Unknown** (engine choices): the danger (the original reads road segments and region risk from `pricing-roads.dat`; the port counts sites
  hostile to you within 5 km of the straight route), the tier bounds (20 / 45 km), deadlines (a day per 20 km plus one), the client's hidden
  budget (up to +15 %), the flagged chance (town reputation / 4 %), reputation steps (+-5), the rent amount (the port uses a tenth of the
  game's sale price; the original reads its own per-material tables), research time (2 h), straight distances instead of roads.

## Port

- Groups: `PopulationSystem.SpawnPlatoon` tagged with `Platoon.ModTags`; clients, caravans and scientists follow you on `StartContract` with
  the vanilla Bodyguard package (`5090-gamedata.base`), ended with `EndContract`.
- Real estate through `FixtureRegistry` internals by reflection: the lease uses the purchase's take-over (`FixtureRegistry.TakeOver`) without
  the purchase's emptying, camp clearing and eviction (as the original's ownership move); ending it sets the property's `Owner` and `Purchased`,
  the fixtures' `Owner`, clears `_ownedProperties` and reopens it to the public. The game saves the owner as for a bought house. Intact houses
  only (a ruin's take-over makes a construction site that has no inverse). A renamed member turns leases off, logged.
- Guild House chairs are the player's finished `BuildSite`s whose `Parent` is the building.
- No Harmony patches. State in the mod's sidecar (JSON).

## Missing

- Removing the mod during a lease leaves the house the player's (the lease record is the mod's); not avoidable without an engine hook.
- Not ported (rules not in the decompile in a usable form): bonus requests, delegation, tax collectors and raids, payroll, the original's own
  escort AI (pause lines, pace) and road routes, blacksmith orders, guild gear, the localisation files. **Unknown**: not checked in game.
