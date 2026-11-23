# Mentors (port)

Port of *KENSHI: MENTORS* (workshop 3834394158, RE_Kenshi plugin `Mentors.dll` 0.5). Lesson rules from the original DLL (decompiled) and its
`Mentors.ini`; no source is published. Trades are the port's own.

- **Opening** (**Verified**, DLL strings and `Mentors.ini`): the player reply "Could you teach me something? [training]" in a conversation with
  a character who can teach the player's character (`AddDialogueReplies`) opens training; with *Close dialogue* (`CloseDialogue`, on) the
  conversation ends. The *Mentors window* key (Ctrl+Shift+T by default, a chord, so the game's T stays Toggle research) opens it on the inspected
  NPC (the HUD's shown character) when it can teach, else on the nearest teacher within *Hotkey radius* (`Radius`, 500), or closes it.
- **Who can teach** (**Verified**): a player character learns from a living non-player character that is not a slave (state 1) and not hostile;
  port also: conscious, not imprisoned or carried (an unconscious check in the original is **Observed** only, an unnamed virtual call).
- **Trades**: the original maps the teacher's English CHARACTER record name to one of 21 trades with a curated table (whole names, then an
  ordered word list; **Verified** shape, DLL rdata). That table is its author's content and is not shipped. Port default: its own heuristic
  (`Professions.cs`): trader words first (with extra skills by the goods named), then field words (ninja, thief, smith, doctor, guard, farmer,
  miner, wanderer...), then a shop (trader) or the law-enforcement NPC class (fighter); others teach nothing. `RequireProfession=0`: everyone
  teaches all 33 skills.
- **trades.json** (optional, in this mod's folder) replaces the heuristic: `trades` (name -> `covers` text and `skills`, `StatsEnumerated`
  names), `exact` (a whole lower-case record name -> trade, checked first), `words` (ordered `[word, trade]`; the first word the lower-case name
  contains wins). Example (made up):
  `{ "trades": { "brewer": { "covers": "cooking and labouring", "skills": ["Cooking", "Labouring"] } }, "exact": { "old tom": "brewer" },
  "words": [["brew", "brewer"]] }`.
- **Lessons** (**Verified**): a skill is taught when the teacher has `MinTeacherLevel` (20) and the student's level + 1 is at most
  min(teacher - `TeacherMargin` (5), `TrainingCap` (60)). A lesson is +1 level costing `BasePrice` (60) x (1 + floor(level)/10)^2 x
  (0.75 + teacher/100), x `AttributePriceMult` (1.5) for strength, dexterity, toughness and perception, rounded up to 10 cats; the stat is raised
  by the lessons, at most 100. Rows: the game's 33 skills, attributes first, stats window names; buttons "+N  P cats" for 1 and 5 lessons
  (N less near the limit).
- **Texts** (**Verified**, DLL strings): "Student: name    Money: N cats", "level -> limit  (teacher T)", "Nothing left to teach.", "The teacher
  cannot teach this any further.", "Not enough money: it costs N cats.", "Skill: a -> b  (-P cats)", "The teacher or the student is not
  available now."
- **Unknown**: whether the original reads the effective or the stored stat (`CharStats::getStat(stat, true)`); port: the student's stored, the
  teacher's with modifiers. Not run in game since these changes. **Observed** (2026-11-19, headless sandbox): an earlier build ran 3000 ticks.

## Missing

- The original's exact trade per NPC (its authored table; supply a `trades.json` to change the port's).
- `OnlyPlayerStartedTalk`: the reply shows in every conversation with a teacher; the dialogue context does not say who started the talk.
- Russian texts.
