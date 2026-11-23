# Kill Counter (port)

Port of Rokeshi's RE_Kenshi *Kill Counter* (workshop 3779464387). Kills, knockouts and limbs removed per character, shown as "Kills", "KO's"
and "Limbs Removed" lines in the character stats window's attributes panel after the four attributes (`AddScreenControls("stats",
"AttributesPanel")`), in the original's colour, for the window's character.

Rules, **Verified** from the original's shipped source (`KillCounter.cpp`, matching the DLL's strings):
- Any attacker is counted (not only the player's); a hit on oneself is not. Hooks: `Character::hitByMeleeAttack`, `Character::iShotYou`
  (bolts and turret harpoons), `Character::getStealthKOChance` + `MedicalSystem::knockout` (stealth knock-outs), the game loop, and
  `Character::serialise`/`loadFromSerialise`.
- Kill: the victim's last attacker when it dies, however long after (bleeding out counts), while it stays loaded; an unloaded victim is dropped.
- Knockout: a hit that takes the victim from standing to unconscious, credited to that hit's attacker; a successful stealth knock-out credits
  the sneaker and makes it the last attacker. If the same character downed and then killed the victim while it was still down, that knockout
  is taken back (the kill replaces it).
- Limbs: arms and legs newly cut off or crushed by the hit.
- The original saves the three numbers as floats `kills`, `kos`, `limbs` in each character's STATS record of the save, and shows them in the
  character stats window's attributes panel (text colour RGB 100,95,90).

Port specifics:
- Hits come from `ICombatHitObserver`, deaths from `ICharacterDeathObserver`. The knockout is read in the slow phase within 30 ticks of the
  hit, as the medical step that downs the victim can run after the hit callback. A stealth knock-out is seen as a sneaker in its
  `StealthKo` task whose target goes down. **Observed** (2026-11-17, live sandbox, earlier version): a melee knockout and a kill of a
  downed victim counted.
- Saved in the mod's sidecar (version 3: by lasting id). Versions 1 and 2 (by name) go to the player's characters of that name at load.
  Tallies of characters the game no longer keeps are dropped after a load and every ten game minutes.

Missing:
- **Unknown**: the lines in a running stats window (font and size are the attribute rows' painted font at the UI scale, a guess).
- Not in the vanilla save's STATS record (`kills`/`kos`/`limbs`): a save moved to the original game loses the counts.
