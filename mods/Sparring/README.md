# Sparring (port)

Port of *Sparring 切磋* (workshop 3776308916, an RE_Kenshi plugin; its `.mod` has no records).

- **Sparring** (`UiRegistration.AddCharacterMenuVerbs`; 切磋 when the game runs in Chinese): a row on the right-click menu of an awake, living,
  uncarried player character that is not the first selected one. The `SparOrder` checks both are the player's, awake, alive, not carried,
  carrying or caged; ends any spar either is in; turns both characters' Hold and Passive off; and gives each an `AttackOrder` on the other as the
  player's pick, so normal combat XP applies.
- **During**: melee blows between the pair do half damage (cut, blunt, pierce, stun); when either is probably dying both are healed completely
  (`MedicalState.Heal`). An `ICombatEngageFilter` refuses any fight start (`Start`) or target choice (`Choice`) between a sparring
  character and anyone but its partner, unless the player picked the target (`EngageAsk.Picked`).
- **End**: when either is down (unconscious, dead, gone) or carried; when either has been out of the fight for 1 s (after 2 s); when the
  selected one moves faster than 0.5 u/s with another target for 1.5 s (after 4 s). Both get a `StopCommand` and their Hold and Passive back. For
  5 s after, blows between the pair are cancelled (this port's). No messages, as the original.
- **Verified** (original DLL decompiled): the button caption Sparring / 切磋 (the game language from settings.cfg); the menu's conditions; damage x0.5
  (`Damages::multiply` in a `hitByMeleeAttack` hook); `healCompletely` of both on `isProbablyDying`; standing orders 13 (Passive) and 12 (Hold)
  off, 6 and 5 on, restored at the end; the end on unconscious / dead / carried, on out of combat mode after a grace, on the first moving above
  0.5 with another attack target; `isEnemy` true only within the pair and false between a sparring character and anyone else, `isAlly` the
  reverse; own-faction relation changes and the campaign notice of a player-on-player attack blocked; spars dropped on load.
- **Observed** (by reading the engine): no relation change or crime, as both are one faction. The end timings are this port's: the original
  counts character updates (60, 30, 120, 90). `isProbablyDying` is a stand-in (blood at or under 0, or a vital part's flesh + bandage under 0).
- *Blunt blows while sparring* (off) is this port's addition. A spar is not saved, as the original drops them on load.
- **Missing**: standing orders 5 and 6 (aggressive, defensive) have no reader in the engine; the per-update re-aim at the partner is the attack
  order. Not checked in game.
