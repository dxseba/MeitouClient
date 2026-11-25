# The Force Awakening

Port of *The Force Awakening* (Workshop 3836973645, `TheForceAwakening.dll`, a build of the Kenshi RPG Classes engine), from a Ghidra
decompile of the DLL and the original's files. Nothing of the original is shipped: its cfg files, texts and 137 WAV sounds are read at runtime
from the installed original (the required Workshop item, or the code-less mod named `TheForceAwakening`), which must also be enabled for its
records (holocrons, contract, masters, dialogue, the Grey One start) and particle scripts.

- **Paths:** Jedi, Sith, Grey Jedi and Bounty Hunter. A pathless player character carrying a class item (Jedi/Sith/Grey Holocron, Hunter's
  Contract) learns its path at once; the item is kept. One path per character; Renounce in the grimoire (click twice within 5 s) frees it.
  One side per squad: a Jedi is refused while a Sith walks in the squad and the reverse (Mod settings lifts it). The Grey One start is born
  a Grey Jedi.
- **Skills:** *The Force* and *Wisdom* in the stats window's Combat column. The Force (Crossbows for the Hunter) unlocks the skills and scales
  their power; Wisdom raises Concentration. Both train as skills are used.
- **Pools:** Concentration (Jedi, Grey Jedi) refills by itself; Fury (Sith) fills from blows given and taken and drains to its floor out of a
  fight; Focus (Hunter) refills by itself.
- **Bar and cast mode:** a five-slot bar above the HUD for the HUD's character (click a slot), G toggles skill mode (1-5 use the slots; out of
  it they select squads as usual). A skill on the caster goes at once; a target or point skill waits for the next left click in the world
  (Esc cancels).
- **Grimoire (L, or the bar's button):** the path and pool, the two specializations and their three passives, the bar slots (pick a slot, then
  a skill), every skill with its level, cost, cooldown and the auto-cast tick (ticked bar skills are used by themselves in a fight).
- **The 21 skill kinds:** Force Push (cone: thrown back and knocked down), Force Wave (all round), Force Leap (leap onto the target; the
  landing knocks down those near), Command (a Force roll; on success the target forgets its hostility to your faction for 2 minutes; hurting it
  breaks it), Deflect (6 s: shots that hit go back at the shooter), Force Heal (him and friends near), Mind Trick (4 s: the target forgets
  why it fought), Battle Meditation (full pool: friends near get Attack and Defence for 20 s), Brainwash (30 s keeping the target in reach,
  then a roll; success brings it into the caster's squad), Force Choke (lifted, held 3 s, hurt each second), Force Lightning (three bolts
  leaping between enemies), Drain Life, Dark Rage (full Fury, 15 s: harder blows, never knocked down), Rapid Fire, Aimed Shot, Thermal
  Detonator (goes off a moment later), Stun Grenade, Dirty Kick, Smuggler's Luck (Athletics and Dodge), Flamethrower (a 1.5 s cone).
- **Sounds, particles, light:** each skill plays its WAV from the original's `sounds\` folder, its Particle Universe systems from the
  original's `fx/arcanista_fx.pu` when loaded, and a short coloured glow on the character.
- **Bounty contracts:** ask the Veteran Hunter for a contract: one of the original's 46 wanted men (cats and where he is usually found); bring
  him down with one of your characters within 600 and the cats are paid at once. "Remind me" tells it again; "drop" gives it back.
- **Casting:** the Sith's skills play their strike clip (heavy swing, chop down, bigchopv2); Brainwash plays the cast pose (`aimshoot`) and
  stands on the original's casting circle (`ARC_cast_magic_circle`) while it is cast. Brainwash can send its target to your squad or to any
  faction of the original's list (the grimoire's Brainwash button).
- **Masters:** the Jedi Master and Grey Master at The Hub, the Sith Lord at Mongrel and the Veteran Hunter at Squin are made at their place
  when a player character first comes near and none lives.

