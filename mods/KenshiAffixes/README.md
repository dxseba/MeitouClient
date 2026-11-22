# Kenshi Affixes (partial port)

Port of *Kenshi Affixes* (workshop 3821983877, GPL-3.0-or-later), rebuilt from its Workshop description (v0.6.0's affix tables).

- **Rolls**: every 60th tick each weapon and armour item a loaded character carries that has no roll yet gets one, kept in its
  `ItemInstance.ModTag` (`affix=Name:value,...` or `none`, and `ilvl=`, in the shared `dxseba;key=value` tag also used by Equipment
  Durability). Item level = the item's gear level (1-100). Up to three distinct affixes, the k-th with chance *Affix chance* (0.9) x level/100 -
  0.2 k (poor gear often has none), each valued between its range's ends by level x (0.5 + 0.5 roll). Deterministic (`Rng.Hash`).
- **Armour**: Guarding +1-10 melee defence, Aggressive +1-10 melee attack (armour totals capped at +25 each), Fleet +1-15% athletics,
  Silent +2-30% stealth, Nimble +1-15% dexterity, Reinforced / Cushioned +2-25% remaining cut / blunt resistance (defence + (1 - defence) x
  share, so +25% turns 60% into 70%, the description's example; capped at the game's 90% cut, 80% blunt). **Weapons**: Keen +3-50% cut,
  Crushing +3-50% blunt, Serrated +4-60% bleeding, Precise +1-15 melee attack, Parrying +1-15 melee defence.
- **Effects**: stat affixes of the worn armour and the fighting weapon (first on the hip, else the back) are stat modifiers
  (`Stats.Modifiers`, key `dxseba.kenshiaffixes`, refreshed every 30th tick; a % is of the stored stat); damage affixes scale the melee packet
  (`ICombatHitFilter`); resistance affixes raise the worn piece's defences for every hit through it (`IArmourFilter`). NPCs too.
- **Tooltip** (`AddItemTooltipLines`): a rolled item shows "Item level N" and one line per affix (or "No affixes"); unrolled items and a
  trader's stock (no tag) show none.
- **Show affixes**: a menu row on any living character posts its equipped gear's affixes.
- **Missing** (partial): the affix colours, the rare Leeching, Deadly and Devastating affixes, value/price changes in shops, rolls for shop
  stock and containers until a character carries the item, the original's combat-strength and crafter-skill item levels, and its fixed
  unique-NPC profiles.
- With Reinforced and Cushioned in the armour pool, items already rolled keep their rolls, but new rolls differ from earlier builds' for the same seed.
- **Unknown**: not checked in game (builds only; the tooltip lines and resistance affixes have not been seen running).
