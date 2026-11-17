# Kenshi Compact (port)

Port of farghul's *Kenshi Compact* (workshop 3763089712, GPL-3.0 source github.com/ekiuts/kenshi). Shift+/ opens the character editor in its
in-play mode (`EditKind.Recruit`, `CharacterEditorPanel.OpenEditor`) on the main selected character (`PlayerInterface.Lead`), not while it is in
combat mode. A Harmony prefix on `GameHotkeys.Key`; the key is swallowed only with Shift. Off in Mod settings.
