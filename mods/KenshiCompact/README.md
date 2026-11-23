# Kenshi Compact (port)

Port of farghul's *Kenshi Compact* v2.0.3 (workshop 3763089712, GPL-3.0 source github.com/farghul/kenshi), a KenshiLua script.

Facts, **Verified** from its `scripts/init/editor.lua`: an `onKeyDown` handler; OIS key 53 (`/`) with the input handler's Shift held calls
`activateCharacterEditMode` on `PlayerInterface.selectedCharacter`, unless that character `isInCombatMode(true, true)` (melee or ranged).
The handler only listens (the key is not consumed). The hotkey is changed by editing `KC_SLASH`. The older C++ plugin used Alt+V.

Port: a key action (`AddKeyAction`, default `/`, rebindable on the Controls page as "Character editor (with Shift)") that
acts only with Shift held; it opens the character editor in its in-play mode (`ModInterface.OpenScreen(ModScreen.CharacterEditor)`, a
recruit's edit) on the HUD's character (`ModFrame.Shown`: the inspected character, else the first selected), not in combat mode
(`CharacterSnapshot.CombatMode`, `isInCombatMode(1, 1)`). `/` is no game action's default, so nothing else loses the key. Off in Mod settings.

Missing: **Unknown** whether `selectedCharacter` is the inspected character (`Shown`) or only the first selected one, and not checked in game.
