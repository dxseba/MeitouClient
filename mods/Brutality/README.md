# Brutality DLL

Experimental.

Gameplay reference: `3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa` (GPL-3.0-or-later).
Build the mod and client from this branch together. It extends the
[PR #270 API](https://github.com/VlcekM/MeitouClient/pull/270) with neutral character shaders,
opaque palette data, raw vertex channels, rendering lifetime/LOD controls and native physics
handoffs. These host contracts are required; an arbitrary stock client is not compatible.
The host APIs contain no gore rules or anatomy assets.

Build: `dotnet build mods/Brutality/Meitou.Brutality.csproj -c Release` (.NET 10).
Install the packaged `Brutality` folder as a trusted code mod and enable `Brutality.mod`.
The package contains `Brutality.dll`, metadata, effect sprites and licensed bundled anatomy;
it never ships replacement engine DLLs. See [ASSETS](ASSETS.md) for packaging and provenance.
The anatomy and its notices are committed; no Gauntlet of Crowns installation is required.
Use **Settings → Mod settings → Brutality** to switch **Brutality gore (restart required)**.
The saved menu choice applies on the next launch. With Gore off, the DLL registers its settings
page and skips gameplay and anatomy loading. Keep the code mod enabled in the launcher to use
the switch. Before a menu choice is saved, `brutality.json` supplies the `enabled` default.

The DLL owns Brutality progression, head/torso/limb decisions, ordinary wounds, articulated
fragments and bowel tissue, robot interiors/cables/discharges/smoke/scrap, liquid blood,
weapon stains/drops, body-part targeting and labels. Torso halves have independent native
carry targets and shared loot. Saves use complete native character/item identities; migration
reads the integrated version's wound, cut, carried-corpse and weapon-blood records.
Transient blood/electrical effects do not replay after loading; bowel props reconstruct.
A lethal unarmed martial-arts head hit can burst an organic humanoid's head from Brutality 80.
The hit must pass the native decapitation gate and deliver at least 85% of the head's maximum
health in cut plus blunt damage after armour, including at least 15% in blunt damage.
The chance rises from 4% at level 80 to 20% at level 100. The saved wound leaves a neck stump.
The impact sends actual skin and skull fragments from the struck model, plus up to three fitted
brain pieces and flying blood. Each fragment has a small ragdoll collider for tumbling, ground
contact and a light rebound. The display scene caps active bodies at 45. The pieces expire
six seconds after release and are transient on load. During asset preparation the displayed
head follows the body. The stump, spray and fragments switch together from that posed head.
Weapon stains participate in the state hash; new opaque item namespaces derive from the world
seed and registered mod RNG purpose, while loaded namespaces retain their original identity.
A native load reserves saved container namespaces before choosing a replacement for a missing sidecar.
Unresolved cosmetic items hash as a sorted record/colour multiset until they have a stable path.
**Observed** (win-x64): the packaged DLL passes `mod-check` on a 536-character save for 600 ticks,
both repeated one-thread runs and 1-versus-4-thread comparison; rendering remains a separate check.
Legacy hit sprites follow native weather/placer ownership; a DLL-only effect container does not
enable them in otherwise empty scenes. Liquid and robot effects use their own bounded lifecycle.

**Punch-through.** A bare-handed (Martial Arts, weapon category 5) crush blow to the chest or the
stomach part that deals, after armour, at least a set share of that part's maximum health opens a
hole through the torso (`ChestHolePolicy`). Mod settings: on/off, killing blows only (default off:
**Observed** in sandbox fights, punches knock out before they kill), the share (default 50%) and a
size scale for new holes (default 100%: a radius of 7.5-13% of the spine, more for harder blows).
Robots, animals and cleaved torsos get none; a later cleave hides it. The blow's roll varies the
height, side offset, size, an area-kept oval (aspect 0.8-1.25, any tilt), the edge's tear and a
wider exit (up to 1.28x); oversized shapes shrink to stay inside the torso. The hole is visual
only, saved in the sidecar and hashed only when present. All three shader passes discard skin,
worn cloth (wider) and anatomy inside the ragged bind-space ellipse: an extra prefix matrix before
the limb planes, flagged by bit 0 of header word 30. A generated tube (tangent w 36, colour alpha
the region) lines it from the front to the back skin with that skin's weights; its shader paints
muscle at the openings, rib chips and lung behind them in the chest, coiled gut and fat in the
stomach. The impact adds a blood burst and meat and bone pieces out of the exit. Portraits use the
host shader and show no hole. **Observed** (2026-11-05, Windows Vulkan sandbox previews of a Hungry
bandit and a Starving Vagrant): the background shows through from front and back, cloth edges
fray, and the wall shows at oblique angles. A real martial-arts hit is not yet checked in play.

Surface rendering uses the ported colour, depth and motion shaders with projected wound masks,
raw anatomy layer channels and the original 36-matrix prefix layout. It includes layered cut
sections, fitted brain geometry/sections, cavity backing and mouth/tongue fitting and filtering.
Generated interiors use explicit sidedness and projected-size limits. Masks and unchanged
payloads and source exterior LOD plans are reused. Ordinary wounds keep native exterior meshes;
new variants wait for pending CPU fits instead of blocking a render frame. Hidden organic interiors do not cast shadows;
part LOD budgets keep their own radius. Healthy fits and exterior LOD plans prepare on one background
worker per world, with 16 entries/192 MiB of fit residency (one oversized fit alone); GPU preparation
uses the remaining frame budget for shaders/exteriors; full anatomy uploads only for actual cuts.
Active cut geometry retains its fitted source across cache eviction. One shared background worker
prepares cut and wound geometry, with priority for queued cuts and up to 128 owners per queue.
Ordinary wounds use the native or compatible mesh while preparation runs.
While replacement geometry and GPU assets prepare, the last complete mesh follows the live
ragdoll with its original joint lengths. First-seen live cuts can use the complete unsplit birth
pose. Restored unseen cuts remain hidden until ready. A family switches together and blends from
the last drawn pose into its current physical poses over 120 ms. Fluid anchors use the drawn poses.
A newer cut supersedes queued work, and world disposal cancels pending work. Preparation and
uploads use the host's background and frame budgets. Lower LOD cuts share the original decoded vertices. Frozen fragment scenes skip solver
steps while keeping kinematic collision obstacles. Render resources
retire after submitted draws finish. Active visible
fragments still need separate geometry memory; an unlimited crowd is not a bounded-memory case.

Tests cover loader trust/disable/relocation, progression and actual-contact causality, native
capture/claim/return, cut policies, independent carry/loot, pending saves, anatomy bounds and
fitting, source shader compilation, palette packing, LOD and resource retirement, and the chest
hole's rule, resolved-hit path, save, shader payload and lining. Isolated native
Windows previews have shown DLL discovery, body separation and projected anatomy exposure;
these checks do not establish visual parity across every race, outfit, cut or crowded scene.

Remaining limits:

- Fragment and bowel physics run in a separate Bepu scene, with no cross-collision against native ragdolls.
  The initial handoff can also advance a newly captured body again in the same tick; trajectories
  are not identical to the integrated runtime.
- The reference human ragdoll has no foot/toe colliders. DLL previews can leave foot skin below
  the floor; a matched integrated-runtime comparison is still pending. See [collision evidence](../../docs/game/ragdolls.md#cut-fragment-collision-approximation).
- Cavity lining is fitted from the installed body skin at runtime; it can differ from the reference’s baked lining.
- Weapon staining still uses a vertex-colour approximation rather than the exact wet gloss/metal blend.
- The mod effect pass lacks native soft-depth, fog and near/far particle partition integration.

Developer previews require both `developerTools: true` and `MEITOU_BRUTALITY_PREVIEW` (torso,
vertical, head:0..2, limb:0..3, severe:0..2, chest:0..5 and stomach:0..5 where 3..5 kill the target;
optional :carry or :carry-negative and :putdown=180).
`head:1:native` runs the pinned lethal-head-hit recipe at tick 120; `burst` previews the martial-arts
finish through a lethal native blunt head hit. `:all` targets the sandbox crowd.
`:after=120` delays a preview until that tick to measure cuts after healthy asset preparation.
`MEITOU_BRUTALITY_FOCUS=1` centres isolated sandbox captures. Bowel trace/hide and anatomy-hide
switches are opt-in visual diagnostics, with no gameplay/save changes.

With developer tools enabled, `MEITOU_BRUTALITY_SANDBOX_LEVEL=100` sets the primary sandbox
hero’s stored stats and Brutality to 100, matching `--sandbox-stats 100 --sandbox-brutality 100`
in the integrated client. It does not equip stronger weapons or change NPCs/new games.
`MEITOU_BRUTALITY_TRACE_HITS=1` logs at most 256 real contacts per world, including post-armour
damage, the skill/lethal/damage gates and accepted cuts; it is off outside developer tools.
