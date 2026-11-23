# Kenshi Affixes (port)

Port of *Kenshi Affixes* v0.6.0 (workshop 3821983877, GPL-3.0-or-later). Rules **Verified** from its shipped source (`AffixRules.h`,
`CombatRules.h`, `ArmourBonuses.h`, `MeitouRules.h`, `KenshiAffixes.cpp`, `config/*.json`), docs and `KenshiAffixes.ini`.

Rolls (once per item, kept in `ItemInstance.ModTag`: `affix=id:value,...` or `none`, `ilvl=`, `aprof=` for an authored profile, in the shared
`dxseba;key=value` tag also used by Equipment Durability), deterministic (`Rng.Hash`), as the world makes the item (`IItemCreatedObserver`):
- Sources: a new NPC's equipped kit at its level ((mean of trained strength, dexterity, toughness) + best of (melee attack + defence) / 2,
  martial arts, bows) / 2, 1-100 (`AutoNPC`); a shop's new stock at 12 + 16 x band (`AutoShops`, `ShopBaseLevel`, `ShopLevelPerBand`); new
  loot (`AutoContainers`) at max(quality level, round(0.7 x site strength + 0.3 x quality level)); gear a player character crafts
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
  `3821983877` beside this one, else the mod named `KenshiAffixes`), `Source/config/fixed-npcs.json` and `meitou.json`. Without it, a log
  line and ordinary rolls only.

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

Tooltip (`AddItemTooltipLines`): "Affixes [FIXED] iLvl 50 / T3", "No affixes (rolled once)", "Affix value +x.x%", then "[RARE] Name +12.3%"
with the effect; the header and affix names in the rank colour (#999999 <15, #FFFFFF <30, #50D878 <45, #53A7FF <60, #BC83FF <75, #FFA84A <90,
#FF6262), `UiLineStyle`. "Show affixes" (a menu row, port only) posts a character's equipped gear's affixes. Rolls of the first port (`Keen:12`)
still read.

Missing:
- Loot: site strength (garrison templates, `[LootSiteLevels]`) is not known (`ItemCreated` names no site), so the default 10 applies, which
  makes the level the quality level; camp ground loot rolls too (the original's are containers only); `ImproveNewLootQuality` (an observer
  may only tag the item); container items from before the mod ran.
- The original's affix effect lines (one line each under its affix): the effect is the line's value (tooltips take 8 lines from all mods).
- The Chinese halves of names, the ini's `ExcludeTemplates`, `[FixedNPCs]`/`FixedProfile_*` overrides, `TestButton`, `CombatLog`. A modded
  Meitou template (not in `meitou.json`) and the original's fill-in affixes for a listed one that cannot apply (authored lists in its code)
  are left out: such weapons roll as ordinary gear, or take fewer than three.
- A trader's stock tooltip shows no tag (the engine's), so stock affixes show once bought. Critical rolls use the world seed, not the
  original's own stream. **Unknown**: not checked in game (the creation rolls included).
