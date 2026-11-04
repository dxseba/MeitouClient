# Local anatomy assets

`AnatomyAssets` resolves optional `assets/anatomy/Ecorche.anatomy`, `Brain.anatomy` and
`Mouth.anatomy` relative to `ModContext.Folder`; it does not depend on another installed mod.
Missing assets return null; `Require(name)` reports a required missing file.

`tools/scripts/package-gore-mod.ps1 -OutputDirectory <package> -AnatomyDirectory <source>`
copies the three local files and records byte sizes and SHA256 in `assets/anatomy/manifest.json`.
Without an explicit source it reads `KENSHI_PATH`, `-GameDirectory` or `meitou.local.json`.
`-AssemblyPath <built mod DLL>` optionally includes the mod assembly; the script never builds.
It copies an authored `Brutality.mod`, or writes a valid empty V17 data mod for code-mod discovery.

**Observed** (local source folder): `Ecorche-LICENSE.md` declares the model CC BY-SA 4.0
and names BodyParts3D and Z-Anatomy. The package copies that declaration when present.
**Unknown**: the declaration's byte-level provenance, full attribution and the Brain/Mouth
files' redistribution terms. The manifest therefore marks these local copies unconfirmed.
Anatomy binaries stay outside Git; public distribution requires a separate provenance review.

Robot effects are bundled under `assets/effects/`: wreaderror's CC0 lightning and Kenney's CC0 particle streak,
unchanged from the pinned reference. [Credits and source hashes](assets/effects/CREDITS.md) travel with the package;
the packager verifies all three copied files. These textures have separate confirmed CC0 notices from the unconfirmed anatomy assets.
