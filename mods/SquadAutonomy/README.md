# Squad Autonomy (partial port)

Port of *Squad Autonomy* (Au2942, workshop 3800729618, GPL-3.0; [Au2942/KenshiPlugins](https://github.com/Au2942/KenshiPlugins)), on Meitou's
autonomous player squads ([docs/modding.md](../../docs/modding.md), "Player squads on AI packages").

- **Window** (the AUT button on the top bar): one player squad at a time.
  - **Autonomy** On/Off: `SetSquadAutonomy(squad, on, Owner: mod id)`, so removing the mod gives the squad back to the player.
  - **Home**: *Set* to the building the HUD inspects (a town building's key or a player-built building's id) or *Clear*: `SetSquadHome`.
  - **Work package**, **Rest package** and **work hours**: inside the hours the squad runs the work package, outside them the rest package (none:
    the work package all day). The system sends `ChangeAi` when the running package is not the wanted one (again after 300 ticks if it does not
    show). The plan is saved with the world (mod sidecar); after a load a plan follows its squad by name if the squad id changed.
- **Packages**: the picker lists the AI_PACKAGE records named in `packages.txt` or tagged `<SquadAutonomy>` (the original's two rules) under
  *Listed*, and every package under *All*. The original's own tagged packages come with its `.mod`, which is not shipped here. **Observed** (2026-11-19, headless, 152 data mods): 9 listed of 279.
- **Missing** (partial): priorities between several packages (Meitou's `ChangeAi` sets one); the original's labour, combat, medic and rescue
  options, the rest-until-healed and sleep thresholds, paid beds and shopping; gate guarding, closing gates and manning turrets (the engine has no
  outpost gate or turret goals yet); a work building separate from the home; the squad-screen button and button layout settings.
- **Unknown**: not checked in game; squad ids across a save and load (hence the name fallback). **Observed** (2026-11-19, headless sandbox fight): loads, 3000 ticks without a fault.
