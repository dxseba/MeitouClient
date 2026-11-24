# Elemental Enchantment (port)

Port of *Elemental Enchantment* (workshop 3832978749), rebuilt from its description and its decompiled plugin (`ElementalEnchantmentTest.dll`).
The original's data (`Elemental Enchantment.mod`: Elemental Haven's town, raids, bosses, wards, cores, recalibrators, vendors, workbench) and
particles load when that mod is enabled beside this one.

- **Model**: every character has one elemental level (experience 0 to 47600; level L needs (4 L + 76) L, so 80 for 1 and 47600 for 100), a
  main element and from level 50 a secondary one, chosen in its **ENCHANTMENT** window (the ENCHANTMENT button under the stats window, the "Enchantment"
  row of your character's menu, or the ENCHANTMENT line of the HUD's character panel; dragging its title moves it). The main element weighs 1; a secondary unlike it 0.25 at 50, then 0.35 / 0.45 / 0.55 / 0.65
  by decade and 0.75 at 100. The stats window's *Elemental Enchantment* skill shows the level and the way to the next. Kept by lasting id in
  the sidecar (version 2; a version 1 save's active element becomes the main one).
- **Bonuses** (`ICombatHitFilter`, melee and bolts), base = cut + blunt of the blow (at least 0), m = *Effect strength* (cap 5), w = weight:
  Fire blunt + m w (0.05 + 0.001 L) base; Water cut + m w (0.05 + 0.001 L) base; Ice cut + m w (0.02 + 0.0008 L) base and bleed multiplier
  + m w (0.1 + 0.004 L); Lightning armour penetration + m w (0.05 + 0.001 L).
- **Speed** (`ICombatSpeedFilter`): attack rate x (1 + m (w_Wind (0.02 + 0.0008 L) + w_Lightning (0.01 + 0.0004 L))); block rate x (1 + m
  w_Wind (0.02 + 0.0008 L)); rates above 100 untouched. Crossbow reload (`Reload`) x (1 + m w_Wind (0.02 + 0.0008 L) + 0.02 r with a Wind
  resonance) and bolt speed (`Projectile`) x the attack factor without resonance, each only when that factor is between 1 and 2 (a bolt
  only below 10000 units/s).
- **Experience**: each landed hit (melee or bolt, not an execution) with a main element gives 4 x *Elemental XP multiplier* (in tenths, at
  most 20x) x the main element's aptitude %, hundredths carried. **Aptitude**: 60% each, the favoured element 100% and its two neighbours on
  the ring Fire, Wind, Ice, Water, Lightning 80%; 1 in 100 characters 100% in all (rolled from the lasting id); *All elemental aptitudes 100%*
  flattens it. *Recruit ability chance* (100%) decides whether a new character has elemental ability at all.
- **Where the record lists come from** (the port ships none): the bosses, the wards and the recalibrator are read at start from the installed
  original's plugin (`plugin/ElementalEnchantmentTest.dll` in Workshop folder 3832978749 beside this mod, else in the code-less mod named
  "Elemental Enchantment"), read as bytes and never loaded (`PluginTables`, `PeImage`). A valid `bosses.json` / `items.json` in this mod's
  folder overrides the plugin's bosses / wards and recalibrator. Each part the plugin does not yield (not installed, or a shape or value that
  does not match) is none, logged once.
  - Plugin layout (**Observed**, Ghidra decompile and the bytes of the Workshop build): the bosses are a static array of 24-byte rows in
    `.rdata` (id pointer, main, secondary, level, padding; elements in the port's `Element` numbering), indexed by a small accessor that
    bounds-checks against the count; the wards are not data but one check function (id length, a shared tail string, a first character
    mapped to an element); the recalibrator is a literal compared in the item-use handler. The reader finds each by its instruction pattern
    in `.text` (exactly one match required), follows the RIP-relative address, and checks every id (`<n>-<file>.mod/.base`, the length the
    code checks, the record type where the loaded data has the id: a boss row naming another type is left out, a ward or the recalibrator naming
    a non-ITEM rejects that part), element range, distinct main/secondary and ward elements, level 1..100.
  - Reader **Observed**: a scratch program run against the installed DLL returned 70 bosses, 5 wards and the recalibrator, identical (hash)
    to an independent byte-level extraction; a copy with one corrupted level was rejected. `mod-check` on autosave0 (base game plus the enabled mods): one boss row
    names a FACTION_CAMPAIGN there and is left out; the wards and the recalibrator pass.
- **Bosses**: the listed CHARACTER records fight with a fixed main, secondary and level. Override format, record id to
  `[main, secondary or "None", level]`:
  `{ "1234-example.mod": ["Fire", "Lightning", 80],`
  `  "5678-example.mod": ["Ice", "None", 45] }`
- **Wards and recalibrator** override format (`items.json`):
  `{ "wards": { "11-example.mod": "Fire", "12-example.mod": "Ice" }, "recalibrator": "13-example.mod" }`.
  A listed ward worn in the belt slot takes *Device damage reduction* (10%, 0 to 30) off incoming cut, blunt and pierce (none at effect
  strength 0). Main, secondary and ward all different is a **resonance**: per element in it, at r = *Resonance strength* x effect strength
  (cap 5), Fire +2% of the base as blunt, Lightning +0.02 armour penetration, Water +2% as cut, Ice +0.05 bleed, Wind +2% of the play rate.
  The recalibrator's right click in the bearer's own inventory (not a bag, never in trade) uses one up and rerolls the aptitude
  (`AddItemUse`): an ordinary character becomes rare 1 time in 100, else favours one of the four other elements; a rare one becomes ordinary
  with any of the five favoured; one without ability gains it with a fresh aptitude (one of five, rare 1 in 100), its main element the
  favoured one (any of five when rare), no secondary, its experience kept. The original seeds the roll from the clock; the port from the
  lasting id and the tick (deterministic).
- **Verified** (decompiled DLL): the ward is the item of the belt section (`getSectionOfType` with AttachSlot 14, `mov edx, 0xe` before the
  call); the reroll rule above (its 1-in-100 and pick-one-of-four branches, the element order Fire, Lightning, Wind, Water, Ice and the
  ring table 0, 2, 4, 3, 1); a bolt carries the main element's and, when set, the secondary's bolt system, behind a switch that is 1 in
  `.data` and never written (no setting, no level of its own).
- **Looks**: while a character has a main element its held blade gives off the original's blade system (`SB_Prototype_Fire`,
  `SB_Prototype_<Element>Blade`) and, once the secondary counts, the secondary's too (`KeepEffect` on `EffectAnchor.Weapon`); unarmed, both
  hands (`Bip01 R/L Hand`) give off `SB_Prototype_HandFire` / `SB_Prototype_<Element>Hand`, sized 0.8 / 0.9 / 1 / 1.1 / 1.15 (below level 20,
  40, 80, 100; at 100). The blade systems are sized by the level too
  (`EffectAnchor.Weapon(...).Sized`), and every size is 1.3x for 0.18 s after the character lands a hit (renewed in place). A bolt an
  enchanted character shoots carries its main element's `EE_Bolt_<Element>` (Fire `SB_Prototype_HandFire`), and the secondary's once it
  counts, until it lands
  (`AddProjectileFeed`, `EffectAnchor.Projectile`). From level 20, with *Weapon trails*, an armed character in combat leaves a trail along its
  blade (`KeepTrail`, material `EE_Trail`, the original's element colour at alpha 0.28 fading out over 0.47 s). From level 40, with *Impact effects*, hits burst the main's and the secondary's `EE_Impact_<Element>` on the victim for
  1 s; a ward's wearer shows `EE_AuraPreview` (Fire) / `EE_DeviceStorm` (Lightning) / `EE_Device<Element>` at its pelvis on its hits
  (*Device combat effects*). Without the original's particles, base-game stand-ins in the element's colour (`kenshi_fire1_small`,
  `kenshi_sparks`, `kenshi_smoke3_small`, `kenshi_light_dust_particles1`; impacts `small- fire2_torch`, `small- sparks1`,
  `small- smoke3_small`, `Dustkick01`, `Footsplashes01`; **Verified** base-game with `meitou-tools record` and `particles effects`).
  *Blade tint* (off; this port's own) also tints the blade.
- **Verified** (decompiled DLL and the constants read from it): every formula and number above, the visual table (hand, blade, impact and bolt systems per element; Fire's bolt is `SB_Prototype_HandFire`), the settings' keys and
  defaults (`ExperienceMultiplier` 1, in tenths up to 20; `EffectMultiplier` 1 up to 5; `ShowWeaponTrails` and `ShowImpactEffects` on;
  `AllAptitudes100` off; `RecruitAbilityStep` 10 = 100%; `ResonanceMultiplier` 1; `DeviceDefensePercent` 10 up to 30; `ShowDeviceEffects` on;
  `UIScale` 1).
- **Verified** (decompiled DLL): the reload and bolt factors and their 1..2 window; the trail's material, colours, alpha 0.28 and fade 0.59 a
  second, level 20 and "while attacking".
- **Differences**: the trail is a band over the whole blade where the original's is one camera-facing ribbon point on the blade (the weapon
  anchor takes no offset; its width is **Unknown**), and it shows while the character is in combat (the snapshot has no attack state); the
  stats window button sits just below the window (the original's place is **Unknown**).
- **Missing** (engine): the original's translations (the plugin's texts in nine languages and the localisation patch's `.eepatch` files;
  Meitou has no per-mod translation API).
- **Unknown**: whether the bolt's secondary system needs the secondary to count (level 50, as the port takes it) or only to be set;
  everything in play (window, bonuses, bosses, wards, effects) until tested in game.
