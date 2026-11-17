# Sparring (port)

Port of *Sparring 切磋* (workshop 3776308916, an RE_Kenshi plugin; no source published, behaviour from its description).

- **Spar** (`UiRegistration.AddCharacterMenuVerbs`): a row on the right-click menu of a player character, awake, alive and not carried, that
  is not the first selected one. It queues a `SparOrder`; the system checks both are the player's, of one faction, able to fight, then gives
  each an `AttackOrder` on the other as the player's pick (`Picked`), so normal combat XP applies.
- **End**: once either is unconscious, dead or gone, or neither has fought the other for 8 s (a move order, say), both get a `StopCommand`
  and the log says who won. For 5 s after, blows between the pair are cancelled.
- **No deaths** (`ICombatHitFilter`): a blow between the pair on a downed partner, or after the end, is cancelled; with *Blunt blows while
  sparring* (default on) a sparring blow's cut damage is added to blunt, against lethal bleeding. Pierce is left as it is.
- **One on one**: the engine's join finders (`CombatSystem.BestAttackerOf`, bodyguards in `RetaliationSystem.Guards`) already skip a foe of
  the joiner's own faction (Observed, by reading). A Harmony prefix on the private `CombatSystem.Engage` also refuses any fight on a sparring
  character by one of its own faction other than its partner, unless the player picked it.
- **Relations and crime**: none expected; both are one faction and the squad memory sets no TEMP_ENEMY on one's own faction (Observed, by
  reading `DialogueSystem.FlagFrom`). Unknown in play.
- Differences: the original's exact end rule and messages are unknown; the cut-to-blunt switch is this port's. A spar is not saved: after a
  load the two fight on as a plain pick with no protection.
- **Verified**: builds with 0 warnings. **Unknown**: all play behaviour (the pair fighting a same-faction target to the end, nobody joining,
  the end and the stop, no deaths, no relation change) is untested in game.
