# Kill Counter (port)

Port of Rokeshi's RE_Kenshi *Kill Counter* (workshop 3779464387). The selected player character's kills, knockouts and limbs removed show under the toolbar.

- A hit by a player character that leaves an alive, conscious victim dead counts as a kill; unconscious counts as a knockout (`ICombatHitObserver`).
- Saved in the mod's sidecar; tallies are keyed by character name (two namesakes share one).
- Limbs removed: a hit's victim's stumps (`LimbState.Stump`) against the count refreshed every tick; the rise goes to the hit's attacker.
  Saved (sidecar version 2; version 1 loads with 0 limbs). **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play): works.
- The victim's state is read in the slow phase within 30 ticks of the hit, as on master the knockout lands after the hit callback. **Observed** (2026-11-17, live Windows sandbox on origin/master f7ad84a35, driven by input): Knockouts went 0 to 1 when Mu downed Stone. A downed victim who later dies (further blows or bleeding out) is a kill for its last attacker; before this, killing a downed Stone left Kills at 0. **Observed** after the fix (2026-11-17, same sandbox, user play): killing a downed Stone took Kills 0 to 1.
