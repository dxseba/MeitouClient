# Speed menu (port)

Port of *Speed menu* (workshop 3818592083). A button under the toolbar (the original's yellow diamond, read from the original's installed folder
`gui/selected_speed_diamond.png`, a sibling of this mod's; a **Speeds** text button without it) opens **SELECTED CHARACTER SPEEDS**: per selected
character its name, `x.x mph (y.y max)` and `Current speed: x.x mph`; with nobody selected, "Select characters to see their speeds.". Speeds
(`CharacterHot.MaxSpeed`, the horizontal `Velocity`) are published from the tick four times a second.

- **Verified** (original DLL decompiled): the texts above; mph = u/s / 4.4704; a speed not finite, negative or 10000 mph and over shows 0;
  order by the maximum speed in tenths of an mph (rounded), slowest first, a tie keeping the previous row order (new rows last), then the
  name, then the character; refresh 4 times a second; text colour (0.82, 0.79, 0.70); the button sits beside the time controls
  (`MainBarGUI::update` hook).
- **Observed** (2026-11-17, live Windows sandbox, earlier version): clicking the button lists the selected hero, and the value follows its speed
  in a fight.
- **Missing**: the portrait on each row (the UI API's portrait node draws a record, not a live character); the scroll view (rows that do not fit
  the screen are counted on a last line instead); the button's exact place beside the time controls (no slot there; it sits under the toolbar).
