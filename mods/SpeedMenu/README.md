# Speed menu (port)

Port of *Speed menu* (workshop 3818592083). A button beside the HUD's time controls (`AddScreenControls(UiScreens.Hud, "TimeMoneyPanel",
Left)`; the original's yellow diamond, read from the original's installed folder `gui/selected_speed_diamond.png`, a sibling of this mod's; a
**Speeds** text button without it) opens **SELECTED CHARACTER SPEEDS** above them: a scroll view (`UiNodeKind.ScrollView`) with, per selected
character, its name, `x.x mph (y.y max)` and `Current speed: x.x mph`; with nobody selected, "Select characters to see their speeds.". Speeds
(`CharacterHot.MaxSpeed`, the horizontal `Velocity`) are published from the tick four times a second and sorted for the frame's selection
(`AddFrame`).

- **Verified** (original DLL decompiled): the texts above; mph = u/s / 4.4704; a speed not finite, negative or 10000 mph and over shows 0;
  order by the maximum speed in tenths of an mph (rounded), slowest first, a tie keeping the previous row order (new rows last), then the
  name, then the character; refresh 4 times a second; text colour (0.82, 0.79, 0.70); the button 30 x 30 at the main bar anchor's corner
  (-39, +52), the panel 330 x 450 (`Kenshi_GenericTextBoxSkin`) at (-365, -454), each coordinate kept at 0 or more (`MainBarGUI::update` hook).
- **Observed** (2026-11-17, live Windows sandbox, earlier version): clicking the button lists the selected hero, and the value follows its speed
  in a fight. The new placement and scroll view are not checked in game.
- **Unknown**: which main bar widget the original's offsets are from (its `MainBarGUI` +0x150); `TimeMoneyPanel` is taken.
- **Missing**: the portrait on each row (the UI API's portrait node draws a record, not a live character).
