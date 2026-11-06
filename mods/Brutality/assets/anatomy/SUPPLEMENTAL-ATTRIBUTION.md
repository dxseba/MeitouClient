# Supplemental anatomy attribution

This supplements the preserved [original attribution](ATTRIBUTION.md) and
[licence notice](LICENSE.md); it does not replace them. Adapted mesh data in this bundle
is distributed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/),
with the full text in `Ecorche-CC-BY-SA-4.0.txt`.

## Brain.anatomy

Cerebral cortical gyri from **Brain for Blender**, by **Anderson M. Winkler**, originally
licensed under [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), as adapted
in **Z-Anatomy** by **Gauthier Kervyn** under CC BY-SA 4.0. Original author, project and
licence attribution are retained here. Source: [Brain for Blender](https://brainder.org/research/brain-for-blender/)
and the [Z-Anatomy atlas](https://github.com/Z-Anatomy/Models-of-human-anatomy).
The adapted ECR1 data is distributed under CC BY-SA 4.0; CC BY-SA 3.0
[section 4(b)](https://creativecommons.org/licenses/by-sa/3.0/legalcode.en) permits a later
licence version with the same elements.

Selection: the first 80 mesh objects in object-name order whose names contain `gyrus`
(case insensitive). The source contains 52 matches, producing 52 batches (26 bilateral
pairs). This is cortical gyri geometry, not a claim that all brain structures are included.

## Mouth.anatomy

Oral and pharyngeal structures from **Z-Anatomy**, by **Gauthier Kervyn**, CC BY-SA 4.0;
underlying **BodyParts3D**, © **The Database Center for Life Science**, CC BY 4.0,
attribution is retained. Sources: the [Z-Anatomy atlas](https://github.com/Z-Anatomy/Models-of-human-anatomy)
and [BodyParts3D](https://dbarchive.biosciencedbc.jp/en/bodyparts3d/).

Selection: eight mesh objects, in export order: `Tongue`, `Soft palate`, `Uvula of palate`,
`Gingiva`, `Superior pharyngeal constrictor.l`, `Superior pharyngeal constrictor.r`,
`Middle pharyngeal constrictor.l`, `Middle pharyngeal constrictor.r`.
No separately licensed ear or kidney models are selected by either export.

## Export changes and source identity

Both exports evaluate the selected Blender meshes, triangulate their surfaces, apply
object-to-world transforms and convert coordinates and normals from `(x, y, z)` to
`(x, z, -y)`. Normals use the inverse-transpose object transform and are normalized;
UV values are zero. Named tissue batches are written as ECR1. There is no additional
polygon simplification in these two exporters.

**Verified**: replaying these transformations in Blender 5.2.2 from the Z-Anatomy Blender
template `Z-Anatomy/Startup.blend` reproduces both bundled files byte for byte, including
their manifest SHA-256 hashes. Source identity:

- `Z-Anatomy.zip`: SHA-256 `e029688545627bd0214b269e1063143abb580aad72b2c2445d6d8a9a0d9da736`.
- `Z-Anatomy/Startup.blend`: 306,838,281 bytes, SHA-256 `9f08a17ea0115fed80b2a73ecdf0a1bc2ab2f6956f37c593ce23d513ea35afcd`.

The exact upstream commit of that archive is unknown; its project URL and file hashes
identify the inspected source without claiming an unverified revision. `Ecorche.anatomy`
retains the separate selection, fitting and conversion credits in `ATTRIBUTION.md`.
