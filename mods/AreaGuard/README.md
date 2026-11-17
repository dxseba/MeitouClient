# Area Guard (port)

Port of *Area Guard* 1.0 RC1 (workshop 3769985477; behaviour from its GPL-3.0 source, an RE_Kenshi plugin hooking `CombatClass::_blockHit`
and `Character::hitByMeleeAttack`). Reimplemented, not translated.

- **Rule**: once a character blocks a melee blow, the same blow (same attacker, swing sequence and blow index, same tick) does not land on
  the bodies its attack zone reaches after it. The original matches attacker, technique, cut direction and the same millisecond.
- **Hooks**: a Harmony prefix on the private `CombatSystem.ApplyBlow` notes the blow's swing sequence (`Effect.Amount`); a postfix on the
  private `CombatSystem.ApplyBlocked` records the block in that world's system; the system's `ICombatHitFilter.HitLanding` spares a later
  melee hit of the same blow on someone other than the blocker.
- **Settings** (Mod settings; the original's ini): *Enable*; *Protection mode* All (default) / Friendly (the blocker's faction counts the
  victim's as an ally, `FactionRelations.IsAlly`) / NonEnemy (not `AggroSystem.IsEnemy`); *Protected reaction* NoReaction (default:
  `PendingHit.Cancelled`, a miss) / HitReaction (the packet's cut, blunt, pierce and stun set to 0, so the hit reaction may play);
  *Factor in disguises* (default on: the victim's faction is `ArrestSystem.SeenFaction`). The original's DebugLogging is left out.
- **Order** (Observed, by reading the engine): the commit applies a blow's bodies sorted by target slot, not along the sweep, so "later" means
  a higher slot; bodies applied before the block still take the blow, as the original's own limitation for its processing order.
- Dodges protect no one; bolts and executions are not touched (as the original).
- **Verified**: builds with 0 warnings. **Unknown**: play behaviour (patches applying, sweeps spared, both reaction modes, relations modes) is
  untested in game.
