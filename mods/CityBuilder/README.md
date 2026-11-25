# City Builder

Port of *Kenshi: CITY BUILDER* (mental, Workshop 3833745044, `City.dll` on RE_Kenshi), from the original's DLL (Ghidra decompile), its
`City.mod`, `City.ini`, `layouts.txt` and `zh_CN.txt`. The original is a required item: its `City.mod` (the CITY build category's buildings,
factions, AI packages and squad templates) must be enabled, and its files are read at runtime from its installed folder; nothing of it ships
here.

- **Residences and trade houses**: some game hours after a CITY building is finished (City.ini `HomeHoursMin`/`Max`, `TradeHoursMin`/`Max`),
  buyers offer to buy it; the Settlement window opens and the game pauses. Sell: the price goes into the player's money and the settlers
  (1-3, from the building's people's template) or the trader and guards move in. Refuse: others come later.
- **Trade houses** hold one of eight shops (General Goods, Weapon, Armour, Prosthetics, Food, Clothing, Tool, Crossbow), chosen with the
  window's buttons before or after the sale. A change takes the furniture and sign down and puts the new shop's up; after the sale the trader
  leaves and one of the new shop comes at once (no new deal). The trader sells from its template's vendor lists; each morning every open shop
  pays a tax.
- **Guard posts** (I-IV: 5, 10, 15, 20 guards): hire for `HireCost` each, one or up to full, paid at once. The guards take up the post when one
  of the player's people is within `ScanRadius`; every fifth watches the gate, the rest share day and night shifts on City.mod's packages.
  Those that no longer fit get their money back.
- **Taverns** (I-III: 3/6/11 beds, 11/30/39 seats): an innkeeper (barman with stock) comes after `InnkeeperHoursMin`/`Max`. From 19:00 the
  night's guests come (Tavern Guests, on City.mod's guest package: the bar, then bed from 22 to 7); in the morning the guests still there are
  counted, paid for at `NightPrice`, the player's share goes into his money, and they leave.
- **Residents** work at the settlement's machines (the engine's town work of their package, "working mines and farms") in the working hours
  (`[Residents]`), and not outside them or with Work off.
- **Tidy** (`[Tidy]`): idle people of the player's own squads carry items lying in the settlement into its storages.
- **Furniture and signs**: a new CITY building is furnished from `layouts.txt` (interior and exterior layouts of the vanilla building it copies).
- **Window**: the CTY button above the bottom panel's shortcuts, key Ctrl+Shift+H ("Settlement window" on the Controls page), the town
  panel's Settlement line, or a selected CITY building's panel button (Sell/Refuse while an offer waits, Hire a guard on a post). Escape closes it.
- **Settings**: City.ini's keys under Mod settings; the installed original's City.ini gives the defaults. Texts follow the game language:
  Chinese from its `zh_CN.txt`, Russian from `City.dll`; the CITY buildings' names and descriptions, the templates' and factions' names are
  shown in that language as the original renames them.

## Facts (Verified: decompile of `City.dll` and reading of the original's files, unless marked)

- CITY buildings: City.mod BUILDING records 100-133 with `building category` CITY, named `<kind>[ <level>][ (<people>)]: <building>`
  (Residence, Trade House, Guard Post I-IV, Tavern I-III; Hive, Skeletons, Swamp). Their `icon` is the vanilla building they copy
  (`4092-TwoStorey.mod.png`), which keys `layouts.txt`.
- City.mod squad templates by people: People 300 (and 304, 305), guards 301-303 (day, night, gate on AI packages 331-333), shops 310-318,
  innkeeper 319, tavern guests 340 (package 334); Hive 410-428, Skeletons 430-448, Swamp 450-468 in the same order. Factions Townsfolk 200,
  Town Guard 201. A template's `layout interior` and `layout exterior` name the shop's furniture and sign.
- `layouts.txt`: `L|<building>|<layout name>|<0 interior, 1 exterior>` then `F|<record>|x|y|z|w|x|y|z` (building frame). **Observed**: rotations
  are about the vertical axis only; positions near 0 for small houses.
