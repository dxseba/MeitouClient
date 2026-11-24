# Kenshi Affixes (port)

Port of *Kenshi Affixes* v0.6.0 (workshop 3821983877, GPL-3.0-or-later). Rules **Verified** from its shipped source (`AffixRules.h`,
`CombatRules.h`, `ArmourBonuses.h`, `MeitouRules.h`, `KenshiAffixes.cpp`, `config/*.json`), docs and `KenshiAffixes.ini`.

Rolls (once per item, kept in `ItemInstance.ModTag`: `affix=id:value,...` or `none`, `ilvl=`, `aprof=` for an authored profile, in the shared
`dxseba;key=value` tag also used by Equipment Durability), deterministic (`Rng.Hash`), as the world makes the item (`IItemCreatedObserver`):
- Sources: a new NPC's equipped kit at its level ((mean of trained strength, dexterity, toughness) + best of (melee attack + defence) / 2,
  martial arts, bows) / 2, 1-100 (`AutoNPC`); a shop's new stock at 12 + 16 x band (`AutoShops`, `ShopBaseLevel`, `ShopLevelPerBand`); new
  loot in a container (`AutoContainers`; `ItemCreated.Place` `Container`, a camp's ground items are not rolled) at max(quality level,
  round(0.7 x site strength + 0.3 x quality level)); gear a player character crafts
  (`AutoCrafting`) at round(0.7 x its trained weapon or armour smithing + 0.3 x quality level). A 60-tick sweep rolls fixed NPCs' gear (also
  after a load), shop stock from before the mod ran and Meitou weapons. The player's characters' other gear is never rolled (marked `aplain`,
  so selling or storing it keeps it plain).
- Eligible: melee weapons and wearable armour (body, legs, hat, boots, shirt, belt, eyes, gloves, neck), single, not locked, not death items.
- Band 0-5: armour (gear level + 5) / 20; weapons by cut-offs 15/30/45/60/80. Affix count per band: 0-1, 0-1, 1-2, 1-2, 2-3, 3.
- iLvl = round(source) + -5..+3, 1-100; tier T = (iLvl - 1) / 20 + 1. Candidates need a positive base field (cut/blunt/bleed multiplier,
  stat multiplier) or a resistance in (0, 1); rare affixes need iLvl 61+. Picks by weight without repeats (100; rares 15; on armour
  Deadly/Devastating 3). Value = low + (high - low) x t x (0.9-1.0), t = (iLvl - 1) / 99 (rares (iLvl - 60) / 40) raised to 1.35 but for
  rares; points rounded, at least 1.
- Fixed NPCs: their equipped gear takes their profile's affixes in order at the NPC's level, no jitter, up to the band's most and 3,
  replacing an ordinary roll. Meitou weapons (model `52293-rebirth.mod`): their template's affixes at level 100, wherever they are. Both
  tables are the original's authored data and are not shipped: they are read at start from the installed original (Workshop folder
  `3821983877` beside this one, else the code-less mod named `KenshiAffixes`), `Source/config/fixed-npcs.json` and `meitou.json`. Without it,
  a log line and ordinary rolls only.
- Meitou fill-in and modded templates (**Verified**, the original's `MeitouRules.h`): every Meitou's priorities get a fill-in list appended, so
  one that cannot take an affix still gets three; a Meitou template the original does not list takes one of six styles picked by its id (FNV-1a
  32-bit over its bytes, one xorshift 13/17/5 step, modulo 6), led by Crushing when its blunt multiplier is above its cut one, else Keen, for the
  styles that lead with the damage affix. The lists are read at start from the installed original's `Source/native/MeitouRules.h` (all six styles
  and known ids, else none of it). **Observed** (2026-11-24, a scratch regex run on the installed file): six styles and the fill-in found.
