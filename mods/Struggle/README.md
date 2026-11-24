# Struggle (port)

Port of *挣扎/Struggle* v1.3.1 (Workshop 3810561672), from its DLL (`Struggle.dll`, Ghidra decompile) and description.

- **Struggle row**: a carried character of yours shows "Struggle (Chance NN%)" on its own right-click menu and on its carrier's; a click
  rolls. Success: the carrier lets go of it. Failure: a cooldown, and with failure punishment a slap to the head and a knock-out.
- **NPC struggle** (off by default): carried NPCs try by themselves each time their own cooldown runs out.
- **Keys**: Alt+6 toggles failure punishment, Alt+7 NPC struggle (rebindable on the Controls page). A line at the top centre reports each try.
- **Settings**: every `Struggle.ini` key under Mod settings.

## Facts (Verified: decompile of `Struggle.dll` and its description, unless marked)

- Chance = 50 + StepPerPoint x (victim's current STR + TGH - carrier's), held to [MinChance, MaxChance] (player) or, after the personality's
  points, [NpcMinChance, NpcMaxChance]. Eligible: alive, conscious, head health not below 0.
- Failure punishment: SlapDamage to the head; knock-out carrier STR x 20 / victim TGH seconds, held to [KoMinSec, KoMaxSec] and at most 30
  (a longer running knock-out is kept). A player's character waits BaseCooldownSec + FailExtraSec after a failure.
- NPC cooldown: NpcCooldownSec, halved when STR + DEX + TGH + PER is above 240, times the personality factor, at least 5 s. Personality
  (factor, points): honorable 1, 0; traitorous 0.9, +5; smart 1.25, +10; dumb 0.75, -10; brave 0.75, +10; fearful 1.5, -10; warm/kind 1.25,
  -5; cold/cruel 0.9, +5; normal 1, 0; maniacal 0.5, +15; other none.
- Ini defaults: StepPerPoint 1.0 (0.001 to 1000), MinChance 5, MaxChance 100, NpcMinChance 5, NpcMaxChance 100, BaseCooldownSec 10,
  FailExtraSec 0, PlayerOnly 1, NpcStruggle 0, NpcCooldownSec 90 (at least 5), FailPunish 1, SlapDamage 10, KoMinSec 5, KoMaxSec 30 (both at
  most 30), ToastSec 4 (1 to 60), Lang 0 (auto; 1 Chinese, 2 English). The texts are the DLL's in both languages.
- Meitou: current stats are stored x scale x the injuries' multiplier; the slap is blunt damage on the head part; the release is
  `CarrySystem.LetGo`; the knock-out `MedicalState.KnockOut`; the personality `Personality.Of` (the dialogue's tag).
- **Unknown**: when an NPC's first try comes (the port: as soon as it is carried and able); what PlayerOnly limits (the port: the menu row to
  your own characters; off, any carried character); the message panel's place and look (the port: a line at 14 % of the height). Not run in
  game.

## Harmony

- Harmony: `Meitou.Game.Ui.ContextMenuWindow.Verbs` (postfix): the carried character's own row. The game gives a body that someone outside your
  faction carries an empty menu and asks no mod for it; without the patch (a renamed method, logged) the row shows on the carrier's menu only.

## Port choices

- The toggle keys are Alt+6 and Alt+7: a key binding cannot be two number keys held together (the original's 6+7 and 7+8).
- Rolls use the mod's `Rng` purpose; cooldowns are game time and enter the state hash (not saved, as the original's).

## Missing

- Nothing beyond the differences above.
