# Brutality DLL

The gameplay reference is MeitouClient commit `3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa`.
Keep its values and observable behavior; identify forced API deviations explicitly.
Generic host API additions are permitted when needed by the mod; keep them reusable and
independent of gore implementation or assets.
Licensed third-party anatomy derivatives are committed under `assets/anatomy/` with their
source hashes, attribution and licence notices. Preserve these when packaging; never add
original Kenshi assets or unrelated atlas content.
The DLL uses the game's shared Meitou assemblies. Never package replacement engine DLLs.
