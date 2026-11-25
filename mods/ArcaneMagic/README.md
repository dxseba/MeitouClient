# Arcane Magic (port)

Port of *Arcane Magic - Wizards of Kenshi* (Workshop 3784221846), built from:
- its plugin `plugin/ArcaneMagicNative.dll` v0.75.1 (Ghidra decompile and constants);
- its data and skeleton files.

The original is a required item. Its `ArcaneMagic.mod` must be enabled and loads as data: the Arcane Focus, the five Magi, factions, towns,
dialogue, journals, the Lucky Scavenger start, the Focus workbench and research, the Summonathan and the cast animations. Its `assets` meshes
are drawn. The plugin itself is never loaded.

## What it does

### Arcane skill and mana

- **Arcane skill** (Weapons column, start 0, up to 100), with the original's pane texts.
  - It is trained only by casting: 5 XP for a Missile volley, Earth Spike or Bomb; 10 for a Whirlwind; 15 for a Summon.
  - Each level needs 20 + 2 x level XP.
  - Every cast also trains Science, Friendly fire, Perception and Dexterity a little.
  - A Focus gives no Crossbows XP.
- **Mana**:
  - Capacity 100 + 2 x Arcane.
  - Regeneration 3 + 0.04 x Arcane a second, x1.5 while wearing the Arcane Levigator backpack.
