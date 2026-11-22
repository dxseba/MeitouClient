# Posh Damage Numbers (partial port)

Port of *Posh Damage Numbers* (workshop 3821781631), rebuilt from its Workshop description.

- **Numbers**: each landed hit from the display-only hit feed (`ModContext.AddCombatHitFeed`; melee, bolts, executions) shows the damage it
  dealt after armour (`CombatHit.Damage`: cut + blunt + pierce, times its scale) over the victim; hits on one target within *Merge window*
  (0.6 s) add into one growing number. Numbers follow the character and rise and fade over *Seconds* (1.4).
- **Colours**: warm white under 15, gold under 40, orange above; red when the victim is one of yours.
- **Flash** (*Flash on big hits*): a single hit of 40 or more on a living victim throws the parry sparks (EFFECT `small- sparks1`, base
  game, system `kenshi_sparks`; **Verified** with `meitou-tools record`) tinted warm white on its chest for 0.12 s (`ModContext.PlayEffect`).
- **Words**: `KILLED` when the victim was alive before the hit and is dead now, `KNOCKOUT` when it was up and is unconscious (from the snapshot).
- Drawn in the HUD overlay by a postfix on `PlayerInterface.BindUi` and `PlayerInterface.Project`, as Level Up Notify. Turn the game's own
  damage floaters off to see one set.
- **Missing** (partial): the pop, shake and arc animation of the numbers (labels rise in a straight line), SEVERED, healing numbers, the stun-only
  numbers of blunt hits, damage from acid and other non-hit sources (the feed carries combat hits only), the 27 MCM settings, the font,
  scaling with the camera zoom, translations.
- **Unknown**: not checked in game.
