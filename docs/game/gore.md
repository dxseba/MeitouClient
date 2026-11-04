# Optional Brutality gore

**Verified** (toggle/configuration/save/render regression tests and native Windows off/on captures).

`meitou.user.json` has `goreMode` (default `false`). Gameplay settings expose
**Brutality gore (restart required)**. A missing key in an older configuration stays off;
Reset also selects Off. Changing the checkbox saves the next launch's preference.
The running host keeps its initial value, including across save loads and new games.

With the option enabled, the integrated implementation adds Brutality progression,
advanced wounds, varied head/limb cuts, body cleaves, organs, weapon staining and the
extra liquid/electrical effects. Standard blood, Dismemberment, Decapitation and Advanced
ragdolls retain their independent settings. Gore off restores their ordinary behavior.

Disabling the feature suppresses new gore changes and its presentation. It does not
remove saved skill XP, wounds, cuts, item stains or pending corpse/carry records. Save
loading/binding and the existing codecs still run so a later enabled launch can read them.
Old queued visual events must not become new bursts after loading.

The bundled `assets/anatomy` folder contains `Ecorche.anatomy`, `Brain.anatomy`,
`Mouth.anatomy`, their licence notices and a SHA-256 manifest. Builds and publishes copy the
complete folder beside the game, viewer, tools and test executable. Runtime resolution prefers
this packaged folder; the selected install's `mods/Gauntlet of Crowns` remains a legacy
fallback. Brain and mouth files come only from beside the selected Ecorche file; bundles
are not mixed. A normal disabled session does not import anatomy. See the
[asset README](../../assets/anatomy/README.md) for provenance and redistribution notices.

Validation: configuration/default/restart and off/on gameplay/render/save regression
coverage is in `Meitou.Tests`. Native screenshots are needed to assess visual quality;
unit tests alone do not establish parity across races, gear and cut angles.