- City.ini keys and defaults: HomeSmall 7000, HomeLarge 15000, TradeSmall 12000, TradeLarge 20000, HomeHours 6-18, TradeHours 12-30,
  InnkeeperHours 4-10, RetryAfterRefusalHours 36, ScanRadius 3000, HireCost 2000, NightPrice 100, PlayerSharePercent 30, TaxPercentPerDay 1,
  Residents Work 1 (7-19), Tidy Enabled 1, Haulers 3, Reach 300, Junk empty. "Small" is the Small Shack and Storm House (ini comment).
- Refusal: the next buyers come `RetryAfterRefusalHours x (0.8 + 0.4 r)` hours later.
- Tavern evening (from 19:00, once): guests `clamp(int(r x (b + 0.99 - 0.4b) + 0.4b), 1, b)` for `b` beds, made in groups of `rand % 3 + 1`
  of template 340. Morning (from 7:00, before 19:00, once): the living guests within 800 of the tavern are counted; without that list a draw
  `int(r x (0.75b + 0.99 - 0.3b) + 0.3b)`, at most `b`, plus one draw per missed night up to 6 more; share `guests x NightPrice x Percent / 100`.
- Shops, each day from 7:00 once: tax `int(price x TaxPercent / 100 + 0.5)` per day, 1 to 7 days.
- Russian: City.dll passes each text to one picker (English in r8, Russian in rdx) and keeps the names of buildings, shops and traders in data
  tables of (Russian, English) pointer pairs; it renames City.mod's records at start (`City data renamed to Russian`).
- The original's state is its own `saves/<save>.txt` (`KENSHI_CITY|8`), not the game's save.
- **Unknown**: which layout the original picks when the building has none of the template's name (it logs "no vanilla layout fits"); which
  people the " Only people live and work here." line belongs to (the port gives it to Swamp); how the original tells junk and severed limbs
  (the port goes by item name).

## Port choices

- The building stays the player's and its people are squads of the player's faction the AI runs (independent, uncounted, homed at the
  building, on their template's package; a faction shop on its template's terms). See Missing.
- Buyers wait off screen until the sale; settlers, traders and innkeepers appear at the building when they arrive.
- Tavern guests are NPC platoons of Townsfolk spawned 100-400 from the tavern and sent to it; they rent no bed from the player (the share is
  paid by the mod as the original does).
- Work hours: outside them the residents' work building is set to their own home, which has no work slot, so they do not work.
- Tidy hauls only items of no faction or the player's, by idle (no task, not fighting) characters of squads the player leads, one item each;
  a hauler that finds no storage with room keeps the item. Haul jobs are not saved.
- Offers, sales, hires and shop changes take effect while paused.

## Missing

- Handing the building and its furniture to the Townsfolk faction, and residents of that faction. Not done, also not by Harmony:
  - the save: `BuildingSave.WriteSiteKeys` stamps every registry site `owner faction ID` = the player's faction and `BuildingSave.Read` makes
    each site with `Owner` = the player's index; its filter `BuildingSave.IsPlayerBuilt` (an expression-bodied static, a likely inlining
    candidate, so a patch may silently not apply) keeps only player-faction states. A transferred building would come back as the player's
    or, if the filter were widened, be read as a player site of another owner in a player outpost's town handle;
  - `MachineSave` writes only player-owned machines, so the furniture's storage, beds and counters would lose their state at a save;
  - the residents, trader and guards are player-faction squads, the only kind the engine homes in a player-built building (`SetSquadHome`);
    an autonomous squad's home town is an outpost its own faction owns (`SquadAutonomy`), so a foreign-owned home breaks their homes, beds,
    work and shop counter. NPC platoons (`SpawnPlatoon`) are saved but have a town site, not a player-built building, as their home.
  Ids, zones and determinism would hold; the state (owner, machines, homes) would not. It needs engine support for non-player buildings in
  player outposts.
- **Unknown**: not checked in game.

## Harmony

None: every part ported runs on the code-mod API.
