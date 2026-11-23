# Taunt Expansion (port)

Port of *Taunt Expansion* (workshop 3771499164). A target score filter (`ITargetScoreFilter`) changes only a taunting candidate's score: the vanilla +2
Taunt term becomes +2 x *Taunt multiplier*, and x *Re-target multiplier* again while the choosing fighter already has a target (the
engine's `TargetCandidate.ChooserHasTarget`). Target choice, AI and pathing stay vanilla.

## Facts

- Settings, defaults and ranges as the original's `TauntExpansion.ini`: `[General] Enabled` true, `[Taunt] Multiplier` 5.0 (0 to 100),
  `RetargetMultiplier` 2.0 (0 to 10). A missing key reads 1.0. **Verified** (original ini; original DLL (decompiled): the clamped reads).
- The original rewrites only the taunt `addss` constant (2.0) of `AI::chooseBestAttackTarget` to 2 x Multiplier, and for a call whose old-target
  `hand` is valid (and RetargetMultiplier is not 1) sets it to 2 x Multiplier x RetargetMultiplier for that call. **Verified** (original DLL
  (decompiled): patch install and the `chooseBestAttackTarget` hook).
- The second factor applies to every taunting candidate of such a call, the current target too. **Verified** (as above: one constant per call).
- In Meitou the calls with an old target are the fighter's own choice while it fights someone, which `TargetCandidate.ChooserHasTarget`
  reports; the attacker of a protectee, the aggro look and an assault have none. **Observed** (docs/modding.md "Target choice").
- **Observed** (2026-11-18, windowed sandbox, user play, older formula): enemies favoured the taunting character.

## Missing

- The `[Logging]` diagnostics (`LogCandidates`, `LogOnlyWhenTauntPresent`, `LogOnlyChangedSelection`, `MinimumLogIntervalMs`,
  `MaximumCandidatesPerEntry`): a debug log of each choice, not ported.
