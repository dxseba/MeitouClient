# Brutality DLL prototype

Experimental port preserved for later work. Visual parity with the integrated version is incomplete;
this is not the recommended playable build.

Gameplay reference: `3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa` (GPL-3.0-or-later).
This branch targets the [PR #270 API](https://github.com/VlcekM/MeitouClient/pull/270), with
neutral character-geometry, absolute-pose, projected-LOD and explicit combat-target contracts.
Those host contracts are required; this DLL is not compatible with an arbitrary stock client.
PR #270 itself contains no gore implementation.

Build: `dotnet build mods/Brutality/Meitou.Brutality.csproj -c Release` (.NET 10).
Install the packaged `Brutality` folder as a trusted code mod and enable `Brutality.mod`.
The package contains `Brutality.dll`, metadata, effect sprites and local anatomy; it never
ships replacement engine DLLs. See [ASSETS](ASSETS.md) for the local packaging command/provenance.
`brutality.json` with `{ "enabled": false }` disables registration and anatomy loading.

The DLL owns Brutality progression, head/torso/limb decisions, ordinary wounds, articulated
fragments and bowel tissue, robot interiors/cables/discharges/smoke/scrap, liquid blood,
weapon stains/drops, body-part targeting and labels. Torso halves have independent native
carry targets and shared loot. Saves use complete native character/item identities; migration
reads the integrated version's wound, cut, carried-corpse and weapon-blood records.
Transient blood/electrical effects do not replay after loading; bowel props reconstruct.

**Verified** (loader, gameplay, native adapter, save and geometry tests): trust/disable/relocation,
progression, actual-contact causality, cut policies, independent carry/loot identities, pending
save rows, anatomy bounds, shared fitting, cut exposure, supplied LOD and deferred GPU retirement.
**Observed** (isolated native screenshots): DLL discovery, torso separation, anatomy exposure
mask correction and distant interior LOD. Remaining visual cases require native review.

Known incomplete visual work: the cavity back wall and the original mouth/tongue filtering,
placement and per-part colours are not ported.

Visual differences from the integrated renderer:
- CPU wound pockets/exposure bands replace its projected wound-discard shader. Each producer starts at most four
  cold actor revisions per fill, stopping additional starts after ~5ms.
- Sections use the stock lit vertex-colour material, without the original layered tissue shading.
- Weapon stains multiply mesh colour; the original separate wet gloss/metal blend is unavailable.
- The mod effect pass has no native soft-depth, fog or near/far particle partition integration.

Generated variants retire after submitted draws finish. Active visible fragments still require
separate geometry memory; retirement tests do not imply constant memory for an unlimited crowd.

Developer previews require both `developerTools: true` and `MEITOU_BRUTALITY_PREVIEW` (torso,
vertical, head:0..2, limb:0..3, severe:0..2; optional :carry or :carry-negative and :putdown=180).
`MEITOU_BRUTALITY_FOCUS=1` centres isolated sandbox captures. Bowel trace/hide and anatomy-hide
environment switches are opt-in visual diagnostics, with no gameplay/save changes.
