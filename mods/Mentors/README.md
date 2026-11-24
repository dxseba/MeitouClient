# Mentors (port)

Port of *KENSHI: MENTORS* (workshop 3834394158, RE_Kenshi plugin `Mentors.dll` 0.5). Lesson rules from the original DLL (decompiled) and its
`Mentors.ini`; no source is published. Its trade table is read from the installed original at load time, never shipped.

- **Opening** (**Verified**, DLL strings and `Mentors.ini`): the player reply "Could you teach me something? [training]" in a conversation with
  a character who can teach the player's character (`AddDialogueReplies`) opens training; with *Close dialogue* (`CloseDialogue`, on) the
  conversation ends. The *Mentors window* key (Ctrl+Shift+T by default, a chord, so the game's T stays Toggle research) opens it on the inspected
  NPC (the HUD's shown character) when it can teach, else on the nearest teacher within *Hotkey radius* (`Radius`, 500), or closes it.
  Without a teacher the window opens with the original's note (**Verified**, DLL code and strings): "Select your character and press
  Ctrl+Shift+T next to an NPC." (nobody selected); with *Require profession*, about the inspected non-player NPC: "<name> is no teacher.
  Teachers need a fitting trade: ..." (no trade) or "<name> (<trade>) cannot teach you any more of their trade."; else "Nobody nearby can
  teach you anything. Select an NPC or come closer."
- **Trace dialogue** (`TraceDialogue`, off): "[trace] " lines in the mod log for the reply offered, added and clicked and the hotkey's choice
  (port: its own steps; the original traces the game's dialogue calls it hooks).
- **characters.txt** (**Observed**, shipped file and DLL strings): the original maps CHARACTER string ids to English names (647 lines) so a
  translated game still finds trades; the port reads the untranslated record name (`BaseName`), the same unless a mod renames a record.
- **Who can teach** (**Verified**): a player character learns from a living non-player character that is not a slave (state 1) and not hostile;
  port also: conscious, not imprisoned or carried (an unconscious check in the original is **Observed** only, an unnamed virtual call).
- **Trades**: the original maps the teacher's lower-cased English CHARACTER record name to one of 21 trades: 6 whole names compared first, then
  101 words in order, the first the name contains wins (**Verified**, DLL code and rdata). Source, first found: this folder's `trades.json`;
  the table in the installed original's `Mentors.dll` (Workshop folder `3834394158` beside this one, else the code-less mod "Mentors");
  else the port's heuristic (`Professions.cs`: trader words with goods skills, field words, shop, law-enforcement class; others teach
  nothing). Each fallback logs one line. `RequireProfession=0`: everyone teaches all 33 skills.
- **DLL layout** (**Verified**, Ghidra and a byte dump of 0.5's `.rdata`): a match list of 16-byte entries (pointer to an ASCII lower-case
  text, u32 trade index, u32 0), the whole names first; the code's lookup `lea`s point at its start and at the first word (the counts are
  code constants). A trade array of 0x60-byte records: pointers to the Russian name, English name, Russian and English covers texts (UTF-16),
  then 16 u32 stat numbers (`StatsEnumerated`) ending in 0; index 0 is an empty sentinel. Reader (`OriginalTrades.cs`, `PeImage.cs`): PE
  sections and VA -> file offset; the unique longest run of valid match entries in `.rdata`, split at the one `lea` target inside it; the
  unique code-referenced sentinel followed by exactly max-index valid trades (printable texts, teachable stats). Any mismatch: not used.
  **Observed** (2026-11-24, scratch program on the installed 0.5 DLL): 6 names, 101 words, 21 trades; byte-flipped copies rejected.
- **trades.json** (optional, in this mod's folder) replaces both: `trades` (name -> `covers` text and `skills`, `StatsEnumerated`
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

- **Only player-started talk** (`OnlyPlayerStartedTalk`, on, **Verified** `Mentors.ini`): the reply shows only in a conversation the player
  started (`UiDialogueContext.Conversation.PlayerStarted`, false for an NPC's approach).
- **Russian** (**Verified**, DLL code): the original shows Russian when the game's locale id starts with "ru". Its texts are pairs, no
  catalogue: each window or notice text is one call of a picker taking the Russian (rdx) and English (r8) UTF-16 strings; each trade record
  holds its Russian name and covers text at +0 / +0x10; a 33-entry skill label table (32 bytes: u32 stat, u32 0, Russian and English text
  pointers, u32 attribute flag, u32 0) sits just before the trade array. In a Russian game the port reads them from the installed original
  (`OriginalTexts.cs`: the calls whose rdx and r8 are set by `lea [rip+disp32]` since the call before, under the one picker that takes the
  training reply; the tables through `OriginalTrades.RussianPairs`, labels by stat), writes them as a gettext catalogue in the temporary folder
  and adds it (`ModContext.AddTranslations`); the window, notices and reply go through it (`MentorsMod.Tr`). **Verified** by a scratch run
  (not in the repo) on the installed 0.5 DLL: 18 picker pairs and 75 table pairs, all Russian. Settings stay English (the original has none).

## Missing

- The original's trades and Russian texts when it is not installed (the port's heuristic and English are used): they are its author's, not
  shipped.
