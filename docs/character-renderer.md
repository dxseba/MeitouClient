# Character renderer

`Meitou.Rendering.CharacterRenderer` partials: core content/material/lifetime; Draw cull/LOD/batches;
Motion previous state; Portraits offscreen portraits; Stats diagnostic rings. Inputs are
interpolated simulation snapshots and data appearances, not simulation-owned GPU resources.
Game assembly rules: [characters](characters.md), [animation](animation.md).

## GPU layout and shading

Instance bones/material offsets index shared storage buffers; group compatible mesh parts.
The update places every in-range part once (model and previous matrix relative to the eye, LOD distance at bias 1) inside the
parallel pose loop; the colour, motion and shadow views from that eye reuse it, cull and pick levels in parallel chunks of 16
characters, and keep the serial order within each batch. Portraits, icons, hover masks and other eyes place their own.
The shadow cascades drawn in a frame are gathered together before the first (`PrepareDepth`, from `ShadowPass`'s `CasterPrepare`): one
parallel walk over the chunks tests each character and part against every cascade, then each cascade's batches and instance rows are
filled in parallel; each cascade's draw only records. Every view has its own batches, named by an id per (mesh part, level, sidedness).
Whether a character is indoors (the GI's interior flag; the host's hidden-indoors test too) is asked again only once it has moved farther
than the nearest interior box was when last asked (at most 50 units) or the loaded interiors changed (`WorldObjectRenderer.InteriorMemo`).
Each `PartMaterial` keeps its last record and is written again only in a new epoch (texture cache `Version`: an image loaded, swapped
or unloaded; LOD bias, stand-in, sampler generation, skin settings) or after its own fields changed (blend, body build). The records live
in `CharacterMaterialTable`, a persistent storage buffer (binding 7) with a slot per material: a record that came out different goes to a
new slot (frames in flight still read the old one), uploaded in one copy; a slot comes back FramesInFlight + 1 frames after its material
changed or went 600 updates undrawn. Texture needs
are offered, and the textures marked used, only on the frame `CharacterTextureBudget` commits (every 500 ms, under the cache's 2 s idle limit).
CharacterShaders set0 bindings: bones6, materials7 (persistent), morphs8, previous bones9; material20 texture
slots. CharacterLayoutTests pins CPU/GLSL layouts. Push depthIndex is near-depth bindless index
for motion,0 otherwise. Camera-relative transforms preserve precision; previous bone/transform
state must match motion history. Skin/body/head/hair/item modes share world lighting/haze.
The colour and motion views use the near slice's rotation-only view-projection (`WorldFrame` passes it, jittered and unjittered; the
previous frame's is moved by the eye difference in double), not the world one with the eye's position cancelled afterwards
(`FromOrigin`, still used by the other views): the world matrix's translation already holds the eye's rounding.
**Observed** (sandbox hero spawned at x = -50874, `--upscaler off`, camera locked on the leader at the minimum boom 10, CPU replay of the
renderer's matrix path): the look-at point projected 0.2 px (range -0.07 to +0.32) off the screen centre, a different error each
frame, against 0.000 near the origin; the world matrix applied to world positions steps by 0.25 px. Its previous-frame twin put the same
noise into the characters' velocity (median of a walker's vectors, second difference 0.39 to 0.25 px in x with the rotation-only
matrix). The error scales with 1/boom and with the distance from the origin; terrain and objects still use world matrices.
Head uses UV+(0,1); dye/vest/overlays and cutout rules come from research. Same-instance caster
pass preserves alpha coverage. Face morphs and body shape precede skinning.

### Skin (Meitou `skin` switch)

Faithful lights skin as every surface (`kenshiLight`, the game's deferred pass: clamped N·L). Meitou: Penner's pre-integrated skin
diffuse replaces the sun's N·L on bare skin (`SkinScattering`: a 64² RGBA16F table baked at start-up from the six-Gaussian skin
profile of GPU Gems 3 ch. 14, each layer normalised along the ring; u = N·L, v = √(1/r ÷ 1 mm⁻¹)). Curvature 1/r is baked per vertex at load
(`CharacterContent.MarkCurvature`, vertex colour Z, not on icon meshes, whose vertex colours tint them: mean |Δnormal| / |Δposition| over the vertex's edges, averaged over vertices sharing a position) and
interpolated; a screen-space fwidth estimate was constant per triangle and drew the low-poly facets and outlines in red. At 1 unit = 100 mm (an **Observed** scale, [terrain](formats/terrain.md)), times 30 (`SkinScatterScale`, a look: at true size the
profile barely reaches a face's 5-8 cm radius, terminator red 0.016 → 0.019; ×30 gives about 0.16 and a tail past N·L −0.2). Red, green and blue read the table with the
normal map mixed 0.6 / 0.3 / 0 towards the smooth normal. Only what the table adds over plain N·L is kept (its strongest channel): its loss of red
on the lit side (energy moved into the shadow) turned sunlit skin cyan-grey. That extra light takes the skin's own hue, saturated (albedo ÷ its
brightest channel, squared): red-orange on human skin, amber on hive chitin, teal on fishmen, any modded race its own. Skin share: body-shaded
parts of people (`CharacterShaders.Skin`; not animals; stumps included) whose race has it on, times what vest layers and hair/beard overlays leave
bare. Per race (`CharacterRenderer.SkinRaces`, settings Graphics > Skin, `meitou.user.json` `skinRaces`: only races changed from the default): every
person race of the loaded data (RACE records no ANIMAL_CHARACTER uses, mods included) is on unless RACE `is robot`. World and Meitou
stage (fog distance -3) and the portraits (the plain model, fog distance 0: their fixed key light is near the camera, so the glow stays on the
shadowed cheek, jaw and neck); the game's stage and icons keep N·L. Lamps too: the character shader defines `LAMP_SKIN`, and
`LampShaders.lampLight` takes each lamp's diffuse from `lampSkinDiffuse` (the same term, per-fragment inputs set in `main`); the specular keeps
N·L. Faithful: push `skinLut` 0, unchanged.
Limits: the sun shadow is not scattered, so the band past the terminator ends at the shadow edge; no back-lit
translucency (ears).

## Content, LOD and memory

`CharacterContent` owns buffers/textures/morph arena; worker fetching/preparation precedes render
uploads. Reuse appearances/content; replace only after ready and retire after GPU consumers.
`CharacterTextureBudget` shares mip/VRAM pressure policy; unavailable content uses a stand-in.
Loading readiness includes used textures deferred by memory pressure, not just queued decodes/uploads, but not the governor's coarser
replacements (`WorldTextureCache.AwaitedCount`: the finer image is drawn until one lands). Under the loading screen (`CharacterRenderer.Covered`)
textures upload and appearances build for up to 16 ms a frame instead of the play budgets.
Generated four-level reductions are always enabled; their index lists are cached on disk (`%LOCALAPPDATA%\Meitou\charlods`, keyed by the parts'
vertices and indices, radius and settings; `--no-load-cache` bypasses it), as building them is most of a mesh's preparation; with 100+ characters drawn in the world, pose rates fall with the character's size on screen
(its diameter in pixels at the view's projection, so resolution and field of view count and big animals keep their rate farther): 100 px and more every
frame, then at most 30/15/7.5 poses/s below 100/50/25 px (in time, the game's animation clock, each on its own phase); at most 48 characters, the largest first,
take the full rate by size, the next ones at least 30/s (`CharacterRenderer.PosePixelSteps`, `FullRateBudget`; engine choices). `CharacterInstance.FullRateAnimation` bypasses
the throttling for presentation actors, the first 10 of the player's characters (squad bar, then the
other squads in order) and the first selected one (`PlayerInterface.FullRateCharacters`). Shadow LOD doubles choice with cascade floors.
Clamp stored bounds to real mesh extent. Animation rendering does not independently integrate
walk-loop root movement; simulation owns relocation. Quads/morph and motion buffers retain
correct previous-frame data across skipped poses and content changes.

## Animals and portraits

Animal race/body/mesh/fur/clips/size use shared assembly with animal inputs, not human fallback.
Portraits use the same content/shading with a dedicated camera/render target; avoid mutating
world draw state. Asset/appearance failure must not corrupt another instance or portrait.
Portrait cells redraw only when their character, appearance, size, camera serial or streamed
texture images/swizzle change, or the look below does; animation and frame counts do not.

### Meitou stage and portraits

`PreviewLook` (set from the world's options every frame): enhanced when Meitou's tone map, grade or
temporal AA is on. The editor stage (stage light unclamped, fog distance -3, platform included) is
drawn into RGBA16F at 2x each way with Meitou AA (1x past 16.6 Mpx); each portrait (plain model) alone
into a 1024 px RGBA16F square (4x its cell). `PreviewResolve` tone maps each sample, averages by
coverage (anti-aliased edges on the transparent background), grades and dithers into the 8-bit
picture. Atlas cells are 256 px apart; enhanced portraits fill them (the original's 128 px were stretched to the
squad bar's 120-230 px from 1080 to 4K lines and looked soft), faithful ones keep 128 px in the corner. Portraits keep the
fixed plain light: a still drawn under the live sky would depend on the hour it was drawn. Faithful
keeps the original's direct 8-bit stage and 128 px plain portraits. No SSAO, shadows or GI (no scene).
Atlas layout (Meitou): squad members take cells 0-29; the conversation window's speaker and player
character take the last two (30, 31) while a conversation is open.
Item icons (`CharacterRenderer.Icons.cs`) are the same: an item's mesh drawn through the item shader into a texture of its own, as a pseudo-instance that the world and shadow views skip;
the camera, lighting and sizes are in [game/ui-screens.md](game/ui-screens.md) "Item icons".
Harness `--crowd` exercises instance scaling; compare pose/upload/draw/shadow separately.
**Observed** human_male:23 poses, no normal offsets,2986/15504 vertices affected, max offset0.18.
**Verified** (`FUN_140071ac0`, `skin.program`; survey 2026-10-21): the game bakes a look's poses on the CPU once (`Ogre::Mesh::softwareVertexPoseBlend`
per non-zero weight into a clone `<mesh>_morph_<n>`, then `removeAllPoses`) and never rebuilds normals or tangents; its vertex programs take no
pose input. No pose in base data (13 posed meshes, 292 poses), installed mods or the Workshop folder carries normals, so the bind normals stay,
as in Meitou (offsets only).

## Remaining limits

Far shadow casters: in Meitou's coarse cascades (`ShadowLod`) a part whose diameter is under one texel
(`ShadowPass.MinCharacterCasterTexels`) casts nothing (hair, beards, weapons, hats); A/B `char-casters`. Release, 400-character
crowd at The Hub: render-thread shadows -0.28 ms (-15 %), 11k of 921k pixels differ by 4/255 or more.
Texture stand-ins: a part whose cut-out texture is still streaming in is not drawn (shadow and motion passes too); the head's
region shows the skin constant and a flat normal until the head maps are resident; vest layers are drawn per ready layer
(`AlphaPending`, `HeadPending`, `HeadNormalPending`, the vest mask).
GPU culling, merged far shadow casters (one skinning per frame for every cascade), tail-mip texture stand-ins and
original animation-update budgets remain incomplete. The `charlod` switch picks the files' own levels instead (Faithful; [characters](characters.md#lod-of-character-meshes)). Tests pin layouts/data; visual parity requires named screenshots across
races, bodies, gear, animals, attachments, motion and shadow views. [bench](bench.md).

### Wounds

The torso holds Gore Lab's cavity lining (`CavityLiningData`), a back wall in the anatomy mesh, male or female, seen only through holes.

Skin sinks along its normal round heavy wounds of its body group (vertex shader, at most 0.25 units on arms, 0.4 on legs, 0.55 on the torso, to stay over the bone): flesh gone.

Up to 64 cuts are drawn as blade shapes in UV in the fragment shader (`woundField`), from the palette header; the mask only marks that a body has wounds.

`WoundMasks` keeps a 512 x 512 mask per wounded character in the body's UVs, the head (v below 0) in its lower half (red core, green open area, blue ring, alpha depth: above 0.55 muscle goes, showing bone and organs), repainted on the CPU when the wounds change, at most 4 a frame and 64 masks. A wound lands at the nearest body vertex facing its side. A wounded palette reserves 35 entries before the bones (the first holds the mask's bindless index; head layout below); instance flag bit 31 turns the fragment term on, off in portraits. Cuts draw on the body mesh only and are clipped at UV island edges. Metal does not draw them. Worn items are cut away near a wound; metal plate takes a smear of blood instead. A stump keeps its Ecorche bone and muscle at the cut. A deep belly wound drops the Ecorche bowel as one loose debris body; until then the bowel shows inside the body. A cut's open area discards the skin, showing the body's dark inside (body back faces, drawn only on wounded bodies) and the `Ecorche.anatomy` of the install's `Gauntlet of Crowns` mod, skinned as the nearest skin and coloured by layer, when that file exists. Each anatomy vertex is moved 0.2 units under its nearest skin vertex, and anatomy draws with its depth pushed one unit back, so the skin always wins and only a hole, cut per pixel, shows it. Meitou ships none of it.

### Head wound relief

`HeadWoundSurface` projects tapered blade strips onto original head triangles, adds barycentrically interpolated face morph offsets once, and skins them to the head bone. Uneven lips descend to partly blood-covered bone; the broken variant opens a cleft through both relief and internal skull. CPU frames/dimensions emit the matching GLSL skin field.

`CharacterInstance.HeadWoundPlacement` carries an optional original-bind-space ray anchor, outward normal, blade tangent and length/width scales (default 1). Finite scales in 0.5..2 multiply the mechanism dimensions; invalid scales keep the fallback path. The renderer projects that estimate onto incoming-facing head triangles, fixes the frame to the actual skin normal, and projects the strips across the curved surface. The simulation estimates the region from incoming side and selected head damage; no measured weapon/skin contact is available. Skin clipping retains original coordinates while relief receives the same face morph as the skin; scaling preserves the anchor and axis.

The wounded palette has 35 matrices: state at bone base -35, head placement at -34, torso separation at -33, 16 sphere matrices at -32, and 16 cut matrices at -16. The placement columns hold centre/half-length, tangent/half-width, normal/depth, then a valid marker and mechanism (`M42`). Header state bits 7/8 select scalp/skull (1/3); bits 9–11 select one of six profiles (three axes, two force sizes); bits 12–13 select mechanism (0/1 slash, 2 thrust, 3 crush). The vertex stage copies that mechanism even for a preset frame. Dynamic placement uses its fixed tangent and mechanism dimensions; a cut that does not fit retries up to 15% less length at the same anchor and axis, with the fitted size shared by skin and relief.

Slash retains the approved tapered sword geometry. Thrust is a compact pointed puncture (half-length/width 0.20/0.095, heavy 0.24/0.12); crush is a broader irregular laceration/fracture (0.30/0.19, heavy 0.36/0.23). Mechanism-specific width, bend and cleft formulas drive CPU relief and the GLSL skin, bone and hair fields. Without a supported dynamic frame, slash retains its six forehead presets; thrust/crush paint their compact shape at the preset frame and suppress legacy slash relief.

Palette/header visibility is separate from the temporary wound texture index: permanent lost groups, cut levels or detached parts still reserve and use the header when the generic mask is zero. Such a header is cleared and receives no stale spheres/cuts. Colour shading permits matched permanent anatomy states 2/4 (mouth/brain/stump/internal backing and fitted relief) without that mask; ordinary anatomy state 1 retains the generic-mask requirement.

Internal anatomy caches by resolved body mesh as well as rig/gender. Skull, mouth and brain bounds use negative-V skin with summed head/jaw influence at least 0.5, excluding nonzero morph slots from poses whose names contain `horn`. This removes Shek torso spikes and articulated horns without imposing a human-height clamp on Hiver heads. Missing/degenerate head bounds omit internal head layers while retaining torso anatomy. Brain layer 6 is additionally clipped to the same broken-skull cleft in colour (including flat/pick views), depth and motion, so imperfect fitting cannot expose it outside the opening.

Relief layers 8–19 encode profile and scalp/skull form, bypass the internal depth push, and discard inactive profiles in colour/depth/motion. `CharacterHeadWoundPool` keeps at most 64 stable per-character parts/buffers, rebuilding only when placement, form/profile/mechanism, appearance or face morph changes. Live draw-part overrides select them without mutating the shared appearance's parts. A slot can be reused only after 60 inactive updates. Full pools, editor blends, animals, robots and unsupported projections retain the mechanism's preset fallback. A valid surface frame whose strip cannot fit keeps dynamic painted clipping without a mismatched preset relief. Instance bit 4 enables skin removal under fitted relief above the anatomy pixel threshold; large internal anatomy stays shared.

Shared hair uses instance bit 5 to read the head header without skin deformation/recolouring. Colour, shadow and motion project outward strands and braids onto the same cut field; hair more than 0.65 units inward from the original skin frame stays intact. Skin and internal bone retain their +/-0.65 slab, so the opening does not reach the opposite side. Portraits keep their header-free path.

**Verified** (`HeadWoundPlacementTests`, `HeadWoundAnatomyBoundsTests`): projection selects the incoming wall from an interior anchor, rejects invalid estimates/scales, and applies face morphs once. Installed human male/female heads fit all six profiles of all three mechanisms at eight incoming sides; base profiles also fit policy scale extremes (0.9/0.85 and 1.2/1.3) while retaining the anchor/axis. Unit scales preserve the legacy dimensions. Mechanism 0/1 produces identical slash geometry, compact mechanisms have distinct footprints, and their fallback retains the shared body without slash relief. Synthetic head bounds reject torso spikes/horn slots, sum head/jaw influence, and retain a tall head rather than imposing a human-height limit.

**Observed** (human sandbox GPU close-ups with the generic wound mask disabled): slash placement opens the front, left, right and back regions with the approved narrow cleft. Thrust produces a compact pointed puncture; crush produces a broader jagged opening, each distinct in scalp and broken-skull views. A back crush opening remains visible through the raised braid; surrounding hair can partly obscure the opening. The six forehead presets fit earlier tested male/female faces. Thrust/crush are implemented visual variants awaiting user feedback.

**Observed** (installed CE body meshes, broken-skull close-ups): Shek, Worker/Soldier/Prince Hivers, Scorchlander and human retain fitted exterior wounds after head-volume correction, without the earlier brain protrusions beside/below the head or over Shek shoulders. The Soldier opening faces upward; a steep overhead view shows the fitted width and central cleft that are foreshortened from the front, while detailed brain gyri remain difficult to assess. Human force probes 15/30/35/60 show gradual size growth along the same axis, and the female force-60 opening fits beneath the tested hair. Two scripted combat snapshots (blocking/shuffling and a downcut, with a dummy opening) retain the wound on the turning/tilting human head. Other body morphs, hairstyles, race/angle combinations and the full animation set remain unverified.

### Torso separation

`CharacterInstance.TorsoCut` carries a bind-space plane, kept side (+1/-1) and optional `FullBody` mode. `TorsoCutSurface` normalizes finite planes and requires upward Y >= 0.5 only for legacy transverse mode. Colour, depth and motion clip skin/shared clothing/anatomy against the same ragged plane. In transverse mode, arm groups 1/2, jaw group 6 and hair stay wholly with the upper half, while leg groups 3/4 stay below. Full-body mode evaluates the same signed plane distance for every body group and shared hair, including head, pelvis and leg fragments. Rigid gear, lights, stuck bolts and pick boxes select the same half by bone. The torso matrix stores plane, kept side, exposure depth and FullBody in row2.z; the header remains 35 matrices. Existing `vHeadRelief` spare bits carry full-body exposure and mouth-window state, without adding varyings.

`TorsoCutSurface.Build` intersects real body triangles. Transverse mode excludes limb islands and keeps the largest torso section. Full-body mode traces closed contours through welded edge adjacency, pruning dangling chains and following bounded planar faces at mouth/eyelid junctions; it caps every retained head/torso/limb contour separately. A stable basis handles vertical normals. Bloody annular flesh rims use a local inset for long contours and descend to recessed lips, leaving the interior anatomy visible. Edge skin weights and body morphs interpolate onto the cap, with the morph applied once. Layer 32 identifies the cap, which bypasses the internal anatomy depth push. `CharacterTorsoCutPool` keeps at most 64 stable half-body buffers/parts (32768 vertices each, with a total full-body rim budget of 2048 points) and composes them with head placement without changing shared appearance parts.

Bone, muscle and organ fragments within `TorsoCutSurface.ExposureDepth` (2.8 bind units) of the cut can show without a temporary mask; full-body slices also expose fitted brain and mouth fragments within that window. Existing head-wound clipping remains active outside it, and ordinary skin occludes the interior. A spilled bowel's original anatomy layer stays hidden. The renderer uses existing local Ecorche assets and ships none. Soft-tissue head cutoff removes whole triangles rather than individual vertices, preserving boundaries between layers/bones. Separate world transforms supplied by the host make the physical gap; `TorsoCutPosition` returns the largest posed contour's rim centre; `TorsoCutNormal` points toward the positive half; `TorsoCutBounds` samples at most 256 retained world-space skin/edge support points for fragment pivots and floor placement. Held poses clone palettes and gear transforms; appearance changes rebuild rigid attachments from the frozen skin palette. Picking intersects only the kept box/plane volume.

**Verified** (`TorsoCutGeometryTests`): normalization/group ownership, open-centre caps, excluded arm islands, one morph application, intact source triangle boundaries and picking that ignores the removed half. Installed male/female humans and CE Worker/Soldier bodies fit both halves at coupled low/middle/high height and angle extrema, plus seeded impact planes, while their arm/leg attachment strips remain clear. **Observed** (scripted human male/female, clothed and `--scene bisect` captures): high/low and opposed diagonal cuts retain open bloody rims, interior organs and loose bowel; live impact poses fall into separated halves. Animation-only preparation of unseen corpses keeps face/hair intact. **Verified** (`TorsoCutGeometryTests`, installed humans and CE Worker/Soldier): vertical and steep diagonal planes cap separate contours through head/chest/pelvis/legs without joining unrelated outlines. `TorsoFragmentMotionTests` checks upward cut faces, a shared ground tangent, cut-leg pivots and floor support. **Observed** (native scripted captures): male/female vertical cuts, opposed long diagonals through pelvis/leg, clothed diagonals and a Worker Drone vertical cut retain separate exposed halves; the live sequence reaches a settled floor pose. Full-body hair keeps its own shading. Wider race/morph/animation combinations remain unverified visually.

### Weapon blood

Rigid melee blades use per-instance `HipWeaponBlood` / `BackWeaponBlood` (RGB, coverage), copied from the actual equipped item. `WeaponBloodFlag` (instance bit 6) reuses the four unused matrix-row W values and weapon-only `vHeadFrame` words; the body wound header, instance stride and varying count are unchanged. Eligible WEAPON parts use `bare sword`, or the whole weapon when no separate blade exists; sheath fields and `back2` backups are excluded. The longest signed local-bounds axis estimates grip-to-tip distance. Existing `bloody.png` splatter grows toward the tip, leaving the first 18% clear; blood changes albedo, gloss and metalness. Materials keep only immutable blade-axis/texture data; character/item colour and coverage never enter shared materials. Portrait/icon draws stay clean. Hit rules and item ownership: [blood-decals](game/blood-decals.md#blood-on-weapons-meitou).

**Observed** (native Samurai sandbox combat, `--scene weapon-blood`): the back blade changes from clean to coverage 0.48, 0.81 and 1 after delivered wounds, with the unused hip weapon at 0. The visible blade tip takes red splatter while its grip and separate sheath keep their original material. Wider weapon-mesh orientations remain unverified visually.
