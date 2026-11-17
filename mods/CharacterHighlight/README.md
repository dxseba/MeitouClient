# Character Highlight (port)

Port of *Character Highlight* (workshop 3688641949). While Alt (Highlight) is held, the game's own outlines round nearby characters are recoloured:
blue yours, red hostile, green allied, yellow neutral (`FactionRelations.IsAlly` / `IsHostile` against the player's faction). Needs Settings ->
Hover outline on. A postfix on `PointerView.HighlightTargets`; no shaders. **Observed** (2026-11-17, live Windows sandbox on origin/master f7ad84a35, driven by input): with Alt held the player character is outlined blue, the hostile foe red; green allies and yellow neutrals also seen (**Observed** 2026-11-18, `--sandbox 4 --sandbox-races`, user play).