## Facts

**Verified** (decompile of `TheForceAwakening.dll` and the shipped files, read with a script) unless marked.

- `TheForceAwakening.cfg`: `spell<N>=kind|name|cost (or "tudo")|cast time|cooldown|power|level|class|strike animation`, 29 lines for 4 classes
  (`jedi`, `sith`, `cacador`, `cinzento`); `passive=class|tree|n|value`; `class_item=` (items 1, 6, 11, 16), `start_class=22-...|cinzento`,
  `bounty=` (46 contracts), `brainwash_faction=`, `element=`. "tudo" needs a full pool.
- `ForceBase.cfg` (the engine's balance): `cast_range=450`, `mana_base=50`, `mana_per_int=3`, `regen_base=1.5`, `regen_per_int=0.05`,
  `power_per_arcane=0.025`, `cost_cut_per_arcane=0.004`, `fury_hit=6`, `fury_hurt=4`, `fury_decay=3`, `fury_decay_delay=8`, `fury_floor=20`,
  `fury_floor_regen=2`, `focus_regen=6`, `hunt_duration=15`, `emp_radius=45`, `rush_range=120`, `passive_levels=20|45|70`,
  `sound_volume=1`, `sound_who=party`, `hud_bottom=230`, `auto_gap_player=0.5`, `auto_gap_npc=2`, keys `toggle_key=G`, `grimoire_key=L`.
- `sounds.cfg`: `<kind>.<cast|release|impact>=file,file`; one is drawn each time. All 137 WAVs are 16-bit PCM mono, at most 3 s (checked
  with a script), so they fit Meitou's loose-WAV path.
- The DLL's kind table maps kinds without lines of their own to RPG Classes kinds for sounds and effects: force_push/force_wave to air_sweep,
  force_leap to crack, deflect to faith_shield, force_heal to lay_hands, mind_trick to mark, battle_meditation to devotion_aura,
  force_choke to death_grip, force_lightning to strike, dark_rage to fury, thermal_detonator to fire, dirty_kick to dash, flamethrower to twin.
- The DLL's effect table (438 rows: kind, `.pu` system, start, seconds) names the systems the port plays (`ARC_air_sweep_ring` ...).
- Masters: table `jedi 5-/2-`, `sith 10-/7-`, `cacador 15-/12-`, `cinzento 20-/17-` (squad, character) at (2639, -51181), (-37504, -83004),
  (15956, -61876), (2639, -51181); made when a player character is within 1500 and none lives, faction `51707-rebirth.mod`, at the nearest
  town, 30 off the place.
- Skill texts (durations 6 s, 4 s, 20 s, 3 s, 15 s, 2 minutes, 30 s, three bolts, 1.5 s) are the original's `DESC_*` lines in `translations.txt`.
- The `.mod`'s own record names are Portuguese; the plugin shows `=id|field|en|pt|ja|ru|zh` lines of `translations.txt`. The port applies
  them in the data phase in the game's language; the interface texts (`@KEY|...`, also `engine_texts.txt`) likewise.
- **Observed** (cfg comments): Concentration's maximum is `mana_base + mana_per_int x Wisdom` and its regeneration `regen_base +
  regen_per_int x Wisdom` per second (the RPG Classes mana with Wisdom for Intelligence, as `STAT_WIS_NOW` says).
- Skill damage (the DLL's damage helper): x `skill_damage=0.8`, x (1 - target Dodge/100 x `dodge_max=0.3`), then `MedicalSystem::addWound`
  with hit direction 6, the game's hit landing (part pick, worn armour, toughness; docs/game/combat.md "Armour and medical hit"); before it the
  attacker gets `rememberCharacter`/`attackingYou` (aggro) and, a player's character hitting a non-player, `setCrime(5)` (Assault). The
  cfg says the same ("sem linha = fisico (so a armadura defende)"). The element resistances it adds come from RPG Classes' class armour, which
  this mod does not have, so they never apply.
- Bounties: lines 124 (`arc_bounty=none`), 126 and 128 (`has`); replies 123 and 125 (`arc_open=bounty_new`) and 127 (`bounty_drop`); the plugin's
  contract check pays when a non-player character of the record is dead or down and a living player character is within 600; a new contract
  is drawn with `rand()` among those not done.
- Casting: Brainwash (kind code past the seven mage kinds) gets `cast_magic`, so `ARC_cast_magic_circle` (60 s rows in the effect table);
  `cast_animations=ARC_cast_staff,aimshoot,reload 1,AM_...,ARC_bow_reload` and the cfg's comment that the staff and bow clips are class-only.
- **Unknown** (port choices): the reaches of push (150, 45 degrees), wave (100), leap landing (40), heal (100), meditation (150), lightning
  hops (80), flame (120, 30 degrees), detonator (50); damage as cut/blunt/pierce from the power; knock-down seconds; throw speed 140; the Force
  roll (50 % + 2 per level of difference, 5-95 %); training per cast (cost x 0.01 levels); Dark Rage's harder blows (+power %).

## Harmony and hooks

- Harmony: `Meitou.Game.PlayerInterface.LeftDown` (prefix: the armed skill takes the next left press in the world; the ground point from
  `PlayerInterface.Ray` and `WorldPicking.MoveHit`, read by reflection).
- Harmony: `Meitou.Simulation.Dialogue.DialogueSystem.LastChecksPass` (postfix: `arc_bounty` none/has) and
  `Meitou.Simulation.Dialogue.DialogueSystem.Say` (prefix: `arc_open` bounty_new/bounty_drop).
- Harmony: `Meitou.Simulation.Population.PopulationData.FactionIndex` (prefix, active only inside the port's own call of
  `Meitou.Simulation.Factions.SquadManagement.Dismiss`, by reflection: Brainwash to another faction).
- `HitResolver.LandDetailed` (armour), `AggroSystem.Force`, `CrimeSystem.Assault`, `AddCombatMove` (`CombatMove.Clip`), `KeepEffect` /
  `StopEffect` (the circle).
- Mod API: `AddSkill`, `AddStatusEffect`, `AddSettings`, `AddKeyAction` (G, L), `AddKeyInput` (1-5 in skill mode, Esc), overlay UI,
  `AddFrame`, `PlayEffect`, `Glow` (the runtime light), `World.PostSound(PostedSound)` with the original WAV's full path (the loose-WAV path:
  no Harmony needed), `ICombatHitFilter` (Deflect, Dark Rage), `ICombatHitObserver` (Fury, Command breaking), `ITargetScoreFilter` (Command,
  Mind Trick), `CharacterPlacement.Place` (Leap), `CharacterCold.Launch` (throws), `MedicalState.KnockOut`/`ApplyHit`/`WakeUp`,
  `PopulationSystem.JoinPlayerSquad` (Brainwash), `PopulationSystem.SpawnPlatoon` (masters), `IModSaveData`, `IModDataPhase`.

## Port choices

- Timed effects (Deflect, Rage, Meditation, Command...) end on a load; the paths, bars, ticks, specialization and pools are saved.
- Mind Trick holds the target's hostility down for its seconds (it drops its fight) instead of a dazed pose.

## Missing

- The original's custom `.hlsl` / `.program` shaders (its glowing forms and blades): Meitou draws no Ogre shader programs, and Harmony cannot
  supply one (the renderer compiles its own shaders; a mod has no hook to add a pipeline). The particle systems, which use plain materials, play.
- The masters being put back at their place: the original teleports a living master there when a height test passes (its two y values
  50 apart); what that test stands for is **Unknown**, so it is not ported. The Grey Master's wandering comes from his data's AI package.
- NPC adepts: only player characters learn paths (the masters do not cast).
- **Unknown**: not checked in game.
