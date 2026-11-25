# Character Edit Tweaks (port)

Port of *Character Edit Tweaks* (workshop 3807964959), from the C++ source shipped in its download (`CharacterEditTweaks_source.zip`, facts
only) and its files. The original is a required item: the port reads its `Translations.json` at start.

- **Undo / redo**: every slider and list change on the BODY, FACE and HAIR tabs (code mods' rows included) is tracked on the editor's working
  look (Harmony on `CharacterDraft.Set` and `Step`). CHANGES (above CLOTHES) shows the changes window right of the name panel: the Undo list
  (red title) and the Redo list (green), newest first, each line `[name] old >> new` (list choices counted from 1); clicking a list's top line
  undoes or redoes it. Ctrl+Z undoes, Ctrl+Shift+Z redoes while the editor is open (`AddKeyInput`; the key is taken).
- **Cancel** (in play only): CONFIRM shrinks to its left half and CANCEL takes the right half (`AddScreenOverrides` with `Bounds`,
  `AddScreenControls("editor", "ConfirmPanel", Inside)`). It puts every edited character's look back as it was when the editor opened, then
  presses CONFIRM.
- **Camera**: the zoom reaches from 23 in front of the boom's 30.15 to 24 behind it for the body and 16 behind it for the head. CAMERA (beside
  CHANGES, on by default) shows a bar right of the name panel that lifts the camera rig by up to 10 units either way (top: up). The lift
  resets when the tab or the character changes.
- **Physique sliders** (BODY tab, playable races): *Body Bulk* (-30 to 100, default 35) and, for a race whose `nm male` and `nm male strong`
  are both set and differ, *Muscularity* (0 to 100, default 50), each after an override row (Off/On). While on, the drawn body takes the
  value / 100 as its bulk (bone shape) or muscle definition (strong normal map blend) in place of the stats'. The values are look keys
  (`ModLooks`, `dxseba.characteredittweaks:bulk on`, `bulk`, `muscle on`, `muscle`), kept by CONFIRM, saves and EXPORT; RESET turns them off.

## Facts (Verified: the original's shipped source `CharacterEditTweaks.cpp` / `.h`, read; unless marked)

- Lists: at most 128 entries each (`_CHANGE_STACK_MAX_SIZE`). A change of the same control as the top undo entry within 3000 ms
  (`GetTickCount`) merges into it; one back to that entry's old value removes it; a change removes its control's redo entries. Undoing moves
  the entry to the other list. Both lists clear when the character, race or gender changes and on RESET, RANDOMISE ALL and IMPORT; RANDOMISE
  drops its tab's entries.
- Ctrl+Z / Ctrl+Shift+Z in `InputHandler::keyDownEvent` while the editor exists; the key is swallowed even with nothing to undo.
- Window: `Kenshi_WindowC`, 240 x 268 at (name panel width + 32, 4), caption Undo in (1, 0.5, 0.5), Redo label at y 96 in (0.5, 1, 0.5),
  two `Kenshi_ListBox` of 228 x 100. CHANGES / CAMERA: `Kenshi_TickButton1`, at CLOTHES' top minus its height minus 2, each half its width
  plus 2 (CAMERA 4 narrower), CAMERA selected at start.
- Cancel (mode `EDIT_MIDGAME` only): CONFIRM halved, `Kenshi_Button1` Cancel beside it; it applies every undo entry's old value, then
  `confirmButton`.
- Camera: zoom table written to body -23 / 24 and face -23 / 16 (vanilla -9 / 7 and -23 / -17: docs `EditorOrbit`). Height bar
  `Kenshi_SliderVBig` at (name panel width + 2, 10), 32 wide, panel height - 24, 161 positions starting at 80; height = (80 - position) x 0.125;
  it resets to 0 on the next category update after being moved.
- Physique: added to the BODY ranges of races with `playable`; Body Bulk min -30, max 100, mid 35; Muscularity min 0, max 100, mid 50 only
  when `nm male` and `nm male strong` are non-empty and differ. Flags int `physique sliders` (bit 0 bulk, bit 1 muscle) on the look; applied
  only to player characters: `bulkMult = Body Bulk x 0.01`, `muscleMult = Muscularity x 0.01`. RESET clears the flags.
- Texts: seven strings (Body Bulk, Muscularity, Undo, Redo, Changes, Camera, Cancel) from `Translations.json` by locale code; English otherwise.

## Harmony

- `Meitou.Data.Characters.CharacterDraft.Set(EditorControl, int)` and `Step(EditorControl, int)`: prefix and postfix (tracking).
- `Meitou.Data.Characters.CharacterDraft.Reset`, `Randomise`, `Import`, `SetRace`, `SetFemale`, and its constructor: postfixes.
- `Meitou.Game.Editor.CharacterEditorPanel.BindEditor`: postfix (keeps the panel); `EditorWheel`: prefix and postfix (zoom limits).
- `Meitou.Game.Rendering.Characters.CharacterRenderer.StageCamera`: postfix (camera height); `ApplyBuild`: prefix (physique overrides).
- Reflection (no patch): `CharacterEditorPanel.Editor`, `EditorOpen`, `Draft`, `ConfirmEditor`; `EditorSession.Current`, `Tab`, `Kind`,
  `Drafts`, `Orbit`; `EditorOrbit.Distance`, `Head`, `SetDistance`; `CharacterAsset.Appearance`.

## Port choices

- Override rows (Off/On) replace the tick boxes on the sliders; a slider that is off keeps its own value rather than showing the stats'.
- The physique override applies to any look carrying it (only the editor writes it, so in practice the player's characters).
- The lists show their five newest lines (no scrolling in a screen control); the window uses `Kenshi_FloatingPanelSkin`; the height bar
  is a plain track and thumb. Meitou's wheel steps (1.5, head 0.5 inside the vanilla head range) are kept.
- Cancel restores every character the editor edited (the original: the shown character's undo list) and keeps a typed name.
- **Unknown**: whether Cancel at a plastic surgeon charges in the original (it confirms, as here).

## Missing

- Nothing. **Unknown**: not checked in game.