- The original's ini lists (**Verified**, `KenshiAffixes.ini`, `RuntimePaths.h`, `KenshiAffixes.cpp`): read from the original's packaged
  `KenshiAffixes.ini`, the player's `%LOCALAPPDATA%\KenshiAffixes\KenshiAffixes.ini`, then one in this port's folder (later keys win):
  `[ExcludeTemplates]` `ID=1` (never rolled, Meitou included), `[FixedNPCs]` `CHARACTER_ID=profile` or `off` (replaces the built-in entry),
  `[FixedProfile_NAME]` `Melee=` / `Armour=` (space-separated affix ids, replacing the profile's). The single options are the settings below.
- Site strength and loot quality (**Verified**, `KenshiAffixes.cpp` `siteStrength`, `templateThreat`, `lootNativeLevel`; `AffixRules.h`
  `lootLevel`, `lootQualityLevel`): the loot's town (`ItemCreated.Town`, `PopulationData.Sites`; its override TOWN when replaced) by its
  `[LootSiteLevels]` `TOWN_ID=level` (1-100; 0 or more counts), else the highest of `combat stats`, `unarmed stats`, `ranged stats`, `melee
  attack`, `melee defence` (each 0-100) over its template and the records it links by `residents`, `leader`, `squad`, `characters`, `members`,
  `housemates`, `stats` (depth first, six links deep, 192 records), else 10. *Improve loot quality* (`ImproveNewLootQuality`, on): a new
  container weapon or armour piece whose roll 0-99 is under strength - 20 (0-80) is lifted to at least grade 20 / 40 / 60 / 80 for a site of
  25 / 45 / 65 / 85 and up (`ItemInstance.Level`, before its affix band is taken), logged `LOOT_QUALITY`.
- Container items from before the mod ran (**Verified**, `ensureContainerItem` on the tooltip): an unrolled weapon or armour piece in a
  non-player container rolls at the loot level on its first tooltip (`UiItemTooltipContext.First`, `Owner`; the system checks the container's
  kind and owner and the item at the index).

Affixes: Keen/Crushing +3-50% cut/blunt, Serrated +4-60% bleed, Precise/Parrying +1-15 melee attack/defence (weapon); Guarding/Aggressive
+1-10 (armour totals capped at +25 each), Fleet/Nimble +1-15% and Silent +2-30% athletics/dexterity/stealth multiplier, Reinforced/Cushioned
+2-25% of the remaining cut/blunt resistance, Evasive +1-10 dodge, Brawler +1-10 martial arts (armour); rare (weapon or armour, iLvl 61+):
Leeching 0-10%, Deadly 0-10% critical chance, Devastating 0-50% critical damage.

Effects: stat affixes are stat modifiers (key `dxseba.kenshiaffixes`, every 30th tick; a multiplier affix adds its share of the trained stat,
which the gear multipliers then scale); weapon damage affixes scale the melee packet (`ICombatHitFilter`); a critical hit (chance summed over
the fighting weapon and worn armour, capped 50%) multiplies cut, blunt, pierce and stun by 1.5 + bonus (capped +0.5); Leeching (capped 10%)
of the victim's real health lost (per part, held to 0..max, before and after) heals the attacker's recoverable stun only, the part with the
most first, never stumps or crushed limbs; resistance affixes act through `IArmourFilter` (game caps 90% cut, 80% blunt). NPCs too.

Price (`ITradePriceFilter`, `TradeDeal.Item`; `AffixPrices`, `ValueBasePercentPerAffix` 8, `ValueLevelPercentPerAffix` 32): an affixed item's
price x (1 + n x (8% + 32% x iLvl / 100) + rares x (20% + 40% x iLvl / 100)), n at most 3, rounded; buying and selling.

Tooltip (`AddItemTooltipLines`): "Affixes [FIXED] iLvl 50 / T3" ("[TEST] " for a test roll), "No affixes (rolled once)", "Affix value +x.x%",
then "[RARE] Name +12.3%" with the effect; with *Chinese names* (on) the original's bilingual "zh / en" texts (**Verified**, `GeneratedAffixes.h`):
the affixes' `name` / `effect` from the installed `Source/config/affixes.json` and the fixed texts from the `UI_*` strings of its
`Source/native/GeneratedAffixes.h`, each used only when its English half is the port's (**Observed**, 2026-11-24: all 17 affixes and 7 texts
match); the header and affix names in the rank colour (#999999 <15, #FFFFFF <30, #50D878 <45, #53A7FF <60, #BC83FF <75, #FFA84A <90,
#FF6262), `UiLineStyle`. "Show affixes" (a menu row, port only) posts a character's equipped gear's affixes. Rolls of the first port (`Keen:12`)
still read.

Debug options (**Verified**, `KenshiAffixes.cpp`): *Test roll* (`TestButton`) adds the menu row "Roll selected gear (once)": the character's
equipped unrolled gear (the player's too, no fixed profiles) at max(its level, *Test level* (`TestMinimumLevel`, 50)), tagged `atest`, then
"Rolled N items (once)" (port: a menu row and a world message, not the title screen's window; its save/load test buttons are not ported).
*Combat log* (`CombatLog`): "COMBAT crit= mult= chance= leech= dealt= healed=" per melee hit with a critical chance or leech, at most 10,000.

Missing:
- Loot: the building's own resident squad, which the original weighs before the town's template (not known for a container here); container
  gear from before the mod ran that is taken without a tooltip seen (take-all) stays plain (the original also rolls at the take: no hook
  before a take) (engine gaps).
- The original's affix effect lines (one line each under its affix): the effect is the line's value (tooltips take 8 lines from all mods).
- A trader's stock tooltip shows no tag (the engine's), so stock affixes show once bought (engine gap).
- Critical rolls use the world seed, not the original's own stream (deterministic by design). The test window's save/load buttons (a
  development aid writing a save named by the wall clock) are not ported.
- **Unknown**: not checked in game (the creation rolls, the container tooltip roll, the bilingual texts' glyphs included).
