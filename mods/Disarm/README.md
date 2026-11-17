# Disarm (port)

Port of *Disarm* (workshop 3759461630), first part only: right-click an unconscious character not of yours and choose **Disarm**; if a selected
squad member is within 40 units its hip and back weapons drop beside it. Not theft: no crime or relation change. Not ported: stealth disarms of
sleeping or awake targets with skill odds, XP, kicking weapons. A row through the interface's character-menu slot (`UiRegistration.AddCharacterMenuVerbs`), an order handled in the tick, the engine's
equipment refresh by reflection. **Observed** (2026-11-17, live Windows sandbox on origin/master f7ad84a35, driven by input): the held right-click menu on a knocked-out Stone showed DISARM; choosing it posted "Stone has been disarmed." and her staff lay on the ground.
