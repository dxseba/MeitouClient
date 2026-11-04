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
`brutality.json` with `{ "enabled": false }` disables registration and anatomy loading.

The DLL owns Brutality progression, head/torso/limb decisions, ordinary wounds, articulated
fragments and bowel tissue, robot interiors/cables/discharges/smoke/scrap, liquid blood,
weapon stains/drops, body-part targeting and labels. Torso halves have independent native
carry targets and shared loot. Saves use complete native character/item identities; migration
reads the integrated version's wound, cut, carried-corpse and weapon-blood records.
Transient blood/electrical effects do not replay after loading; bowel props reconstruct.
Legacy hit sprites follow native weather/placer ownership; a DLL-only effect container does not
enable them in otherwise empty scenes. Liquid and robot effects use their own bounded lifecycle.

Surface rendering uses the ported colour, depth and motion shaders with projected wound masks,
raw anatomy layer channels and the original 36-matrix prefix layout. It includes layered cut
sections, fitted brain geometry/sections, cavity backing and mouth/tongue fitting and filtering.
Generated interiors use explicit sidedness and projected-size limits. Masks and unchanged
payloads and source exterior LOD plans are reused. Ordinary wounds keep native exterior meshes;
new variants wait for pending CPU fits instead of blocking a render frame. Hidden organic interiors do not cast shadows;
part LOD budgets keep their own radius. Healthy fits and exterior LOD plans prepare on one background
worker per world, with 16 entries/192 MiB of fit residency (one oversized fit alone); GPU preparation
uses the remaining frame budget for shaders/exteriors; full anatomy uploads only for actual cuts.
Active cut geometry retains its fitted source across cache eviction. Preparation is best-effort:
an immediate or uncached cut can still require
CPU work. Lower LOD cuts share the original decoded vertices. Frozen fragment scenes skip solver
steps while keeping kinematic collision obstacles. Render resources
retire after submitted draws finish. Active visible
fragments still need separate geometry memory; an unlimited crowd is not a bounded-memory case.

Tests cover loader trust/disable/relocation, progression and actual-contact causality, native
capture/claim/return, cut policies, independent carry/loot, pending saves, anatomy bounds and
fitting, source shader compilation, palette packing, LOD and resource retirement. Isolated native
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
vertical, head:0..2, limb:0..3, severe:0..2; optional :carry or :carry-negative and :putdown=180).
`head:1:native` runs the pinned lethal-head-hit recipe at tick 120; `:all` targets the sandbox crowd.
`:after=120` delays a preview until that tick to measure cuts after healthy asset preparation.
`MEITOU_BRUTALITY_FOCUS=1` centres isolated sandbox captures. Bowel trace/hide and anatomy-hide
switches are opt-in visual diagnostics, with no gameplay/save changes.

With developer tools enabled, `MEITOU_BRUTALITY_SANDBOX_LEVEL=100` sets the primary sandbox
hero’s stored stats and Brutality to 100, matching `--sandbox-stats 100 --sandbox-brutality 100`
in the integrated client. It does not equip stronger weapons or change NPCs/new games.
`MEITOU_BRUTALITY_TRACE_HITS=1` logs at most 256 real contacts per world, including post-armour
damage, the skill/lethal/damage gates and accepted cuts; it is off outside developer tools.
