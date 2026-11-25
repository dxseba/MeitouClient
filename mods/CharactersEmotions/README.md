# Characters Emotions (port)

Port of *Characters Emotions - Realtime expression* (Workshop 3791017374, MIT), from its DLL (Ghidra decompile), its ini files and readmes. The
original is a required item: its `Plugins/CharactersEmotions_emotions.ini`, `CharactersEmotions_animemotions.ini` and `localization/*.txt` are
read from it at start; nothing of it is shipped.

- **Live expressions** on the player's characters whose body mesh has the `CE_*` shape keys (face-morph races: Vanilla Race for CE, VRoid World,
  VRoid Face+): blinking, a random normal expression, combat (Angry), hunger (Sad), injury (Pain), seated (Relax), bed (Sleep), a hit (Attacked)
  and knocked out, by the original's priority KnockOut > Attacked > Sleep > Angry > Pain > Sad > Relax > Normal.
- **Animation emotions**: a section of the animemotions ini whose names hold a clip playing on the character follows the clip (its fraction times
  `TotalAnimFrame`, control points interpolated), with `EmotionPriority`, `IgnoreBlink` and `OnlyAnimEmotion`.
- **Speech morphing**: while the character's speech bubble shows, random `CE_SpeechA/I/U/E/O` keys crossfade.
- **Ban key** (key action *Toggle Selected Character Ban Key*, default P): the selected characters stop or resume showing expressions.
- **Settings** (Mod settings): every value of the original's `CharactersEmotions_settings.ini` except the menu key, with its defaults and
  English labels, translated from the original's localization files (`AddTranslations(language, pairs)`).

## Facts

- **Verified** (shipped readmes and ini files): the eleven default keys, the 0..100 amounts over 100, `InvalidShapeKey` set to 0 before an
  expression, the priority order, the animation emotions' rules (frame lists ascending, unequal lists skipped, outside the first/last point no
  contribution, frame 0 a reference only), the five speech keys, the settings and their defaults.
- **Verified** (decompile, the state test and its callers): the situation is tested in this order: unconscious and not dead (KnockOut); clip
  `sleepinbed` or `sleeponfloor`, or a current task 98, 258, 175 or 261 (Sleep); in combat (the portrait's combat flag, else
  `isInCombatMode`) (Angry); any body part with (health - a second value) / max at or under `HurtBoundaryValue` (Pain); `MedicalSystem` +0x60 at
  or under 2.0 (Sad); clip `sitting chair` or `sitting idle`, or task 275 (Relax); else Normal. Attacked comes from a `MedicalSystem::addWound`
  hook: full for `EmotionAttacked_KeepTime`, then fading over `_OffTime`. Intervals are `frequency + random(-spread..spread)` clamped to 0..3600000
  ms, on `GetTickCount` (real time). Speech hooks `Dialogue::say`; after leaving the character editor it waits 5000 ms.
- Port mapping (**Observed**, by reading Meitou): Sleep is `CharacterSnapshot.Asleep`, `InBed` or the two clips (the task numbers are not
  mapped); Angry is `InCombat`; Pain is `Body.WorstPart`; Sad is `Body.Hunger <= 2` (Meitou's hunger runs 3 fed to 0 starving, so +0x60 is
  taken as hunger, **Unknown** in the original); Relax is the two clips; a hit is any landed hit of the combat hit feed.
- **Unknown**: the original's exact blink and normal-expression curves (the port blinks as a triangle over `Blinking_Time`, morphs the normal
  expression in and out over `_MorphTime` around `_KeepTime`), how long its mouth moves after `Dialogue::say` (the port: while the bubble shows),
  and whether the situational expressions crossfade (the port switches them at once).
- Harmony: `Meitou.Game.Rendering.Characters.CharacterRenderer.Update(System.Numerics.Vector3)` (prefix and postfix). It reads `_live`,
  `_liveCount`, `_content.Morphs`, `CharacterAsset.MorphBase/Morph/Blending/IsIcon`, `MorphSource`, `MeshMorphs.SlotCount/Poses/Deltas` and calls
  `MorphArena.Reserve/Write` by reflection. The postfix sums the look's own weights with the live keys into a per-asset block of three regions in
  the morph arena and points the asset's `MorphBase` at it for the frame; the prefix restores the asset's own base first. A failure logs once and
  the faces are drawn as without the mod.
- `AddLookExtras` gives every look a weight of 0.0001 on the blink keys, so a look is built with a morph the live weights can replace.

## Port choices

- Characters: the player's people in snapshot order (the original: the squad in front order), or the selected ones; the ban list lasts the
  session.
- The original's F6 window is Meitou's Settings page; the ini files of expressions are read, not edited, in game.

## Missing

- Nothing needs an engine change. **Unknown**: not checked in game.
