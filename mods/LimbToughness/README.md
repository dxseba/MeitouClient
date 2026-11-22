# Limb Toughness (port)

Port of *LimbToughness - Max HP to Limb* (workshop 3787222853).

Facts, **Verified** from the original DLL (decompiled; constants read from its data) and its shipped `LimbToughness.ini`:
- Factor = 1 + `HealthPercentPerToughness` (0.5; invalid or negative: 0.5) / 100 x `CharStats::toughness()`; 1 when either is 0 or less.
- Each part's stored maximum becomes floor(baseline x factor + 0.5), the baseline being the part's maximum first seen; its health becomes
  floor(health x new / old + 0.0001) (negative health untouched). A part within 1 of its baseline maximum while the stored maximum is above
  it counts as full first. Parts: head, chest, both arms, both legs and every other part of the body.
- Exclusions: a robotic part (`HealthPartStatus::isRobotic`) unless `ApplyToSkeletonsAndRobotLimbs` (0); a robot race unless
  `ApplyToRobotRaces` (1); an animal unless `ApplyToAnimals` (1). Hooks `HealthPartStatus::setup`, `MedicalSystem::init`, `updateStats`, `load`,
  `CharStats::xpStat_eventBased`/`xpStat_timeBased`, and the `maxHealth` getter (`ApplicationMode` 0 only; default 4 = every stored path).

Port: every 60th tick, the bonus is set on each living character's `HealthPart.HpMultiplier` (maximum = base x multiplier x scale - wear),
toughness `CharacterStats.ScaledToughness` (the stat with modifiers x the stats multiplier). The baseline multiplier is the one first seen, an
animal's its size now (its growth resets the multiplier). Settings: the ini's keys on the Settings screen. Not saved: a load starts from the
plain maximum; health above it (saved with the bonus) is kept, not scaled again.

Missing:
- `ApplicationMode` (only the stored path exists here), and the timing of the original's hooks (the port re-applies every 2 s).
- A loaded part saved injured with the bonus, below the plain maximum, is scaled up once more (the multiplier is not saved).
- **Unknown**: not checked in game; whether `CharStats::toughness()` includes modifiers; how the health panel shows the raised maximum.
