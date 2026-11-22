# Posh Damage Numbers (port)

Port of *Posh Damage Numbers* (workshop 3821781631). The original is a KenshiLua script (`scripts/init/damage_numbers.lua`, settings
`dn_settings.lua`, digits and words drawn from its `DamageNumbers_atlas.png`) plus a Mod Config Menu bridge DLL; rules below are from the
script (**Verified**, read), implemented anew.

- **Source of numbers**: the original samples the flesh and stun of every body part of watched characters (yours, anyone within *FightRange*
  (100 m = 1000 units) of them, anyone fighting them) 20 times a second and shows each drop, so any damage counts. Port: a combat hit observer
  (`ICombatHitObserver`, display only) per landed hit, damage = cut + blunt + pierce after armour times the hit's scale; hits on your people,
  by them, or within *Fight range* of one of them. A victim dead before the hit gets no number; the killing blow does.
- **Number**: "-N" (rounded); ignored under *MinDamage* (1). Ratio = damage / the hit part's maximum health (0.2 when unknown). Colour: your
  people `#FF4040`; else light `#FFF5DB` to medium `#FFD140` up to ratio 0.12, then to heavy `#FF7A1F` by 0.37. Size 0.86 + ratio x 0.7; big
  at ratio 0.3 (lives 1.35 s, else 1.05 s, x *Duration*); rise 1.25 x *Rise*; hits on one target while its number is younger than *MergeWindow*
  (0.22 s) + 0.05 add up (size +0.06, at most 1.75; it pops again from 0.05 s).
- **Animation** (per frame, real time): pop 0.35 to 1.28 in 0.1 s, back to 1 by 0.24 s; white flash fading over 0.12 s (*Flash*); big hits shake
  4 px for 0.18 s (*Shake*); rise easeOutQuad, sideways arc to alternating sides (0.55 + jitter of +-0.175, easeOutCubic); shrinks 12% while
  fading from 62% of its life. Height = clamp(character height on screen x *NumberSize* (0.3), *MinNumberPx* (22), *MaxNumberPx* (50)) at
  1080p, times its size; at most *MaxActive* (60), the oldest recycled.
- **Words**: KNOCKOUT `#96BFFF`, SEVERED `#FF593D` (once per part, never on animals), KILLED `#D91A1A`; 1.7 s, rise 0.9, starting 1.15 above,
  no sideways drift. Port: brought by a hit (knocked out, a part now a stump, dead).
- Drawn in the HUD overlay from bindings set by a postfix on `PlayerInterface.BindUi` (projection by its `Project`). Turn the game's own damage
  floaters off to see one set.
- **Unknown**: not checked in game; the label font's pixel height (taken as 18 at scale 1), so sizes are approximate; whether inline colour tags
  draw in overlay labels.

## Missing

- Damage that is not a combat hit (acid, bleeding, starvation) and the stun share of blunt hits as the original measures them: needs a
  per-part health change feed (a display-side event of flesh/stun lost per part, or `CharacterSnapshot.HealthParts` for every character).
- Healing numbers (*ShowHealing*, off by default, *HealingThreshold* 5 per second): same need.
- The fade-out alpha (a UI label has no per-node alpha), the digit images and Exo 2 font, Russian words (*WordsLanguage*), colour settings,
  *MaxWatched* / *Layer* / *Debug*, words from causes other than a hit.