- **HUD**: a **Mana** line with a bar on the HUD's character panel, beside a **Spell** line.
  - Click **Spell** for the next spell.
  - Shift + click locks or unlocks the shown spell (the original's Shift + right-click debug toggle).
  - The **Next Arcane spell** key action (unbound) cycles the selected characters.

### The Arcane Focus and its spells

The **Arcane Focus** (CROSSBOW `Arcane Focus Orb`) needs no ammunition. A shot casts the selected spell. A cast is held while its mana gate is
shut, as in the original.

- **Arcane Missile** (25 mana):
  - 1 missile below Arcane 25, 2 below 50, 3 below 75, 4 below 100, 5 at 100.
  - Damage x (1 + 0.04 x Arcane).
- **Earth Spike** (25 mana):
  - At the cast clip's hand contact (0.73 s), spikes rise under the target. There is one per missile the caster's Arcane level would fire.
  - They strike 0.18 s later, each pierce U(min, max) x height bonus x grade x 0.6.
  - They stand until 2.18 s.
- **Whirlwind** (40 mana):
  - At the cast's slap (0.7 s), a twister leaves the caster. It moves 135 units/s towards the target for 3 s (+0.5 s per extra missile in
    the caster's volley).
  - Every 0.2 s it strikes each living character within 30 units (20 up and down): pierce U(min, max) x height x grade x 0.3.
  - It spares the caster, its squad and its allies.
  - It stops at ground that can't be walked.
- **Summon** (all the mana, which drops to 0):
  - Summonathans stay for 150 s. Up to 1 below Arcane 50, 2 below 100, 3 at 100.
  - A player's join the caster's squad.
  - Vane's are of his faction, in a squad of their own.
- **Arcane Bomb** (100 mana and 20 flesh from the caster's healthiest part):
  - It is thrown 1.07 s into the cast. It flies at 180 units/s from the caster's hand to the aim point.
  - It bursts there, at ground above it, or after 12 s.
  - Everyone within 80 units (8 m) takes cut damage, the caster and friends too. Damage: (35 + 1.2 x Arcane) x grade.
  - Full damage within 25 units, falling to 35% at 80. Armour counts.
- **Damage terms**:
  - min and max are the Focus's pierce damage (its quality's) x (1 + 0.04 x Arcane), rounded.
  - The grade factor comes from the Focus's item level: 0.9 / 0.95 / 1 / 1.05 / 1.1 / 1.15 at 0 / 20 / 40 / 60 / 80 / 95.

### Magi and unlocks

- **Unlocks**: the Missile is always available. A Magus's death or imprisonment unlocks its spell for the player's faction, with the
  original's notice:
  - Vane: Summon;
  - Tor: Earth Spike;
  - Christopher: Whirlwind;
  - Malakor: Arcane Bomb.

  A locked spell shows `[LOCKED]` and can't be cast.
- **NPCs** with a Focus cast Missiles. The named Magi start at their Arcane level:
  - Vane 75, Christopher 78, Tor 86, Malakor 90, the Grand Magus 100.
  - They cast their own spell when its gate opens, else Missiles.

### Looks and saves

- **Clips**: the Focus aims with the original's `AM_ArcaneAim_Xbow`. After an Earth Spike, Whirlwind or Bomb, the recovery plays that
  spell's cast clip.
- **Models**: the original's spike, twister, bomb orb and burst meshes are drawn, rigid. Port-made motion stands in for their skeleton
  animations:
  - the spikes rise out of the ground and sink back;
  - the twister turns;
  - the orb spins;
  - the burst swells.

  The original's flash (`AM_ArcaneFlash_Proto`) marks the casts, spikes and bursts.
- **Focus grades**: a Focus wears the textures of its grade's vanilla weapon model (Rusted Junk to Meitou, by item level), as the plugin
  does.
- **Save keys**: the port writes the original's keys, so a save taken to the original keeps them. It reads a save of the original from them.
  - STATS floats `AM_arcane_level`, `AM_arcane_xp`, `AM_mana_current` and `AM_selected_spell`, written once a second.
  - `AM_temporary_summon` on summons.
  - The player faction's `AM_*_unlocked` floats.
- **Hits**: spell blows go through the game's own bolt hit (armour, toughness, wounds, knock-downs, relations, deaths, hit observers).

## Facts (Verified: decompile of `ArcaneMagicNative.dll` v0.75.1 and constants read from its `.rdata`, unless marked)

### Caster state, gates and XP

- **Per-character state**:
  - Arcane level (float, 0 to 100), XP pool, mana, selected spell.
  - Spells are numbered 0 Missile, 1 Summon, 2 Earth Spike, 3 Whirlwind, 4 Bomb.
  - Selector texts are `ARCANE MISSILE` and the others, with ` [LOCKED]` added for a locked spell.
- **Mana and levels**:
  - Capacity `level x 2 + 100`.
  - Regeneration `(level x 0.04 + 3) x dt`, x1.5 when the backpack section's item is named `Arcane Levigator`.
  - Level-ups happen while `xp >= level x 2 + 20`.
- **Gates** (`FUN_180001a00`): Missile mana >= 25, Whirlwind >= 40, Bomb >= 100, Summon a full bar.
- **Costs and XP**, from the log texts and constants:

  | Spell | Cost | Arcane XP |
  | --- | --- | --- |
  | Missile | 25 mana, once a magazine | 5 |
  | Earth Spike | 25 mana | 5 |
  | Whirlwind | 40 mana | 10 |
  | Bomb | 100 mana and 20 flesh | 5 |
  | Summon | mana to 0 | 15 (`DAT_18004dec0`) |

- **Event XP** on every cast (`xpStat_eventBased`): stat 4 (Science) 2.5, 36 (Friendly fire) 1, 24 (Perception) 1, 18 (Dexterity) 0.5.

### Spells

- **Volley and Missile**:
  - Volley count by level: 25 / 50 / 75 / 100 give 2 / 3 / 4 / 5.
  - Missile pierce min/max x `(1 + 0.04 x level)`, then `floor(x + 0.5)`.
- **Earth Spike**:
  - One spike per volley count, each `RangeRandom(min, max) x heightBonus x gun factors x 0.6 x grade`.
  - Pierce, bleed 1, hit direction 6, on the target.
  - Spikes appear when the cast clip passes 0.3667, strike 0.18 s later and live 2.18 s.
  - A cast whose clip is not playing after 4 s is cancelled.
- **Cast clip lengths**:
  - 2.0 s (spike), 1.8 s (whirlwind), 2.4 s (bomb).
  - **Verified**: animation lengths in the original's `AM_*_Male_v001.skeleton` files.
  - The clip time is a fraction of the clip (**Observed**: the end test is 0.99).
- **Whirlwind**:
  - Launched at clip 0.3889 (the slap).
  - Duration `3 + (volley - 1) x 0.5`.
  - Tick pierce `[min, max] x height x grade x 0.3`.
  - Speed 135, a step every 0.2 s, sphere 30 across and 20 up.
  - Skips the caster, the dead, the same platoon and allies either way.
  - Pierce, bleed 1, hit direction 6. Ends at unavailable terrain.
- **Bomb flight**:
  - Thrown at clip 0.4444 from `Bip01 L Hand` towards the shot's aim point.
  - 180 units/s in 0.02 s steps, with a 2.5-radius sphere trace that hits bodies after 15 units of flight.
  - Bursts on arrival, on a hit, at terrain above it, or after 12 s.
  - Flight clip 4.0 s, burst clip 0.8 s.
- **Bomb damage**:
  - `(level x 1.2 + 35) x grade`; the grade factor is stored with the cast (`FUN_18000f9f0`).
  - Sphere query 110, victims within 80 (3-D).
  - Full damage to 25, then `1 - min((d - 25) / 55, 1) x 0.65`.
  - CUT, bleed 1, everyone including the caster.
  - Paid at launch: mana -100 and 20 flesh off the part with the most.
- **Grade factor** from the Focus's item level: <20 0.9, <40 0.95, <60 1, <80 1.05, <95 1.1, else 1.15.
- **Summon**:
  - Cap 1 / 2 / 3 at level <50 / <100 / 100, less those alive.
  - Placed at caster + (2 + 3i, 0, 2i).
  - Lifetime 150 s; destroyed at expiry.
  - Same faction and platoon as the caster, its bodyguard.

### Casters and unlocks

- Players cast only unlocked spells.
- An NPC casts a spell only as its named Magus (string ids 46, 81, 151, 99; 197 is the Grand Magus).
- Magus baseline levels: 75 / 78 / 86 / 90 / 100.
- **Unlocks**:
  - WORLD_EVENT_STATE Alive / Imprisoned / Dead triples per Magus:
    - Vane 235 / 236 / 237;
    - Tor 241 / 240 / 242;
    - Christopher 249 / 250 / 248;
    - Malakor 252 / 251 / 253.
  - They are stored per faction (`Faction::saveState`).

### Focus, clips and saves

- **Ammunition**:
  - The plugin fakes ammunition for the Focus (`Inventory::hasItem`, `countItems`, `getNumItems`, `Character::_NV_hasAmmoFor`).
  - It suppresses the Focus's crossbow XP.
  - The CROSSBOW record names `ammo` 96014-rebirth.mod and `num shots` 5; the plugin overrides the magazine with the volley count.
- **Clips**:
  - The aim pose `aimH` is mutated to `AM_arcane_aim_pose` (ANIMATION 22 `AM_ArcaneAim_Xbow`).
  - The cast clips are ANIMATION 245, 247 and 255.
- **Save keys**:
  - `CharStats::serialise` writes `AM_arcane_level`, `AM_arcane_xp`, `AM_mana_current` and `AM_selected_spell` as floats into the STATS
    state (`GameSaveState::getState(0x19)`).
  - `Character::_NV_serialise` writes `AM_temporary_summon` = 1 there for a summon.
  - `Faction::saveState` writes `AM_summon_unlocked`, `AM_earth_spike_unlocked`, `AM_arcane_bomb_unlocked` and `AM_whirlwind_unlocked`
    (floats).
  - It also writes `AM_spell_manual_locks`, a float of a count or mask (**Unknown**).
- **Focus grade**:
  - The tier comes from the item level: >94 5, >79 4, >59 3, >=40 2, >19 1, else 0.
  - Tiers name a MATERIAL_SPECS_WEAPON: `Meitou`, `Edge Type 3`, `Mk III`, `Catun No.3`, `Old Refitted Blade`, `Rusted Junk`.
  - That model's textures and normal maps replace the Focus entity's `Tooth-Pick_DIF_HI.dds` and `AM_arcane_missile_DIF.dds` sub-entities.

## Port choices

- **One release is one cast**:
  - The data phase sets the Focus's `num shots` to 1 and removes its `ammo`, so Meitou's crossbow never runs dry.
  - The volley's shards leave together, each 1.5 to 4 degrees off the aim. The original fires the magazine shot by shot.
- **Spell blows** are bolt hits from the caster. The port's hit filter gives them the spell's packet.
- **Spell timing**: each spell starts at its clip's fraction times the clip's length, whether or not the clip plays. The original cancels a
  cast whose clip was cut.
- **Bomb flight**: the bomb does not trace bodies. It bursts at the aim point, at ground above it, or after 12 s.
- **Missile damage** is scaled on the bolt, not on rounded min/max.
- **Models and grade texture**: the spell models are rigid and moved by the port. The grade texture covers the whole Focus part, not only its
  base-texture sub-meshes.
- **Unlock check**: an imprisoned Magus unlocks its spell whoever holds it. For Vane, the original also checks that the player was involved.
- **HUD**: the mana HUD and selector are character-panel lines, not the original's widgets above the HUD.
- **Saving**: state goes into the port's sidecar by lasting id, and also under the original's keys.
  - A summon marked `AM_temporary_summon` that the sidecar does not list is removed after a load.
  - `AM_spell_manual_locks` is not written: its encoding is unknown.
  - The faction unlock keys are written but not read back; the sidecar keeps the unlocks.
- **NPC summons are not kept across a save**, on purpose: they are temporary, as the original marks them. A Harmony patch that keeps members a
  mod added to an NPC squad is not used: Meitou rebuilds an NPC platoon's members from its saved record, so the patch would have to make up
  platoon member records for creatures meant to vanish.

## Harmony

- `Meitou.Simulation.Combat.CombatSystem.Aim` (prefix: hold the release while the mana gate is shut).
- `Meitou.Simulation.Combat.CombatSystem.ApplyFire` (prefix and postfix: the Missile volley and its costs; any other spell replaces the bolt).
- `Meitou.Simulation.Combat.CombatSystem.Schedule` (postfix: the spells' queued blows land).
- `Meitou.Simulation.Movement.AnimationSystem.Update` (prefix: aim and cast clips; `AnimationSystem.Plays` is called through a delegate).
- `Meitou.Game.Effects.Debris.AddBolts` (postfix on the host thread, display only: the spell models go into `WorldGpu.DebrisPieces`).
- `Meitou.Data.Characters.CharacterAppearance.ChooseWeapons` (postfix: the Focus's weapon model by grade; changes the look only).
- `Meitou.Simulation.Saving.SaveCapture.WriteFactionsAndTowns` (postfix: the faction unlock keys go into the save's records).
- Reflection, no patch:
  - `Meitou.Simulation.Combat.CombatSystem.BoltHit` and `CombatSystem._projectiles`;
  - `Meitou.Simulation.Combat.ProjectileFlight._projectiles` and `NextId`;
  - the fields of `Meitou.Simulation.Combat.Projectile`;
  - `GameHost.Gpu`.

## Missing

- **The spell models' skeleton animations**: Meitou's loose-mesh pass draws rigid meshes. Skinning a free-standing mesh needs a new renderer
  path, which a patch cannot add, so the port moves the rigid models instead.
- **Focus Assembly Workbench "retained core section"**: an inventory-window change of the plugin, not traced. The workbench and its research
  are data and load.
- **Unknown**: not checked in game.
