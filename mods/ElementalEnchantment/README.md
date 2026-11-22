# Elemental Enchantment (port)

Port of *Elemental Enchantment* (workshop 3832978749), rebuilt from its description and its decompiled plugin (`ElementalEnchantmentTest.dll`).
The original's data (`Elemental Enchantment.mod`: Elemental Haven's town, raids, bosses, wards, cores, recalibrators, vendors, workbench) and
particles load when that mod is enabled beside this one.

- **Model**: every character has one elemental level (experience 0 to 47600; level L needs (4 L + 76) L, so 80 for 1 and 47600 for 100), a
  main element and from level 50 a secondary one, chosen in its **ENCHANTMENT** window (the "Enchantment" row of your character's menu, or the
  ENCHANTMENT line of the HUD's character panel). The main element weighs 1; a secondary unlike it 0.25 at 50, then 0.35 / 0.45 / 0.55 / 0.65
  by decade and 0.75 at 100. The stats window's *Elemental Enchantment* skill shows the level and the way to the next. Kept by lasting id in
  the sidecar (version 2; a version 1 save's active element becomes the main one).
- **Bonuses** (`ICombatHitFilter`, melee and bolts), base = cut + blunt of the blow (at least 0), m = *Effect strength* (cap 5), w = weight:
  Fire blunt + m w (0.05 + 0.001 L) base; Water cut + m w (0.05 + 0.001 L) base; Ice cut + m w (0.02 + 0.0008 L) base and bleed multiplier
  + m w (0.1 + 0.004 L); Lightning armour penetration + m w (0.05 + 0.001 L).
- **Speed** (`ICombatSpeedFilter`): attack rate x (1 + m (w_Wind (0.02 + 0.0008 L) + w_Lightning (0.01 + 0.0004 L))); block rate x (1 + m
  w_Wind (0.02 + 0.0008 L)); rates above 100 untouched.
- **Experience**: each landed hit (melee or bolt, not an execution) with a main element gives 4 x *Elemental XP multiplier* (in tenths, at
  most 20x) x the main element's aptitude %, hundredths carried. **Aptitude**: 60% each, the favoured element 100% and its two neighbours on
  the ring Fire, Wind, Ice, Water, Lightning 80%; 1 in 100 characters 100% in all (rolled from the lasting id); *All elemental aptitudes 100%*
  flattens it. *Recruit ability chance* (100%) decides whether a new character has elemental ability at all.
- **Bosses**: CHARACTER records listed in an optional `bosses.json` in this mod's folder fight with a fixed main, secondary and level (the
  port ships no list; the original's own table lives in its DLL). Absent or invalid: no enchanted bosses, logged once at start. Format, record
  id to `[main, secondary or "None", level]`:
  `{ "1234-example.mod": ["Fire", "Lightning", 80],`
  `  "5678-example.mod": ["Ice", "None", 45] }`
- **Wards and recalibrator** come from an optional `items.json` in the same folder (none without it):
  `{ "wards": { "11-example.mod": "Fire", "12-example.mod": "Ice" }, "recalibrator": "13-example.mod" }`.
  A listed ward worn in an equipment slot takes *Device damage reduction* (10%, 0 to 30) off incoming cut, blunt and pierce (none at effect
  strength 0). Main, secondary and ward all different is a **resonance**: per element in it, at r = *Resonance strength* x effect strength
  (cap 5), Fire +2% of the base as blunt, Lightning +0.02 armour penetration, Water +2% as cut, Ice +0.05 bleed, Wind +2% of the play rate.
  The recalibrator's right click in the bearer's own inventory (not a bag, never in trade) uses one up and rerolls the aptitude to a different
  result, granting ability to one without it (`AddItemUse`).
- **Looks**: while a character has a main element its held blade gives off the original's blade system (`SB_Prototype_Fire`,
  `SB_Prototype_<Element>Blade`) and, once the secondary counts, the secondary's too (`KeepEffect` on `EffectAnchor.Weapon`); unarmed, both
  hands (`Bip01 R/L Hand`) give off `SB_Prototype_HandFire` / `SB_Prototype_<Element>Hand`, sized 0.8 / 0.9 / 1 / 1.1 / 1.15 (below level 20,
  40, 80, 100; at 100). From level 40, with *Impact effects*, hits burst the main's and the secondary's `EE_Impact_<Element>` on the victim for
  1 s; a ward's wearer shows `EE_AuraPreview` (Fire) / `EE_DeviceStorm` (Lightning) / `EE_Device<Element>` at its pelvis on its hits
  (*Device combat effects*). Without the original's particles, base-game stand-ins in the element's colour (`kenshi_fire1_small`,
  `kenshi_sparks`, `kenshi_smoke3_small`, `kenshi_light_dust_particles1`; impacts `small- fire2_torch`, `small- sparks1`,
  `small- smoke3_small`, `Dustkick01`, `Footsplashes01`; **Verified** base-game with `meitou-tools record` and `particles effects`).
  *Blade tint* (off; this port's own) also tints the blade.
- **Verified** (decompiled DLL and the constants read from it): every formula and number above, the visual table (hand, blade, impact and bolt systems per element; Fire's bolt is `SB_Prototype_HandFire`), the settings' keys and
  defaults (`ExperienceMultiplier` 1, in tenths up to 20; `EffectMultiplier` 1 up to 5; `ShowWeaponTrails` and `ShowImpactEffects` on;
  `AllAptitudes100` off; `RecruitAbilityStep` 10 = 100%; `ResonanceMultiplier` 1; `DeviceDefensePercent` 10 up to 30; `ShowDeviceEffects` on;
  `UIScale` 1).
- **Missing** (engine): weapon trails from level 20 (an Ogre `RibbonTrail` with `EE_Trail`; no mod trail API); bolt effects `EE_Bolt_*` (no
  anchor on a projectile in flight); Wind's crossbow reload speed and the bolt speed (no reload or shot-speed filter); the blade's level size
  and the 1.3x "hit glow" for 0.18 s after a hit (`EffectAnchor.Weapon` has no stretch multiplier); the window's button in the stats window and
  dragging it (no stats-window slot, no drag events for mod nodes); the original's translations.
- **Unknown**: which worn slot the original reads a ward from (any equipment slot here); the recalibrator's exact reroll rule (here: a
  different result); everything in play (window, bonuses, bosses, wards, effects) until tested in game.
