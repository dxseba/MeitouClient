# Lammy's Director Panel

Port of *Lammy's Director Panel (RU/EN)* 1.6.6 (Lammy, Workshop 3805040918), from the original's DLL (`DirectorPanel.dll`, Ghidra decompile
and its read-only strings) and files. The original is a required Workshop item: its Russian texts and saved points are read from it at runtime.

A panel for filming and testing. Ctrl+L (a key action, rebindable on the Controls page) opens it; Esc or X closes it; the header drags it; RU/EN
switches its language (kept as the "Panel language" mod setting). It works with the first selected character, else the HUD's. Commands apply at
once, also while paused.

- **Items:** kind filter (All, Weapons, Armour, Crossbows, Limbs, Backpacks, Items), search, quantity; weapons take a manufacturer and one of its
  own grades, armour, crossbows and prosthetics one of six grades. Open another character's inventory (the HUD's, an inspected NPC, when it is
  not the first selected) in the loot window, held by the first selected; "Moving items does not count as stealing". Paint worn armour with a
  colour scheme. Give money. Heal all squads.
- **Stats:** every stat in the original's seven groups with tick boxes (tick all, per group), one value set on the ticked stats of the character
  or of everyone selected. Bounty: assign an amount for a faction, clear all. An animal's age (Young, Adult, Old).
- **NPC Spawn:** characters or animals and machines, count, distance in front of the character; into the player's active squad, or alone in a
  squad of a chosen faction, hostile (attacks) or neutral.
