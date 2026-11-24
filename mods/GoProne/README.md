# Go Prone

Port of *Go Prone* (Genpretz, Nexus 1830), from the original's DLL (`Go Prone.dll`, Ghidra decompile) and files.

- **Toggle prone** (`AddKeyAction`, unbound by default): the selected player characters lie down in the staying-low posture
  (`MedicalState.StayingLow`, [ragdolls](../../docs/game/ragdolls.md) "Staying low": stealth x2, sight x0.5, crawl speed 7, grounded clips)
  and start sneaking; pressed while any of them is staying low, they stand (posture 0, the engine's get-up as for a speed order) and keep
  sneaking. Characters crawling with a leg down, playing dead or out cold are left as they are. A chord such as Ctrl+Z can be bound on the
  Controls page. The engine ends the posture as it ends staying low (a speed order, sneak off, a leg down, a melee attack, a caught thief's
  watcher); it is not saved, as in the game.

## Facts (Verified: decompile of `Go Prone.dll`, `startPlugin` and its hooks, unless marked)

- Adds the input command `toggle_prone` with no key or modifiers (`InputHandler::loadConfig` hook), so it is unbound until the player binds it.
- Its Controls line ("Toggle prone"; looked up as a translatable message, **Observed**, no catalog shipped) is inserted right after the "Toggle stealth mode" (`toggle_sneak`)
  line (`DatapanelGUI::addCustomLine` hook).
- On the command (`PlayerInterface::playerControl` hook): down = not `PlayerInterface::selectedCharactersLayingLow()`. For each selected
  character whose posture (vfunc `0x368`) is not 2, 3 or 4: down sets stealth on, then posture 1; up sets posture 0, then stealth on. Then the
  HUD's sneak button shows the first selected character's stealth mode. No sneak, combat, furniture or carried test.
- No lying-down clip of its own: the posture setter changes the clips; posture 1 to 0 queues the game's get-up task 65
  ([ragdolls](../../docs/game/ragdolls.md)). The posture is not saved by the game.
- The `.mod`'s description says double-tapping toggle sneak goes prone; the DLL has no such logic (`toggle_sneak` is only used to place the
  Controls line). **Observed**: the description is older than the DLL.
- **Unknown**: whether `selectedCharactersLayingLow` means any or all of the selection (the port uses any).

## Port choices

- A carried character and one in a bed, cage or chains is skipped (the original does not test it).

## Missing

- The Controls row comes after the game's rows (Meitou places mod rows there), not right after "Toggle stealth mode". Engine gap.
- **Unknown**: not checked in game.
