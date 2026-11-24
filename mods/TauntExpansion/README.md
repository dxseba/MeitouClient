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
- `[Logging]`: the original writes one line per `AI::chooseBestAttackTarget` call whenever it is enabled (actor, result, selected, score,
  candidate count, law enforcer, combat mode, old target valid; with `LogCandidates` each candidate's name, Taunt and Selected flags, at most
  `MaximumCandidatesPerEntry`, the rest counted as truncated). It skips a call without a taunting candidate (`LogOnlyWhenTauntPresent`), one
  whose selection equals the actor's last logged one (`LogOnlyChangedSelection`), and one sooner than `MinimumLogIntervalMs` after the actor's
  last line. Clamps: interval 0 to 60000 (default 500), candidates 1 to 512 (default 32). **Verified** (original DLL (decompiled): ini reads and
  the hook's logging).
- The port writes these lines to the mod log under *Logging* in Mod settings, behind its own *Log target choices* switch (off), for the
  fighter's own choice (`TargetChoiceKind.Fight`). The selection is the first strictly best of this mod's answers (as `ChooseTarget` keeps the
  first strictly better score; a later mod's filter could still change it), the interval counts game time instead of wall time (deterministic),
  and the law enforcer and combat mode fields are left out (the ask does not carry them). **Observed** (by reading `CombatSystem.Targets.cs`);
  the lines are **Unknown** in play.

## Missing

- Nothing the port alone can add: the log's law enforcer and combat mode fields need the target score ask to carry them.
