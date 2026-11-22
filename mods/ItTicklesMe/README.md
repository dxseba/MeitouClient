# It. Tickles. Me (port)

Port of the RE_Kenshi stumble-spam fix (workshop 3731488940). A melee hit whose stagger damage (cut + blunt + pierce + extra stun) is below a share
(default 0.33) of the victim's stumble threshold no longer starts a light stagger; heavy staggers are unchanged. Settings: on/off and the share.
The share's default is read, as the original reads it, from a LOCATIONAL_DAMAGE record named `It Tickles Me - Light Stagger Ratio` (its
`affects move speed`, clamped 0..1) when the original's data mod is loaded; a value set on the settings page wins over it.

Harmony: a transpiler on `CombatSystem.BlowReaction` (the single melee `damage > StumbleThreshold` comparison) and a prefix on `CombatSystem.Stagger`.

- **Verified** against the original DLL (decompiled): it hooks `Character::_startStumble` and skips it when the hit has a source and an attacker,
  the threshold (`calculateStumbleThresholdDamageAmount`) is above 0, the summed damage (four `Damages` components, each floored at 0) is below the
  threshold and below ratio x threshold; the record name, field and 0..1 clamp, default 0.33. Bolts stagger only heavy in Meitou, so the melee
  path is the only light one.
- **Observed** (2026-11-17): the transpiler applies and the game runs a sandbox fight. **Observed** (2026-11-18, windowed sandbox on origin/master
  f7ad84a35, user play): weak hits no longer stagger; with the filter off they do.
