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

- `ApplicationMode` (**Verified**, DLL): 1 to 4 apply on part setup (1, 4), medical init (2, 4) and updateStats (3, 4); every mode above 0 also
  on `MedicalSystem::load` and on Toughness experience (stat 21); 0 only raises what the `maxHealth` getter returns.

Port: the bonus is set on each living character's `HealthPart.HpMultiplier` (maximum = base x multiplier x scale - wear), toughness
`CharacterStats.ScaledToughness` (the stat with modifiers x the stats multiplier), the tick a character is first seen (made, or loaded) and the tick
its Toughness changes, and every 60th tick for the rest (an animal's growth). Modes 1 to 4 are alike here (the engine has no separate setup, init
and stats moments); mode 0 acts as 4 with a log line. The baseline multiplier is the one first seen, an animal's its size now (its growth resets
the multiplier). Saved (`IModSaveData`, v1): each raised character's part ratios (multiplier / baseline) by lasting id, so a load rescales its
saved health from the maximum it was saved with; ratios of NPCs not back yet are written again, dropped once their character is gone for good.
A save from before (no ratios): health above the plain maximum is kept, not scaled again. Settings: the ini's keys on the Settings screen.

Missing:
- `ApplicationMode` 0 (getter-only): needs a max-health read filter (engine gap).
- **Unknown**: not checked in game (the save and the per-tick change test included); whether `CharStats::toughness()` includes modifiers; how the health panel shows the raised maximum.