- **Relations:** a faction's relation to the player's faction (both ways); any two factions, one way or both.
- **Research:** learn any research directly; known ones ticked, "Hide known".
- **Behaviour:** an AI package on the character's squad, by default first separated into a squad of its own; "Restore normal behaviour" puts
  back what the squad had. Weapon stance: draw, sheathe, leave combat, keep weapon ready. Idle pose (the editor's idle stances).
- **Barbershop:** opens the character editor on the character.
- **Body:** strike a part (cutting or blunt, any damage); knock out for seconds, coma until healed, near death, heal everything; sever or restore
  each arm and leg; hunger value and presets.
- **Effects:** blood splashes on the ground round the character (six directions, seven cut types); Bloodbath mode (more blood from every
  cutting hit); custom body blood; an EFFECT record on a bone (11 bones, size, loop; "Blood only" filter).
- **Teleport:** to coordinates; saved named points (add by the character's position or by coordinates, rename, erase, teleport); Shift + left
  click on the world map moves the selection there.
- **World:** speech-bubble and pop-up text opacity; "Write data to the log"; time of day (forward only), freeze time.

## Facts (Verified: decompile and strings of `DirectorPanel.dll` 1.6.6, unless marked)

- `DirectorPanel.ini`: `Hotkey=CTRL+L` (CTRL, SHIFT, ALT, A-Z, F1-F12), `Width=650`, `Height=1051` (shrinks to the screen), `Language=RU`
  (RU or EN; the panel's toggle writes it).
- Tabs, in order: Items, Stats, NPC Spawn, Relations, Research (Technologies), Behaviour, Barbershop, Body, Effects, Teleport, World. Each panel
  text is an English string directly followed by its Russian one in `.rdata` (the port pairs them at runtime, about 400); the tab names are a
  separate Russian list (the port carries those 11 words).
- Points file `DirectorPanel_points.txt`: one `name|x|y|z` line (`%s|%.2f|%.2f|%.2f`); the shipped list names 20 towns as `Russian | English`.
- Game calls it imports (what each feature does in the original): `RootObjectFactory::createItem` and `Character::generateWeapon` (items, a
  maker's `weapon models` list for grades), `RootObjectFactory::create` (spawn), `FactionRelations::setRelation`, `Research::completeResearch` /
  `isFinished` / `checkRequirements`, `Blackboard::replaceAIPackage` / `clearAllPackages` / `addPackages` / `addFallbackPackages` (behaviour),
  `PlayerInterface::activateCharacterEditMode` (barbershop), `MedicalSystem::applyDamage` (strike), `CombatClass::addEffect` (bone effects),
  `MapScreen::getMouseWorldPosition` and `mapMousePressed` (map teleport), `Character::teleport`, `SkyX::update` (freeze time),
  `InventoryGUI::isStealing` (no-theft moves), `AppearanceBase::updateBloodyness` (body blood), `DialogueSpeechBubble::setAlpha` (text opacity),
  `ForgottenGUI::createInventoryWindow` (another's inventory).
- Strings name the bones `Bip01 Head`, `Spine2`, `Spine1`, `L/R UpperArm`, `Forearm`, `Thigh`, `Calf`; blood splatter's directions (front,
  behind, left, right, above, below) and cut types (default, pierced, upward, thrust, rightward, leftward, downward); Bloodbath uses
  `BloodSplatter01` of `53579-Newwworld.mod`.
- **Unknown**: the numbers behind the six armour grades, hunger presets, coma and near death, spawn spacing, the animal ages and Bloodbath's
  extra blood (the decompile does not link the texts to their code). The port's choices are in the code: grades 0/20/40/60/80/100, hunger
  300/30/1 HUD units, coma a 1,000,000 s knock-out, near death every part at 2 % and 120 s out, spawns 1.5 m apart, ages 0.3/1/1.3, three extra
  splashes per cutting hit.

## Port choices

- World writes are the mod's own paused commands (`DirectorSystem`); items go through the game's `DebugGiveItem`. A behaviour change and a
  squad join are the game's `ChangeAi` / `SetSquadPackages` and `MoveToSquadCommand`, applied at the next tick or paused step.
- "Join player squad" spawns as the player's own sandbox spawn and moves it into the active squad; all spawns of one record share a look.
- The original's "Restore normal behaviour" re-adds the squad's packages; the port puts back the packages the squad had before the panel's
  first change (forgotten at a load).
- Russian is the default only when the game runs in Russian.
- Another's inventory opens through the game's Open Inventory order: the first selected character walks to the target and holds the window (at
  once when beside it), where the original opens the window directly.
- Splatter: a ground mark has only a direction and size, so the cut types are approximations (pierced on both sides, thrust narrow, sideways cuts
  turned a quarter, upward smaller, downward larger); from above throws all round, from below leaves a pool.
- Body blood: Meitou's most is "as if all health is lost", so the original's 2 draws as 1, every part alike.
- An idle pose is set only on a look that already names one (the look's dictionary is not grown while the renderer may read it).

## Harmony

All applied in `Initialize`, before the JIT warm-up. Each body is wrapped: a throw is logged once and the game's own behaviour stays.

- `Meitou.Game.Host.WorldSession..ctor` (postfix, every constructor): keeps the session for its `GameClock` (World tab).
- `Meitou.Simulation.Carrying.CarrySystem.Openable` (postfix): yes for a character the panel opened (the set changes only in the system's serial
  step, from commands, and is hashed).
- `Meitou.Simulation.Crime.CrimeSystem.IsStealing(World, CharacterCold)` (postfix): no while "Moving items does not count as stealing" is on (the
  original hooks `InventoryGUI::isStealing`; set from a command, hashed).
- `Meitou.Game.GameHost.Drawn` (postfix): the custom body blood in the character's draw entry (display only).
- `Meitou.Game.Ui.SpeechBubbles.AddBubble` (prefix): the bubble's alpha times the speech opacity (display only).
- `Meitou.Game.Ui.DamageFloaters.AddFloaters` (prefix and postfix): the pop-up texts added that frame take the opacity (display only).

Internal calls bound with `AccessTools`: `Meitou.Simulation.Combat.CombatSystem.MeleeDrawn` (the drawn weapon) and
`Meitou.Simulation.Dialogue.DialogueSystem.Separate` (the dialogue's own split into a new squad), both called in the system's serial step.

## Missing

- "Draw from" (back, hip, auto) and "Which weapon": the game draws the fighter's own weapon; the side it comes from is not a setting.
- "Hold pose" with its tuning numbers and repeat: the original plays a clip over the current one through a game call whose two numbers' meaning
  its own author lists as unknown; Meitou has no layered clip a mod can start.
- **Unknown**: not checked in game.
