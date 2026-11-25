# Flowing Hair (port)

Port of Teirdalin's *Flowing Hair [RE_Kenshi]* (Workshop 3798913525, version 4.3), from its shipped `FlowingHair.ini`, DLL strings and its
GPL-3.0 source ([Teirdalin/Flowing-Hair](https://github.com/Teirdalin/Flowing-Hair)), used for facts only.

- **Hair and beards sway**: scalp hair and beards hang from the head and swing with the head's motion, the character's own acceleration
  (inertia) and running airflow, gravity and the wind; rain soaks them (heavier, less blown), and they dry again. Only the nearest
  *Character Limit* characters within *Flowing Hair Distance* of the camera flow (players and NPCs alike; NPCs only with *Non PC Characters Have
  Hair Physics*); the rest are drawn as the file has them.
- **How**: when a hair or beard mesh is prepared as worn gear, its hanging vertices are re-skinned from the head bone to a chain of 4 (scalp) or
  3 (beard) nodes, which are palette copies of bones no other mesh uses (`Bip01 * Toe0Nub`, `HeadNub`, `Prop1/2`, feet, calves; the doubled
  palette Meitou's gear sway already has for backpacks). Each frame a Verlet chain per mesh is stepped in the character's model space and
  written into those copies; at rest the copies equal the head bone. Display only: nothing reaches the simulation, saves or the state hash.
- **Tails**: the Khajiit race mod's bodies (`khajjit_male.mesh`, `khajjit_female.mesh`) and The Cat Mod's `cat.mesh` get their tail
  re-skinned the same way onto 4 nodes, driven as the original's tail rod: a pitch and a side bend on springs, an idle swish and a lag
  against the owner's turning (*Khajiit Tails*, *Cat Tails*).
- **Rain and dust**: the wind also pushes the particles of every weather system whose name holds rain, dust or sandstorm (*Rain And Dust*,
  *Particle Wind Scale*).
- **Stress wind** (key action *Toggle stress wind*, default P as the original's): blends in a steady maximum wind (*Stress Wind
  Acceleration*, 240) in 1/0.75 s and out in 1/3 s, while *Stress Wind Key* is on. Meitou binds P to photo mode, so the default is left out
  with a log line until the player binds the key on the Controls page.
- **Settings** (Mod settings): the original's five graphics rows (labels and hints as its DLL has them) plus its ini keys under Physics and
  Wind, with the values the Workshop release ships.

## Facts

- **Verified** (shipped `FlowingHair.ini`): Gravity 15.06, FlowingHairDistance 500, FlowingHairCharacterLimit 4, Inertia 0.035, MovementDrag 12,
  MaximumMovementAcceleration 120, Damping 0.035, BendingStiffness 0.45, ShapeStiffness 0.018, BeardShapeStiffness 0.0045, BeardRootFraction
  0.04, ConstraintIterations 8, MaximumAcceleration 40, CrownFraction 0.04, ScalpShell 0.16, HairRigidity 35, FaceCollision 1; `[FlowingWind]`
  BaseWind 15, GustWind 45, GustFrequency 0.04, DirectionDegrees 35, DirectionVariation 75, RainAndDust 1, ParticleWindScale 1.
- **Verified** (DLL strings): version 4.3; the graphics rows "Hair Gravity", "Hair Rigidity", "Flowing Hair Distance", "Flowing Hair Character
  Limit", "Non PC Characters Have Hair Physics" and their hints; keys WetHair, WetHairSoakSeconds, WetHairDrySeconds, WetHairExtraWeight,
  WeatherWindMultiplier, WeatherGustFraction, MaximumWeatherWind, KhajjitTails, CatTails, EnableStressWindHotkey.
- **Verified** (source, `HairCandidate.h`): hair is told by mesh name: beard, facial, moustache, mustache = facial hair; hair, scalp,
  ponytail, mohawk, nutfro = scalp hair.
- **Verified** (source, `ClothSolver`, `Plugin.cpp`): fixed step 1/120 s, at most 4 steps, frame time capped at 1/15 s; Verlet velocity kept
  `1 - damping - 0.05 wet`; distance constraints, then shape pull `stiffness x 4^((rigidity - 50) / 50)`, then a displacement cap of
  `clamp(span x 0.25, 0.25, 1.5)` (beards `span x 0.5`); force `limit(inertia + gravity x wet weight + wind / wet weight, max) + airflow / wet
  weight`, inertia `(v_last - v) x inertia / dt`, airflow `-v min(drag, maxMove / |v|)`; reset after a move over 10 units. Defaults in source:
  WetHairSoakSeconds 20, WetHairDrySeconds 120, WetHairExtraWeight 0.75, FollowWeather 1.
- **Verified** (source, `KhajjitTail.h`, `CatTail.h`, `TailDynamics.h`): tail profiles by body mesh name: Khajiit male 139062 triangles, tail
  runs from triangles 68446 and 137977 of 1085 each; female 33317, 16837 and 32333 of 984; cat 8376 triangles, 2 x 152 tail triangles listed by
  id. A tail shares no vertex with the body, lies in x -2..2, y -1..13, z -7..0 (cat x -3..3, y 0..15, z -22..-6), is at least 5 tall and reaches
  y 8; its root band is its top 6 %. The cat's tail is solved in (x, z, -y). Rod: springs `v += (16 (target - a) - 8 v) h`, pitch target
  `clamp(-0.04 fz / r, -0.04, 1.35)`, side `clamp(0.017 fx / r + idle, -0.65, 0.65)`, `r = (0.5 + 0.015 R)(1 + 0.65 wet) + 0.025 max(0, -fy)`,
  idle `(0.085 sin(1.6 t + phase) + 0.025 sin(0.71 t + 1.73 phase)) / (1 + 0.009 R + wet)`, turn lag decaying over `0.28 - 0.0012 R` s at
  strength `0.9 - 0.004 R`, clamped to (-0.08..0.30, -0.55..0.55, -0.40..0.40), hip yaw adding `-0.55 yaw` of side bend; 240 Hz, 16 steps.
  Tails take inertia and airflow at 0.1.
- **Verified** (source, `IntegratedWind.cpp`): rain/dust wind is a LinearForce affector (force added, `FA_ADD`) named `FlowingWind_Force_v1` on
  each technique (at most 16) of a ParticleUniverse system whose template name holds rain, dust or sandstorm, force = (wind x, 0, wind z) x
  ParticleWindScale from the shared weather sample.
- **Verified** (DLL strings): "debug hotkey enabled: P toggles maximum steady wind; suppressed while typing or in a modal"; StressWindAcceleration
  (source default 240, range 40..1000); the stress blend goes up at 0.75 and down at 3 per second (source).
- **Verified** (source, `WeatherDynamics.h`, `WindField.cpp`): weather wind = direction x min(max, speed x multiplier x (1 + gust (0.7 sin p +
  0.3 sin(2.17 p + 1.3)))), `p = 2 pi f t`, defaults multiplier 2, gust 0.25, max 60; procedural wind as in `sampleWind`, one global sample
  (no spatial variation) shared by hair and particles.
- **Observed** (`strings` on `female_skeleton.skeleton`): Kenshi's human rig has no finger bones; its leaf bones are `Toe0Nub` (x2), `HeadNub`,
  `Prop1`, `Prop2`.
- Harmony (all display side):
  - `Meitou.Game.Rendering.Characters.CharacterContent.Mesh` and `CharacterContent.Prepare` (prefixes): a hair mesh is asked again with the
    plan's marker as its `GearSway.Bake` rule, so prefetch and build share one cache entry.
  - `Meitou.Game.Rendering.Characters.GearSway.BakeMesh` (prefix): with the marker, the hair bake instead of a backpack's.
  - `Meitou.Game.Rendering.Characters.CharacterContent.AddItemParts` (postfix): a look with baked hair gets `CharacterAsset.SwayPalette`.
  - `Meitou.Game.Rendering.Characters.CharacterRenderer.Sway` (postfix): the chains, or rigid copies.
  - `Meitou.Game.Rendering.Characters.CharacterRenderer.PosePortrait` and `CharacterRenderer.Freeze` (postfixes): rigid copies.
  - `Meitou.Game.Rendering.Sky.WorldWeather.Update` (postfix): the weather's wind and rain.
  - `Meitou.Data.Particles.ParticleSimulation.Affect` (postfix): the wind added to rain and dust particles' velocity.
  - The tails come through the same `Mesh` / `Prepare` / `BakeMesh` / `AddItemParts` patches (the body mesh), read
    `ParticleSimulation.TechniqueState` (`Count`, `Vx`, `Vz`) by reflection.
  - Read by reflection: `CharacterRenderer.CharState` (`Asset`, `Bones`, `World`, `Centre`, `PosedRagdoll`), `CharacterRenderer.Live.Asset`,
    `CharacterRenderer._motionSeconds`, `_portraitBones`, `_states`, `CharacterAsset.Rig`, `CharacterContent.Rigs`, `FindMesh`,
    `CharacterRigs._rigCache`, `SkeletonRigCache._rigs`, `GearSway.Bake`.

## Port choices

- A node chain instead of per-vertex cloth: the mesh bends smoothly along its length but does not crumple; the original's scalp-shell and
  crown pinning (ScalpShell, CrownFraction) become a root line at the head's centre (a beard: a quarter of its height below its top), and its
  face ellipsoid a "no nearer the head's centre than 0.9 of rest" rule. Hair hanging less than 1 unit below the root line (beards 0.6) stays rigid.
- The wind is the weather's wind at the camera (Meitou's `WeatherState`), the rain its rain; indoors and swimming are not told apart.
- The settings live under Mod settings, not the graphics page or `FlowingHair.ini`.
- The Cat Mod's tail is found as the mesh's pieces wholly inside the tail box with exactly 2 x 152 triangles, not by the original's list of
  triangle ids; the Khajiit tails by triangle count and runs only (Meitou welds vertices, so the original's vertex count and index hash do not
  apply). The tail rod's centreline is drawn through 4 nodes, not 24 rings. The particle force applies to every technique of such a system.
- The stress key is a rebindable key action; the original suppresses it while typing or in a modal, as Meitou's key actions are.

## Missing

- Nothing for want of a hook. **Unknown**: not checked in game; the tails could not be checked at all (neither race mod is installed here), so
  whether their meshes match the profiles after Meitou's weld, and whether the cat's skeleton has 4 free leaf bones, is open: a mesh that does
  not match is drawn as the file has it. The original's `TailFollow` correction of the tail against the body is not ported.
