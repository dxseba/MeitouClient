# Happy Families (port)

Port of *Happy Families (Natural Childbirth) [RE_Kenshi]* (workshop 3800064617): a KenshiLua script (`scripts/init/BaseScript.lua`) plus
`HappyFamilies.mod`. The behaviour is rewritten in C# from what the script does; the `.mod` (five `HF_Random<race>` CHARACTER templates) loads
as data from the original, a required item.

- **Conceiving:** every 30 ticks, a woman of the player's faction lying in a bed may conceive with each man lying in another bed of the same bed
  building (a double bed or bunks; any faction): 30% a pregnancy, else no further try for 0.1 to 0.2 game days. Refused (no try) when the
  last failed try is too recent, her squad or the faction has no room, either race is not one of the five, either is still a child, or they share
  an ancestor within four generations.
- **Pregnancy:** half a game day; her Stomach slider grows by up to 200 and with a third left she says "I think I'm pregnant...". At the end the
  slider is put back and the child is born, unless the couple check now refuses (she says why).
- **The child** (`PopulationSystem.SpawnIntoPlayerSquad`, past the caps, which the check above tested): the `HF_Random<race>` template of the
  mother's or father's race (50/50, by race name; else the mother's own CHARACTER), in her squad at her side. Named with the father's surname
  (his name after its first space). Looks: skin tone at one random point between the parents', each of the original's ~100 sliders at its own
  random point, no beard. Stats: a quarter of the parents' summed stored stats for the 27 skills the script really reaches (see Facts).
- **Growing up:** over 10 game days its child sliders (height, build, head; Shek have their own table) move to its mixed adult values, drawn
  again each 1% (`CharacterEditing.Apply` as a recruit's edit), and each body part's maximum health rises from 0.3 to 1 times the parents'
  average for that part (`IPartMaxFilter`), kept after it grows up.
- **Family tree** (`AddKeyAction`, default F): a 600 x 150 window at the top left, "Family Tree of <name>", Father / Mother (" (Dead)" once gone,
  "Unknown" for none), "Children: ..." or "No Children."; it follows the selected player character; F or X closes it. Texts go through
  `context.Tr`.
- **Saved** in the mod's save data (`IModSaveData`, by lasting id): lineages, pregnancies and failed-try waits; all of it, and the roll
  counter, is in the state hash (`IStateHashed`, id and key order).
- **A save the original made** (no data of this port's in it): at load (`IWorldReady`) the family keys the script left in each saved
  character's look record are read (Facts); parents are found by their handle text.

## Facts (Verified: reading `BaseScript.lua` and `HappyFamilies.mod`'s strings, unless marked)

- Times in game days: pregnancy 0.5, childhood 10 (`TimeMultiplier` 1); retry 0.1 to 0.2; chance 0.3; child health factor 0.3; belly +200 x
  progress over the pre-pregnancy Stomach; the "pregnant" line at a third of the time left. The Workshop text's "~1 ingame day" is not the code.
- Runs every 40th `onCharsUpdate` over the player's characters that are human: grow (a child does nothing else), then a woman's pregnancy, then,
  not unconscious and inside a building, every operator of its useable stuff is a candidate father (not herself, not female).
- Races: string ids `17-gamedata.quack` Greenlander, `18019-gamedata.base` Scorchlander, `5276-chareditor.mod` Shek, `2603-gamedata.base`
  Cannibal, `50523-rebirth.mod` CannibalSkav, each needing CHARACTER `HF_Random<name>`; the child's template is looked up by the parent's race
  **name** instead, falling back to the mother's record (so a Cannibal Skav's child takes the mother's record).
- Room: squad size + 1 and player characters + 1 each below `max squad size` / `max faction size`.
- Related: ancestor sets 4 deep including the person; any overlap refuses.
- Stats: the child's field = (mother + father) / 4 for each name of the script's list. **Observed** (KenshiLua's `docs/BindingsReference.md`,
  CharStats fields): `strength`, `dexterity`, `athletics`, `meleeAttack` and `meleeDefence` are not fields (the fields are `_strength`,
  `_dexterity`, `_athletics`, `__meleeAttack`, `_meleeDefence`), so the script never reads a parent's value there (nil, taken as 0) and its
  write does not reach the stat; the port leaves those five out. `toughness` is commented out; Stealth is not listed. **Unknown**: whether the
  binding ignores or raises on those writes (raising would also skip the script's last look update of the birth, which the next pass makes).
- `fitness`: **Verified** (`kenshi_x64.exe` 1.0.68, CharStats reader `FUN_14064b6a0` at `14064b7fe`): the STATS key `endurance` is read into
  CharStats +0x84, the slot after `strength` (+0x80) where KenshiLib's field order puts `fitness`. No `StatsEnumerated` uses +0x84
  ([character stats](../../docs/game/character-stats.md) table), so it has no Meitou stat; Meitou keeps `endurance` only as a saved legacy
  float (`LegacyStats`), which `world.SaveFields` refuses as an engine key. Not ported, like the other fields without a stat (survival, arrow
  defence, bluff, tracking, climbing, doctor, the spirit and alignment values).
- Handle text: **Verified** (`hand::toString`, RVA 0x9B5750 by `docs/formats/kenshilib-1068.tsv`, disassembly; save keys from the handle writer
  `FUN_1406bca80`): `I-S-C-CS-TYPE`, the first four as unsigned 32-bit, the type signed, joined by `-`.
- A father no longer at hand at the birth is replaced by the mother, and the check then refuses as "Related".
- The script's own save file (`HappyFamilies_FamilyData.txt`) is never written: its handlers are commented out. The data lives in the look's
  maps: string `FamilyTable` (`own;mother name;mother handle;father name;father handle[;father of the current child]`, `no` for none; written
  for children only), strings `MotherName`/`FatherName`, floats `ComeOfAgeTime` (or int), `WHENADULT_<slider>`, `WHENADULT_LIMB_<k>`,
  `WHENCHILD_LIMB_<k>`, `PregnancyEndTime`, `Stomach_Original`, `NextAllowedChildCheck`, bool `bJustSaidImPregnant`. A mother's father of the
  current child is in no saved field unless she was born in the mod, so after a reload the original's birth falls back to the mother ("Related"). Translations are Lua globals (`MODSTRING_*`) a
  separate file may set; none is shipped.
- **Unknown**: not checked in game.

## Port choices

- A bed fixture holds one occupant in Meitou, so "sharing a bed" is lying in beds of the same bed building (the original reads the bed
  building's operators, which a shared-bed mod makes several).
- Look edits go through the recruit editor's path, which sanitises slider values to the race's limits (the original keeps any value).
- Family data is kept in the mod's save data, not in the look. Importing an original save: **Unknown** whether Kenshi writes the script's keys
  into the saved look record and whether a character's runtime handle equals its saved one (the log line counts lineages whose own handle
  matched); a parent not found keeps its name and shows " (Dead)". `WHENADULT_LIMB_<k>` keys are taken as positions in the body's part list,
  1-based when the lowest is 1 (**Unknown**: the binding's key type).
- Randomness is `Rng.Hash` on the world seed (deterministic).
- F may be taken by a game binding: then the default is left out (log line) and the key is bound on the Controls page.

## Harmony

- None.

## Missing

- `fitness` (STATS `endurance`): no Meitou stat stores or reads it, and `world.SaveFields` refuses `endurance` as an engine key, so a child's
  value cannot be set without an engine change (a Harmony write into the save would set a number nothing reads).
- Not checked in game; the import of an original save is untried (no such save on hand).
