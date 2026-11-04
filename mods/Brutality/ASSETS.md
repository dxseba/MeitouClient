# Bundled assets

`AnatomyAssets` resolves `assets/anatomy/Ecorche.anatomy`, `Brain.anatomy` and `Mouth.anatomy`
relative to `ModContext.Folder`; it does not read another installed mod. Missing optional files
return null; `Require(name)` reports a required missing file. The licensed three-file bundle is
committed with [attribution, licence notices and source hashes](assets/anatomy/README.md).

After building the DLL, package it without a Kenshi install or downloads:

```powershell
pwsh -File tools/scripts/package-gore-mod.ps1 -OutputDirectory <package>/Brutality -AssemblyPath mods/Brutality/bin/Release/net10.0/Brutality.dll
```

The script uses the committed anatomy by default, verifies its manifest hashes, and preserves all
listed notices and provenance. `-AnatomyDirectory <source>` or `-GameDirectory <Kenshi>` explicitly
selects a local override (the latter uses `mods/Gauntlet of Crowns`; `KENSHI_PATH`/`meitou.local.json`
can supply its install path). Byte-identical overrides retain the verified bundle's notices;
different bytes receive an unconfirmed local-override manifest rather than inherited permission.
The script never builds. It copies an authored `Brutality.mod`, or writes an empty valid V17 data
mod for discovery, and includes the code's GPL licence without replacement engine DLLs.

Robot effects under `assets/effects/` are wreaderror's CC0 lightning and Kenney's CC0 particle
streak, unchanged from the gameplay reference. [Credits and source hashes](assets/effects/CREDITS.md)
travel with the package; copied effects and anatomy metadata are checksum-verified.
