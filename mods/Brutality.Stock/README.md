# Brutality for stock MeitouClient

Built against upstream MeitouClient `0938b0360eac51e53cee540b6db7189bfd3f557f` with the official `0.17.2.0` assembly version. Verified on Windows with `v0.17.5-nightly.20261112` (`e85d0d54`). Copy the whole `Brutality` folder into the configured Kenshi installation’s `mods` folder. Enable `Brutality.mod` and trust its code in Meitou. Keep one Brutality build active per installation. The DLL loads through the stock code mod loader and uses the stock Meitou assemblies.

Open Meitou’s mod settings to enable or disable Brutality and restart to apply. The in-game Brutality panel shows progression, unlocks and the current cleave chance. It also has switches for the HUD and Alt labels. Enable native Decapitation and Advanced Ragdolls for their corresponding effects. With developer tools enabled, use F10 **Brutality level (0-100)** or **Gore preview (selected)** to test the selected character.

The port includes directional head and limb cuts, rare paired arm or leg loss, head fragments and brains, chest or stomach cleaves, articulated torso fragments, attached intestines, race-coloured blood, stained weapons, and mechanical internals with sparks, smoke and scrap. Each torso half can be carried separately with the native animation. Living incapacitated enemies can be attacked at a selected body part. The punch-through controls govern new holes, including the damage threshold and hole size. A qualifying unarmed blow that destroys an organic head can scatter six head fragments and three brain fragments. The chance rises from 10% at Brutality 80 to 60% at 100.

Torso cleaves require a lethal sword or other blade slash to the chest or stomach with at least 70 delivered cut damage and at least 75% of that part’s maximum health. Brutality 100 gives qualifying hits a 95% chance. Four heavy head hits can open a localized wound. Jaw and skull variants require the major head tier and at least 35 force for the variant. Punch-through defaults to an unarmed chest or stomach hit with at least half the part’s maximum health in delivered damage. Fatal flank blows can also open the torso. Fatal punches through a robot leave a metal opening. In the developer sandbox, use F10 **Force hits to** to pick the hit location and **Sandbox hero melee damage (1-12)** to make lethal hits easier to reproduce. The multiplier applies to the primary sandbox player.

Generated geometry is prepared in the background and admitted within bounded scene and renderer budgets. Simultaneous cuts can finish on different frames. At the mesh limit, settled uncarried halves can leave the scene temporarily. Their poses and shared loot are retained, and the halves can return when there is room. If a cut cannot be built, the native appearance is used. A targeted attack approaches and faces the selected body part when its ragdoll pose is available. The weapon can keep its stain when dropped and picked up. Torso halves retain earlier wounds and amputations. Torso halves, detached heads and severed limbs can be saved. Head pieces saved in flight resume their motion. Intestine poses retain their velocity, attachment and settled state. Burst debris is temporary. Nearby moving native ragdolls receive bounded normal impulses from fragment contacts through the stock physics update.

This port writes saves under `dxseba.brutality.stock`. It can import progression, wounds, cut shapes, weapon stain data and supported torso carries from earlier versions. Integrated aimed orders restore against a unique matching body part. Imported fragments rebuild on the saved map. Older DLL order sidecars and incompatible in-flight physics require a fresh order or pose.

## Build and package

Run the following commands from the repository root.

```powershell
dotnet build mods/Brutality.Stock/Meitou.Brutality.Stock.csproj -c Release
pwsh -File tools/scripts/package-gore-mod.ps1 -OutputDirectory dist/Brutality -AssemblyPath mods/Brutality.Stock/bin/Release/net10.0/Brutality.dll
```

The script creates `Brutality.mod` for discovery and includes the manifest, bundled anatomy, effects and licence notices. Install the resulting `dist/Brutality` folder. `mods/Brutality` contains the shared source and assets. Build the stock project above for this branch.
