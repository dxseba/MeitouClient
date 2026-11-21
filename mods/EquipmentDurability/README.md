# Equipment Durability (partial port)

Port of *Equipment Durability* (workshop 3834344700), rebuilt from its Workshop description.

- **Condition** 0 (broken) to 1 (new) per weapon and armour item, kept in `ItemInstance.ModTag` as `dxseba.durability:<value>` (saved and
  copied with the item; an item another mod tagged is not tracked; untagged counts as new).
- **Wear** (`ICombatHitObserver`, applied in the serial slow step): the attacker's weapon (first tracked weapon on the hip, else the back) loses
  *Weapon wear per hit* (0.002) per landed melee hit; each worn armour piece whose ARMOUR coverage includes the part hit
  (`ArmourData.CoverageOf`) loses *Armour wear per hit* (0.002) x (1 + damage it stopped / 20).
- **Debuff** (`ICombatHitFilter`): below *Worn below* (39%) a weapon's cut, blunt and pierce fall linearly to -*Broken penalty* (-70%) at 0. A
  broken item stops wearing and stays, unless *Delete broken gear* is on.
- **Repair gear (weapon NN%, armour NN%)**: a row on your own character; with an item named "Tools" in its inventory, the weapon gains
  *Repair base* (0.15) + *Repair per skill* (0.35) x Weapon Smith / 100 and each worn armour piece the same by Armour Smith.
- **Missing** (partial): the tooltip display (shown in the menu row instead), the armour debuff (the engine's armour absorption has no mod
  hook), wear on blocks, the repair animation and sounds, Finite Ore pairing; the original's wear rates and repair formula are not published.
- **Unknown**: not checked in game; that vanilla data has an ITEM named exactly "Tools".
