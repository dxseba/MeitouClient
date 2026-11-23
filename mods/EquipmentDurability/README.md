# Equipment Durability (partial port)

Port of *Equipment Durability* (workshop 3834344700, "Durability" 1.2.0 by its shipped README), rebuilt from the behaviour of its shipped
`Durability.cpp` (GPL-3.0) and README; no code was copied.

- **Settings**: `Durability.cfg` in the mod folder, written with the defaults when missing and read at start, with the original's keys and defaults, so
  the original's cfg works unchanged (`Verbose` and `TooltipAfterRow` are read by nothing here).
- **Durability**: an integer per weapon, crossbow and armour piece with coverage (not locked armour such as shackles), in `ItemInstance.ModTag` as
  `dur=<n>` of the shared `dxseba;key=value` tag (`ItemTag`; saved and copied with the item). Rolled once in the class's range (`min-max/cap`, the cap
  is the maximum): Katana 15-267/500, Sabre 35-399/700, Hacker 45-445/800, Heavy 98-533/1000, Blunt 25-350/600, Polearm 35-399/700, crossbows
  15-267/500, any other weapon 100-1000; armour by its `class`: light (and cloth) 100-300/600, medium 100-400/800, heavy 100-600/1200. A crafted piece
  (`Crafter` set) starts at its cap. Gear whose name, weapon material or maker holds an `IgnoreWeapons` word (default `Meitou`) or `IgnoreArmour`
  word is left out.
- **Wear** (player characters only): 1 per landed melee hit (weapon in hand), per block, per crossbow bolt loaded, and 1 per worn armour piece whose
  `part coverage` lists the part a melee hit struck. Grade wear, the chance a use costs a point, by item level: weapons from level
  5/10/15/20/25/30/35/40/50/55/60/70/75/80 at 100/95/85/75/65/55/50/45/35/30/25/15/10/5%; armour and crossbows from level 5/20/40/60/80/95 at
  100/75/50/25/15/5%. At 0 the piece stays usable at the minimum, or with `BreakWeapons = 1` is destroyed (weapons and armour).
- **Effects**: a player's melee blow deals x1.07 above 90% of the cap, and below 39% falls linearly to 30% at 0 (cut, blunt, pierce); a player's
  armour gets the same shape (`Armour*` keys) on its cut and blunt resistance in melee hits, held to the engine's 0.9 / 0.8 caps. A player's
  crossbow reload takes the game's time plus the reload change (up to `ReloadPenaltySeconds` 10 below 39%, `ReloadBonusSeconds` 0.8 less above
  90%), never under a quarter of the game's time when faster nor under 0.1 s (`ICombatSpeedFilter`, `CombatSpeedKind.Reload`; the game's time
  rebuilt from `RangedFormulas.ReloadTime` and the stats the reload blends).
- **Tooltip**: `Durability: N / max`, `BROKEN` at 0, then `(-NN% dmg)`, `(+NN% resist)` or `(+N.N s reload)`; texts in English, Portuguese, German,
  French, Russian and Spanish by the game language (the original's table). The value is in `PenaltyColor` (59231a) while a penalty applies or at a
  quarter of the cap or less, `BonusColor` (a8b774) while the bonus applies (`UiLineStyle`).
- **Rolls**: on first use, on the item's first tooltip (`UiItemTooltipContext.First` queues a roll for the item by `Owner` / `Index`: a
  character's inventory or a container), as the original, and when the world makes the item (`IItemCreatedObserver`: loot, crafted, shop stock,
  spawn kit), so a trader's stock, which no command can name, shows a value too.
- **Repair Weapon / Repair Armour** on a player character's menu, when there is a repairer: with `RepairOthers` and another character selected, that
  one if it is a player character carrying the repair item; else the clicked one. The repair item: the vanilla Tools (`43404-changes_otto.mod`, any
  language), `RepairItemID`, or a `RepairItem` / translated Tools name, in the repairer's own sections. The weapon is slot 1 (back) then slot 2 (hip),
  armour helmet, chest, shirt, legs, boots, skipping full pieces. One Tools is used up; the piece gains 500 + 2 x the base smithing level (Weapon,
  Crossbow or Armour Smithing), 50 and no bonus when broken, up to its cap. It takes 1.2 s standing still, playing `RepairAnimation`
  (`auto`: the first-aid `medic` clip, or a named ANIMATION; `AddCombatMove` with `CombatMove.Clip`, spread over `RepairSeconds`); moving 0.75 units, being attacked or going
  down cancels it with nothing used up. A squad member's repair walks the repairer within 10 units first (following a member that moves 3), settles
  0.4 s, then repairs; it fails after 25 s, 4 s without getting nearer, 4 units past its best, or the member moving off. `RepairSound`
  (`repair.wav`, a 16-bit PCM file in the mod folder, not shipped: copy the original's) plays on the interface as the work begins
  (`RepairSoundTiming = start`) or as the repair is applied (`end`; an instant repair plays it then).
- **Breaking** (`BreakWeapons = 1`): the piece is removed and the owner's fighter and look are made again (`CharacterRefresh.Refresh`, Equipment).
- **Missing** (engine): the tooltip row's place after "Sell value" (`TooltipAfterRow`; mod lines sit after the game's rows, before the
  description); stopping the sound when a repair is cancelled (a posted sound cannot be stopped); "anything that stops the animation" as a cancel
  (the clip is a combat move the game's own actions replace; the port's cancels are the original's listed ones).
- **Observed** differences: the repair timer runs on game time (the original's on real time, so equal at x1 speed); a shot is counted per bolt loaded
  (the engine reports reloads); armour wear and effects follow melee hits only, as the original's `hitByMeleeAttack` hook.
- **Verified** against the original's source (read, not copied): ranges, grade tables, formulas, repair rules, texts. **Unknown**: not checked in game.
