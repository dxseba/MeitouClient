# Anatomy assets

These ECR1 geometry files are third-party anatomy derivatives, copied unchanged from the
user-supplied Gauntlet of Crowns bundle. They contain anatomy meshes, not native Kenshi mesh
payloads. `manifest.json` records their byte lengths and SHA-256 hashes.

The accompanying [licence notice](LICENSE.md) and [attribution](ATTRIBUTION.md) are retained
verbatim; the notice files were renamed to keep their relative links working. They credit
[Écorché](https://github.com/Charleeh/ecorche), [BodyParts3D](https://dbarchive.biosciencedbc.jp/en/bodyparts3d/)
and [Z-Anatomy](https://github.com/Z-Anatomy/Models-of-human-anatomy). Adapted anatomy mesh data is separate
from the game's GPL-3.0-or-later code. The upstream pipeline's MIT notice is retained for
completeness; its code is not bundled here.

The adapted meshes are distributed under CC BY-SA 4.0. `Brain.anatomy` contains cortical
gyri from Brain for Blender by Anderson M. Winkler (originally CC BY-SA 3.0), as adapted
in Z-Anatomy; `Mouth.anatomy` contains eight Z-Anatomy oral/pharyngeal structures.
[Supplemental attribution](SUPPLEMENTAL-ATTRIBUTION.md) records those credits, original
licence links, exact selections, source hashes and verified ECR1 export transformations.

This complete folder is bundled with the Brutality code mod. Mod builds and publishes copy
it under `assets/anatomy`; `tools/scripts/package-gore-mod.ps1` uses this committed bundle
by default and preserves its manifest and notices. Runtime paths are relative to the mod
folder, with no Gauntlet of Crowns dependency. Disabling the mod prevents anatomy loading.
Keep these notices and the manifest with redistributed assets.
