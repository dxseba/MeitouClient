# Vital Read (port)

Port of *Vital Read* (workshop 3813852230, the MCM fork): a state badge (icon and short text) on each squad-bar portrait. Screen controls on the
HUD's `*portraits` anchor (one build per portrait shown, anchored to its picture and clipped to it and the bar's page) add a non-pickable image
and label in corners of each marked member's picture. `VitalSystem`, a tick system, publishes each player character's dying, coma, limb and bed facts every 5 ticks.

## Facts

- **Precedence**, first match: dead (none), repair bed `RP`, bed `BD`, playing dead `PD`, dying `DY`, recovery coma `RC`, unconscious `ZZ`,
  crippled leg `CrL`, crippled arm `CrA`, starving `ST`; a crippled arm hides starving. **Verified** (original DLL (decompiled): the overlay
  kind and its label table).
- **Repair bed / bed**: lying in a bed whose building function is 25 (`BF_SKELETON_BED`) / 6 (`BF_BED`), any race. **Verified** (as above).
- **Dying**: not dead or playing dead, and blood-loss trauma, `isProbablyDying`, or blood at or under the KO point (`pointOfNoReturn`); no
  unconscious test. **Verified** (as above; fork README v2). Meitou: `MedicalState.BloodKo`, `MedicalState.KoPoint`, and the health word's Dying
  standing in for `isProbablyDying`, whose rule is not known.
- **Recovery coma**: the coma flag, `!canGetUpWakeUp`, no KO timer, no bleeding or extra blood loss, none of the dying terms; no unconscious
  test. **Verified** (as above). Meitou: `canGetUpWakeUp` as unconscious and `WakesWithin(0)`; extra blood loss has no counterpart.
- **Unconscious**: the medical unconscious flag (or its virtual, or state 4). **Verified** (as above).
- **Crippled leg** `isCrippled && !canIkick` (Meitou: `Crawling || LegsMessedUp`); **crippled arm** `!isLeftArmOk || !isRightArmOk` (Meitou:
  `BothArmsWork`); **starving** `isReallyHungry` (Meitou: hunger below 1). **Verified** (as above) for the tests; the Meitou stand-ins are
  **Observed** equivalents only.
- **Settings** and defaults (`mod-config.defaults.json`; labels from its en_GB catalog): Enabled on, Show icons on, Show text on, Icon size 20 (0
  automatic), Text size 18, Icon corner bottom left, Text corner top right, Horizontal / Vertical margin 0 (shared by icon and text). **Verified**.
  Ranges, the automatic icon size (0.3 of the portrait) and *Coloured text* (off) are Meitou's.
- **Icons**: the defaults are `Kenshi_UI.png` crops (unconscious `45 122 32 32`, playing dead `80 126 38 27`, from the game) and the original's
  own PNGs (`gui/gfx/heart_64px_opt.png`, `death_64px_opt.png`, `starving_64px_opt.png`, `broken-arm_64px_opt.png`, `broken-leg_64px_opt.png`,
  `repair-wrench_64.png`, `bed-plus_64.png`). Looked up as the original does: the path under the mod folder, then the bare name in its `icons/`,
  `gui/gfx/` and folder, then the game's resources. **Verified** (original config and README). The port's last step is the game's own
`data/gui/gfx` (where `Kenshi_UI.png` is); other mods' `gui/gfx` folders, which Kenshi also registers, are not searched. The port does not ship those PNGs:
  after its own folder it looks in the installed original's (its required item; `OriginalFolder`), and a state without its file shows text only.
- **Observed** (2026-11-18): an earlier text-only build loaded. Everything here is unverified in play (placement, icon loading, each trigger).

## Missing

- The original's own icon art (not redistributable from here); drop its `gui/gfx` PNGs into this mod's `gui/gfx` to get them.
- Per-state texture, size and crop overrides (`<state>IconTexture`, `IconSizePx`, `IconCoord*` in `mod-config.json`).
- Debug logging and the probe hotkeys (Ctrl+Alt+F3..F12).
- The text's shadow: a mod node's style has no shadow (the earlier build drew one through the bar's own control).
