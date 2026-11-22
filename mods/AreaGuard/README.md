# Area Guard (port)

Port of *Area Guard* 1.0 RC1 (workshop 3769985477), an RE_Kenshi plugin hooking `CombatClass::_blockHit` and
`Character::hitByMeleeAttack`. Reimplemented from its behaviour, not translated.

- **Rule**: once a character blocks a melee blow, the same blow (same attacker, swing sequence and blow index, same tick) does not land on
  the bodies its attack zone reaches after it. The system hears blocks as an `ICombatDefenceObserver` (kind `Blocked`; dodges protect no
  one) and spares later hits as an `ICombatHitFilter`. No Harmony patches.
- **Settings** (Mod settings; the original's `AreaGuard.ini`, same names and defaults): *Enable* (on); *Protection mode* All (default) /
  Friendly / NonEnemy; *Protected reaction* NoReaction (default: `PendingHit.Cancelled`, a miss) / HitReaction (cut, blunt, pierce and stun
  set to 0, so the hit reaction plays); *Factor in disguises* (on); *Debug logging* (off: a log line per spared blow).
- Friendly asks the blocker whether it regards the victim as friendly (`DialogueSystem.RegardsFriendly`, the game's slot 126 with squad
  memory, love, allied factions, the victim's disguise when set); NonEnemy asks whether it does not regard it as an enemy (squad memory
  TEMP_ENEMY / TEMP_ALLY, else a hostile faction, disguise counted).
- **Verified** (original DLL, decompiled): ini keys, defaults and the 1/2/3 and 1/2 numbering; Friendly and NonEnemy call the blocker's
  `Character` vfuncs 0x3f0 (`isAlly`) and 0x3e8 (`isEnemy`) on the victim with `FactorInDisguises` as the second argument; a hit is
  protected only when the newest block by another character (a 128-entry ring, entries older than 750 ms ignored) has the same attacker,
  the same attack technique and the same cut direction, and was recorded in the same `GetTickCount64` millisecond; NoReaction skips the
  native hit call (returns hit material 0), HitReaction calls it with `Damages::multiply(0)` and restores the damages afterwards; a block
  that happens inside a hit call takes that hit's attacker and technique (the "active hit context", 50 ms).
- **Differences** (engine choices): "same blow" is the swing sequence and blow index within one tick rather than technique, direction and
  millisecond; the commit applies a blow's bodies sorted by target slot, so "later" means a higher slot (**Observed**, reading
  `CombatSystem.ApplyBlow`); bodies applied before the block still take the blow, as in the original. Bolts and executions are untouched (as
  the original). The ini's legacy text values and "restart to apply" do not apply: settings change live.
- **Unknown**: play behaviour (sweeps spared, both reaction modes, Friendly/NonEnemy) is untested in game.
