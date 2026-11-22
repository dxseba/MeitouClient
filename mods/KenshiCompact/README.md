# Kenshi Compact (port)

Port of farghul's *Kenshi Compact* v2.0.3 (workshop 3763089712, GPL-3.0 source github.com/farghul/kenshi), a KenshiLua script.

Facts, **Verified** from its `scripts/init/editor.lua`: an `onKeyDown` handler; OIS key 53 (`/`) with the input handler's Shift held calls
`activateCharacterEditMode` on `PlayerInterface.selectedCharacter`, unless that character `isInCombatMode(true, true)` (melee or ranged).
The handler only listens (the key is not consumed). The hotkey is changed by editing `KC_SLASH`. The older C++ plugin used Alt+V.

Port: a Harmony prefix on `GameHotkeys.Key`; Shift+/ opens the character editor in its in-play mode (`EditKind.Recruit`,
`CharacterEditorPanel.OpenEditor`) on the panel's character (`PlayerInterface.Lead`), not in combat mode (`CharacterSnapshot.CombatMode`,
`isInCombatMode(1, 1)`). The key goes on to the game too. Off in Mod settings.

Missing: changing the hotkey (the script's one line); **Unknown** whether `selectedCharacter` is the inspected character (`Lead`) or only the
first selected one, and not checked in game.
