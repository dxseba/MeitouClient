# It. Tickles. Me (port)

Port of the RE_Kenshi stumble-spam fix (workshop 3731488940). A melee hit whose stagger damage (cut + blunt + pierce + extra stun) is below a share
(default 0.33) of the victim's stumble threshold no longer starts a light stagger; heavy staggers are unchanged. Settings: on/off and the share.

Harmony: a transpiler on `CombatSystem.BlowReaction` (the single melee `damage > StumbleThreshold` comparison) and a prefix on `CombatSystem.Stagger`.
**Observed** (2026-11-17): the transpiler applies and the game runs a sandbox fight; the game runs a sandbox fight. **Observed** (2026-11-18, windowed sandbox on origin/master f7ad84a35, user play): weak hits no longer stagger; with the filter off they do.
