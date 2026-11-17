# Taunt Expansion (port)

Port of *Taunt Expansion* (workshop 3771499164). A postfix on `TargetScore.Score` changes only a taunting candidate's score: the vanilla +2 taunt
term times *Taunt strength* (default 1.5), and, for an enemy whose current target is someone else, a share (*Re-targeting*, default 0.5) of its
stubbornness term. Target choice, AI and pathing stay vanilla. **Observed** (2026-11-17): loads; loads. **Observed** (2026-11-18, windowed sandbox on origin/master f7ad84a35, user play): enemies favoured the taunting character.
