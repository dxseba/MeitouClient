# Brutality DLL

The only gameplay reference is MeitouClient commit `3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa`.
Keep its values and observable behavior; identify forced API deviations explicitly.
Engine changes for PR #270 remain separate from this mod branch.
Anatomy files are local dependencies under `assets/anatomy/`; never add them to Git.
The DLL uses the game's shared Meitou assemblies. Never package replacement engine DLLs.
