# Posh Damage Numbers (port)

Port of *Posh Damage Numbers* (workshop 3821781631). The original is a KenshiLua script (`scripts/init/damage_numbers.lua`, settings
`dn_settings.lua`, digits and words drawn from its `DamageNumbers_atlas.png`) plus a Mod Config Menu bridge DLL; rules below are from the
script (**Verified**, read), implemented anew.

- **Source of numbers** (**Verified**, script `sample`): the original samples the flesh and stun of every body part of watched characters 20
  times a second; per part, flesh lost plus stun gained is its hit, the sum the damage, so any damage counts (hits, acid, a wound worsening).
  Port: the same from the engine's health changes (`IHealthChangeObserver`, display only), summed per character and tick. Watched: your
  people, anyone within *Fight range* (100 m = 1000 units) of them, and anyone in a hit with them in the last 10 s (the original's
  "fighting your people" mark, 10 s; port: from `ICombatHitObserver`). The blow that kills is shown; after that the body is quiet.
- **Number**: "-N" (rounded); ignored under *MinDamage* (1). Ratio = damage / the worst-hit part's maximum health (0.2 when unknown). Colour: your
  people `#FF4040`; else light `#FFF5DB` to medium `#FFD140` up to ratio 0.12, then to heavy `#FF7A1F` by 0.37. Size 0.86 + ratio x 0.7; big
  at ratio 0.3 (lives 1.35 s, else 1.05 s, x *Duration*); rise 1.25 x *Rise*; hits on one target while its number is younger than *MergeWindow*
  (0.22 s) + 0.05 add up (size +0.06, at most 1.75; it pops again from 0.05 s).
- **Animation** (per frame, real time): pop 0.35 to 1.28 in 0.1 s, back to 1 by 0.24 s; white flash fading over 0.12 s (*Flash*); big hits shake
  4 px for 0.18 s (*Shake*); rise easeOutQuad, sideways arc to alternating sides (0.55 + jitter of +-0.175, easeOutCubic); shrinks 12% while
  fading from 62% of its life. Height = clamp(character height on screen x *NumberSize* (0.3), *MinNumberPx* (22), *MaxNumberPx* (50)) at
  1080p, times its size; at most *MaxActive* (60), the oldest recycled.
- **Words**: KNOCKOUT `#96BFFF`, SEVERED `#FF593D` (once per part, never on animals), KILLED `#D91A1A`; 1.7 s, rise 0.9, starting 1.15 above,
  no sideways drift. Port: KNOCKOUT when a health change finds the character newly unconscious, SEVERED when a changed part is a stump,
  KILLED from the deaths observer (any cause).
- **Healing** (*Show healing*, off; *Healing threshold* 5): flesh regained summed per character over one second of real time, a green
  `#6EE678` "+N" from the threshold. **Colours** are Mod settings (free text `#RRGGBB`, the original's `Colour*` keys; invalid: default).
- Laid out once a frame (`AddFrame`, projection by `ModFrame.Project`) and drawn in the HUD overlay (`Animated`, `TextScale`,
  `TextColour`, `Opacity` for the fade: in over 0.05 s, out by easeInQuad from 62% of its life). Turn the game's own damage
  floaters off to see one set.
- **Unknown**: not checked in game; the label font's pixel height (taken as 18 at scale 1), so sizes are approximate.

## Missing

- The digit images and Exo 2 font, Russian words (*WordsLanguage*), *MaxWatched* / *Layer* / *Debug*; KNOCKOUT from a fall unconscious
  without a health change.
