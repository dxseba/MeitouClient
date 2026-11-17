# Level Up Notifications (port)

Port of *[RE_Kenshi] Level Up Notifications* (workshop 3701534964). When a player character's stat passes a whole level (optionally only every
Nth): floating text over the character ("Dexterity 2", rising for *Floating text seconds*) and a notice ("Mu: Dexterity 2"), each optional.
Polls stats every 15th tick; presentation only, not saved or hashed. The floating text is a postfix on `PlayerInterface.BindUi` that projects
the character's head (`PlayerInterface.Project`) into bindings, drawn as labels in `hud.overlay`.

- **Observed** (2026-11-17, live sandbox on origin/master f7ad84a35): a fight posted "Mu: Dexterity 2". **Observed** (2026-11-18, windowed `--sandbox 4 --sandbox-races` on origin/master f7ad84a35, user play): the floating text works.
