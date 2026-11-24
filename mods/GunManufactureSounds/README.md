# Firearms Sound Effects - GunManufacture+ (port)

Port of *Firearms Sound Effects - GunManufacture+* (workshop 3087291056), from its plugin (`GunManufactureRealFireSounds.dll`, Ghidra
decompile), its data and its `RE_Kenshi.json`. The original is a required item: enabled, its `.mod` (the guns' reload animations and their gun
cues) loads as data and its `GunSounds.bnk` loads through its `RE_Kenshi.json` `SoundBanks` ([mod sound banks](../../docs/audio.md#mod-sound-banks)).
The port reads the gun table from its plugin at start (bytes only, never loaded). Needs GunManufacture+.

- **Shot**: a shot (`Attack` on the shooter) by a character holding a gun of the table plays that gun's bank event on the shooter
  (`ModContext.PostSound(event, shooter)`) instead of the crossbow sound.
- **Reload cue**: an animation sound event whose name is one of the table's events (the reload animations post them) is dropped, on everyone.
- Other crossbows, turrets and every other sound are unchanged.

## Facts (Verified: decompile of `GunManufactureRealFireSounds.dll` and the bytes of the Workshop build, unless marked)

- The table: 69 rows of 16 bytes at `0x18000c100` (record number int32, 4 zero bytes, pointer to the event name); 35 distinct events
  (`cal50`, `spas12`, `ak`, `vss`, ... `uzi`). The record number is compared with the character's equipped crossbow's game data id (`+0x1c`),
  which is the number before the `-` of its string id (e.g. 1585801-Gun manufacture+.mod, AA-12, plays `spas12`). **Verified** also by the
  port's reader (`GunTable`, a scratch run against the Workshop DLL: 69 rows, 35 names, the rows above).
- `Character::audioEvent` hook (`FUN_1800021f0`): with a mapped gun and an event that is a vanilla crossbow sound (`FUN_180001670`: lower-case
  `crossbow`, `harpoon_gun`, `crossbow_turret`, `harpoon_turret`, or containing `crossbow` / `harpoon` but not `reload`), the mapped event is
  posted instead.
- `AK::SoundEngine::PostEvent` hook (`FUN_180002d30`): an event named as a table event (case ignored, `FUN_180001a60`) is cached for its game
  object and suppressed (return 0); a later `Attack` on that object is redirected to the cached event. Equip and unequip clear the cache.
- The data (`GunManufactureSounds.mod`, read with `FcsReader`): 37 ANIMATION and 37 ANIMATION_EVENT records (event = the gun's name), 71
  CROSSBOW changes setting `reload anim`.
- In Meitou the shot is `SoundKind.Shot` → `CharacterSounds.Shot` (`Attack` with the `Weapons` switch), and animation events post through
  `CharacterSounds.Fire` (`AnimationSFXEvent::vfunc_1`).
- **Unknown**: not checked in game (GunManufacture+ is not installed here).

## Harmony

- `Meitou.Game.Audio.CharacterSounds.Fire` (prefix): skips a table event.
- `Meitou.Game.Audio.CharacterAudio.PlayCombatSound` (prefix): for `SoundKind.Shot` with a mapped gun (the shooter's crossbow part in the
  snapshot on screen), posts the gun's event and skips the vanilla `Attack`.
- Each logs once on a failure and lets the game's sound play.

## Missing

- Nothing.
