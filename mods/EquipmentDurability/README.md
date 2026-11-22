# Equipment Durability (partial port)

Port of *Equipment Durability* (workshop 3834344700), rebuilt from its Workshop description.

- **Condition** 0 (broken) to 1 (new) per weapon and armour item, kept in `ItemInstance.ModTag` as `durability=<value>` of the shared `dxseba;key=value` tag (`ItemTag`; saved and
  copied with the item; an item another mod tagged is not tracked; untagged counts as new).
- **Wear** (`ICombatHitObserver` and `ICombatDefenceObserver`, applied in the serial slow step): the attacker's weapon (first tracked weapon on
  the hip, else the back) loses *Weapon wear per hit* (0.002) per landed melee hit, and the defender's weapon *Weapon wear per parry* (0.002) per
  blow it parries (a weapon block; fist blocks and dodges wear nothing); each worn armour piece whose ARMOUR coverage includes the part hit
  (`ArmourData.CoverageOf`) loses *Armour wear per hit* (0.002) x (1 + damage it stopped / 20).
- **Debuff**: below *Worn below* (39%) a weapon's cut, blunt and pierce (`ICombatHitFilter`) and a worn armour piece's cut, blunt and pierce
  defence (`IArmourFilter`, for every hit through it) fall linearly to -*Broken penalty* (-70%) at 0. A broken item stops wearing and stays,
  unless *Delete broken gear* is on.
- **Tooltip** (`AddItemTooltipLines`): gear this mod has worn shows "Condition NN%", with "(broken)" and "(effectiveness -NN%)" when they apply;
  never-worn gear shows no line.
- **Repair gear (weapon NN%, armour NN%)**: a row on your own character; with an item named "Tools" in its inventory, the weapon gains
  *Repair base* (0.15) + *Repair per skill* (0.35) x Weapon Smith / 100 and each worn armour piece the same by Armour Smith.
- **Missing** (partial): the repair animation and sounds, Finite Ore pairing; the original's wear rates, armour debuff curve and repair
  formula are not published (the armour debuff reuses the weapon's).
- **Verified** by `meitou-tools record "Tools"`: vanilla ITEM `43404-changes_otto.mod` is named "Tools". **Unknown**: not checked in game
  (builds only; the tooltip line, armour debuff and parry wear have not been seen running).
