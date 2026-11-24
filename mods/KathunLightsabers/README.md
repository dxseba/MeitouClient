# Glowing Lightsabers for Project Kathun (port)

Port of *GLOWING LIGHTSABERS for Project Kathun* (workshop 3833105321), from its plugin (`KathunSaberGlow.dll`, Ghidra decompile), its
settings file and its data. The original is a required item: its `.mod` (the six LIGHT records) and `fx/` halo systems load as data when it is
enabled, and the port reads its `KathunSaberGlow.cfg` at start (a copy in this folder wins). Needs Project Kathun (or The Lightsaber / Vented
Lightsabers) for the sabers.

- **Who**: the player's living characters, then every other living character within `range` (1500) of one, in snapshot order; only while the
  weapon is drawn (`CharacterSnapshot.WeaponDrawn`, the held `bare sword` part).
- **Which weapons**: a held model named on a `mesh=` line, or one whose materials or their textures hold a `texture=` keyword (the mesh's
  submesh materials, looked up in the `.material` scripts of the model's mod folder). Colour: the mesh line's, else the texture line's.
- **Blade glow** (`Glow(GlowTarget.Weapon)`): the colour's `glow r g b` at strength `glow_brightness` (0.6), on every matched saber.
- **Halo** (`KeepEffect(KSG_halo_<colour>, EffectAnchor.Weapon(id, offset).Scaled(1))`): per blade range n = round(length / `halo_step`), at least
  1, and n + 1 glows from end to end, at the system's authored size. Mesh y is mapped onto Meitou's weapon frame by the model's bounds (y extent;
  the longer side is the tip side, as the engine's blade frame takes it).
- **Light** (Harmony): the colour's LIGHT record (point, radius 45, brightness 1.2, the colour's diffuse) at the middle of the first blade
  range, moved each frame with the drawn blade; night-only (`WorldLightRule.Night`), as the game's carried lights.
- At most `max_lights` (12) sabers get the halo and light; `light=0` / `halo=0` turn them off.

## Facts (Verified: decompile of `KathunSaberGlow.dll` and the bytes of the Workshop build, unless marked)

- Settings keys and defaults (initial values at `0x180017160`, parse in `FUN_180001750`): `glow_brightness` 0.6, `light_along` 5, `range` 1500
  (taken only above 0), `halo_step` 1.2 (taken only above 0.2), `light` 1, `halo` 1, `max_lights` 12 (taken only at 0 or above); `colour=`,
  `texture=`, `mesh=` add entries. `light_along` is used only when the blade is placed from the hand (below).
- Who (`FUN_180006a20`, `GameWorld::mainLoop` hook): `PlayerInterface::getAllPlayerCharacters`, then the update list's non-player characters
  within `range` of any player character (`FUN_180006980`, squared distance); dead ones skipped. Light and halo stop after `max_lights` sabers;
  the blade shader is applied to every match.
- The weapon's model is its record's `bare sword` file; the saber test needs a `texture=` keyword in a texture of the entity's materials, then
  the `mesh=` line gives colour and ranges (`FUN_180003bd0`, `FUN_1800039b0`). A loaded `LightsaberGlow.dll` (another plugin) takes the
  `Lightsaber_` textures (`GetModuleHandleA` check).
- Light (`FUN_180005c20`): `Renderer::createLight(LIGHT record, false, 0)` on its own scene node, placed at `(from + to) x 0.5` of the first
  range along the model; no record: logged "no light record", no light.
- Halo (`FUN_180005c20`): per range `count = max(1, (int)(|to - from| / halo_step + 0.5))` and points `from + i (to - from) / count`,
  i = 0..count; each point one `KSG_halo_<colour>` Particle Universe system (`FUN_180005570`). Model without ranges read: 9 points from the
  right hand along its bone axis ("blade placed from the hand").
- LIGHT records 1 to 6 (`KathunGlowingLightsabers.mod`, read with `FcsReader`): point lights, radius 45, brightness 1.2, falloff 1.5,
  variance 0; Blue, Red, Green, Yellow, Orange, Purple.
- **Observed**: `fx/KSG_halo.material` names `KSG_halo.dds`, which the Workshop item does not ship. **Unknown**: what the halo draws without
  it, in the original and here.
- **Unknown**: whether `Renderer::createLight(..., false, 0)` lights by day; the port follows the description ("light at night").
- **Unknown**: Project Kathun is not installed here, so no saber was matched, drawn or lit; nothing is checked in game.

## Port choices

- The light is placed on Meitou's drawn blade frame (hilt end to tip of the weapon's pick boxes) by the model's y bounds, not by the
  original's mesh-space ranges on the entity. A model whose long axis is not y is placed wrongly (**Unknown** whether any is).
- Past 48 halo glows at once (Meitou's effects keep 64 alive, shared by all mods) the points are spaced wider and each glow grown to match.
- Glow, halo and light are renewed every 0.25 s for 0.6 s.

## Harmony

- `Meitou.Game.Rendering.Characters.CharacterRenderer.CollectLamps` (postfix): adds the lit sabers' lights to the frame's moving lamps at
  `CharacterRenderer.HeldBladeFrame` (read by reflection), with the host's `GameHost.DrawKey` (reflection on `ModFrame._host`). A failure
  logs once and turns the saber lights off.

## Missing

- The blade's texture-region tint (`texture=` `u v` box and `tint`) and the emissive shader (`KSG_BladeGlow_FP`): Meitou's `Glow(Weapon)`
  lights the weapon from 0.3 of its length to the tip, and a glow and a tint cannot share one target; the `.hlsl`/`.program` are not read.
