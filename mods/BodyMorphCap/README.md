# BodyMorphCap (port)

Port of *BodyMorphCap* v7 (regulareverydaynormalmazaf, Workshop 3800922376, GPL-3.0-or-later), from the original's DLL (Ghidra decompile),
config and description. A character's body is drawn as if Strength and the two smithing skills (bulk) and Dexterity, Martial Arts, Swimming
and Athletics (muscle definition) were no higher than their caps; the stats themselves, combat and carrying do not change. Works on NPCs and
the player's characters (*Apply to player*). Settings under Mods on the Settings screen.

## Facts

- **What is capped** (**Verified**, decompile of its stat clamp, used by its three hooks `AppearanceHuman::updatePysiqueFromStats`,
  `updatePhysiqueMuscleMults` and `_NV_createBody`): for the hook's call the original lowers seven `CharStats` fields to their caps and puts
  them back after: strength (+0x80) to `cap`, weapon smith (+0xd0) to `cap_weapon_smith`, armour smith (+0xd4) to `cap_armour_smith`, and
  dexterity (+0x88), martial arts (+0x10c), swimming (+0xa8) and athletics (+0x94) to `cap_muscle`. Cooking and science, which lower the
  definition, are not touched. Port: the drawn build is made again from the capped stats ([animation](../../docs/animation.md) "Stat-driven
  body": bulk from max(strength, smithing), definition from swimming, athletics, dexterity and martial arts).
- **Skipped**: everyone with `enabled = 0`; player characters (`Character::isPlayerCharacter`) with `apply_to_player = 0`. **Verified** (as above).
- **Config** (`BodyMorphCap.config`, shipped): `enabled = 1`, `cap = 100`, `cap_weapon_smith = 100`, `cap_armour_smith = 100`,
  `cap_muscle = 100`, `apply_to_player = 1`, `language = auto` (auto / en / ru), `debug = 0`. A missing cap reads 100, `apply_to_player` 1.
  **Verified** (file and decompile of its reader). The port's settings take these defaults; the file is not read.
- **Mod Hub rows** (Emkejs-Mod-Core, optional): the same keys as switches and sliders 1 to 1000 step 1 (**Observed**: the slider floats 1,
  1000, 1 passed with each row; their order in the hub's struct is not traced). The labels and hints are the DLL's English strings; each
  Russian one is stored just before its English one in `.rdata` (**Verified**, read from the DLL). The port reads them from the installed
  original at start when the game is Russian or *Menu language* is `ru` (`OriginalLabels`); nothing of the original ships.
- **When it shows** (**Observed**, description): characters already drawn keep their body until it is rebuilt (zone re-entry, load). Meitou
  draws the build from the stats at every publish, so a capped body shows at once; the bone shape follows the engine's rule (made once,
  rebuilt only while of the player's faction) for the capped values, and is made again when a setting changes (port choice).
- Robots and animals have no stat-driven body in Meitou (as in the original's appearance code), so they are left alone.
- **Harmony**: postfix on `Meitou.Simulation.World.FillBody` (the publish of a character's body), setting `CharacterSnapshot.Build` through
  its non-public setter. A throw is logged once and the engine's build stays.
- `debug = 1` writes `BodyMorphCap.log` in the game folder; the port's *Debug log* writes the first 20 clamped bodies to the mod log.
- **Unknown**: not checked in game.

## Missing

- The title screen's saved-character preview builds its body through a generic method Harmony cannot patch (docs/modding.md "Platforms"), so it
  shows the uncapped body.
