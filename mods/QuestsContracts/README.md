# Quests & Contracts

Port of *Quests & Contracts* (Workshop 3797459646; its plugin `Ledger.dll` and BFrizz's *Extra Extensions* variables it needs), from the
original's shipped GPL source (`Ledger-source.zip`, read for facts only), its lists and its `.mod`. The original is a required item: its
`QuestsAndContracts.mod` carries every line, and its `ledger-*.txt` lists and `Ledger.json` are read from its folder at runtime.

- **Barmen's work:** "Got any jobs?" offers one job of the town's pool (deliveries, packages to another town); take it or pass. Three per town
  per three days, three days to hand in (a package has its own days), any order; the hand-in takes the wanted items from the party.
- **Special jobs** at reputation 70: a debt to collect (4) or a harasser to teach (5) on a native of the town, picked and locked; down or dead
  makes it READY, a paid debt strips his money, a talked-down thug is half pay.
- **Rides:**
  - The debtor's escort (8) and the caravan (9): the one who asked follows on the contract his accept line gives his squad.
  - In the destination town the ride is ARRIVED and he starts his end talk with you, which pays, gifts or betrays.
  - Past its days the talk is forced; two days later the ride is dropped.
  - The escort may betray (30 % on the beg) and rolls its gift at the accept.
  - The debtor rests 3 days between rides, the caravan 2 hours; the caravan avoids its last 8 towns.
- **The law desk:**
  - It hands out three cutthroat posters and one big name per desk and three days.
  - A poster is read when handed over, heard or carried; the wanted one of a read poster is seen near you (his town and day) and priced at
    the poster's reward with each faction it names.
  - Handing him in at a desk claims the poster.
- **The Shinobi thieves:**
  - Heists (12), one at a time: the lookout knows when you are in the town and which marked piece lies in its containers. A chest heist is
    stocked by the fence into a lockable chest (Metal Chest, Safe, Secure Wooden Chest) near you.
  - Marks (14): stocked into a safe on the spot.
  - Rescues (13): in the jail town the scan looks for one of the guild in reach. With none, one is spawned behind the bars of the nearest
    free cage. Freed, he follows you out of the town and starts his own end talk.
  - Promotions for the bar, the desk, the thieves and the caravan.
- **Pickpocketing:** while sneaking, Pickpocket on a standing, conscious man who is not yours.
  - The approach roll (1.1 x thievery / 100) catches you as the game's theft does.
  - Or it opens his inventory, where every item rolls as a pickpocket's take.
- **Reputation** 0..100 per NPC, town (60 %) and faction (25 %): +10 a hand-in, -3 a failure or cancel, -8 a job done and never reported.
  Rides and claims count for the faction too.
- **The Ledger** (J): contracts, done and failed, bounties, relations, log.
- **The overlay** (hold H): within 2500 of the ground under the camera, "!!" over job targets, "!" over givers with work, "..." over anyone
  you can talk to. The marks and radius come from `Ledger.json`.
- **Quest dots** on the world map: the targets where they stand, the guild's towns.
- **The readout** (an unbound key on the Controls page; the original's F10 is Meitou's debug menu): the talk context, the open jobs and the
  targets' portraits.

## Facts (Verified: `Ledger.cpp` of `Ledger-source.zip` and the shipped lists, unless marked)

- **Extra Extensions' tests:**
  - They are DIALOGUE_LINE reference lists `variable equals` / `variable less than` / `variable greater than` to records of type 1000
    (`value` int; 78 in `QuestsAndContracts.mod`, read with a scratch FCS dump), with value0 the operand.
  - The plugin writes the values; the dialogue never writes them (no set/add effect in the `.mod`).
- **Signal lines:** lines tell the plugin by being said (`Dialogue::_doActions` hook, the line's record matched against
  `ledger-signals.txt`).
- **Context at a player talk:**
  - Rep is the best of the NPC/squad/town/faction keys.
  - The town code is FNV-1a of the town name, mod 1000000.
  - Offers are drawn at random per pool; a pass lasts one visit.
  - Specials: one per town per 3 days. Fail after 3 days (`FAIL_DAYS`).
- **Reach:** items are counted across player characters within 6000 of the talker; target picks are within 2500 of the barman, in the same
  town, standing.
- **Draws:** bounty, heist and rescue draws hash the day, the town code and the kind. The heist also hashes the heists done, and never picks
  the last town twice.
- **Ride end:**
  - A ride ends by its contract's end stamp and a forced event-30 talk (`Dialogue::sendEventOverride`); 40 tries, 3 s apart.
  - The escort's AI package is the `.mod`'s "Escort" (`5536`), given by the player's accept choice (`AI contract`). The rescued thief's is
    "Thief escapes" (`4335`). Both read from the `.mod` with a scratch dump.
- **Chest heist:**
  - It stocks only a "Metal Chest", "Safe" or "Secure Wooden Chest" within 900, nearest first, starting at heists done mod the count.
  - The piece is the want list's entry at day mod the count.
- **The rescue's spawned man:** the squad template "Thief Prisoner" (`4334`), the Shinobi Thieves, the nearest cage within 9000, once per
  rescue.
- **Pickpocket:** the original adds Loot (26) to the menu of a conscious man while the player's man sneaks. It rolls the game's notice once
  with no item and treats every take as theft.
- **Unknown**: whether Extra Extensions' tests add to a line's score. The port keeps the engine's score as without them (`score bonus` lowered by
  the tests it adds as conditions).

## Port

- **Data phase:** each variable test becomes a `conditions` entry (DIALOG_ACTION, condition code 7300 + variable index, `compare by` 0/1/2).
  Each signal line gets an effect of code 7290 whose value is the signal's index.
- **Ride end talk:** `DialogueSystem.Start` with event 30 (reflection); the contract ends with `EndContract`.
- **Chests:** a fixture's `Contents`.
- **Pricing:** `CrimeSystem.AssignBounty`. Raising by 1 reads the entry, then the port tops it up to the reward.
- **The spawned prisoner:** `PopulationSystem.SpawnPlatoon`, then `FixtureRegistry.Imprison` once he loads.
- **The approach roll:** `CrimeSystem.StealRoll`; the window is the game's `OpenLootOrder`.
- **Map dots:** `AddMapMarkers`. **Portraits:** image nodes with `PortraitOf`.
- **Harmony:**
  - `Meitou.Simulation.Dialogue.DialogueSystem.Start` (prefix: the talk context before the first line).
  - `Meitou.Simulation.Crime.ArrestSystem.PayBounty` (prefix and postfix: the claimed body).
  - `Meitou.Simulation.Carrying.CarrySystem.CanBePickpocketed` (postfix: a mark the approach roll let through opens). It is patched in
    `Initialize`, before the simulation's code is compiled.
- Characters are tracked by `LastingId`; the state is saved in the mod's sidecar (JSON).
- **Engine choices:**
  - The reputation's squad key is left out; Meitou has no squad names.
  - The approach roll happens when you choose the row, not at the reach.
  - The overlay draws its marks over the heads, not as speech bubbles.
  - Pricing a poster adds 1 cat to the reading.

## Missing

- Nothing that the engine blocks.
- **Unknown**: not checked in game.
