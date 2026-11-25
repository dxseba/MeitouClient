# Desolation System - Skills & Combat Overhaul (port)

Port of *Desolation System - Skills & Combat Overhaul* (xuw377, Workshop 3796024680), from its DLLs (`DesolationSystem.dll`,
`DivineGraceUI.dll`, Ghidra decompile), its ini and its in-DLL perk codex.

- **Speed caps** (`ICombatSpeedFilter`): attack cap tiers 1.2 / 2.4 / 3.6 / 4.8 / 9.6 or a custom value; dexterity and attack skill speed a
  capped swing up, then the Ryu style, Athletics Momentum, Martial Burst, Universal Mastery, a polearm's slow and Lightning Counter apply.
- **Base stats**: Strength penetration, Strength Gap Recoil (Defense Master immunity), Hyper Armor, Titan Shockwave and Shockwave Resist,
  Perception Synergy, Last Light, Toughness Reduction, Tough Vitality, Evasion Master, Shadow Step (faster dodges), Phantom Movement (blocks
  back to back), Iron Guard, Lightning Counter, Aquatic Dragon, Wind-Eroded Wall (off by default).
- **Weapon masteries** (above level 60, that weapon in hand): Katana Tear, Saber Guard (parry and riposte), Hackers, Blunt Impact, Heavy
  Onslaught (reach and stagger), Polearm Supremacy (reach and slow), Martial Burst, Crossbow Mastery, Squad Volley, Turret Barrage.
- **Ryu styles** (melee attack 60+): Gale, Balance or Crush for the inspected character, from the Ryu card; saved with the game.
- **Professions and synergies**: Hemostasis Miracle, Master Hemopoiesis, Battlefield Healing, Field Rations, Field Nutrition, Nano-Repair,
  Prosthetic Tuning, Master Porter (capacity and speed), Blade Unity, Steel Body, Grand Fletcher, Master Key, Silent Touch, Phantom Pickpocket
  (V while sneaking), Silent Verdict (the Assassinate card: Sneak or Harvest).
- **Supreme Realms** (player characters, every listed stat 90+): Physical Sanctification (damage -10 %, flesh regeneration, blood floor, no
  injury penalties, limb lock, no KO from vital parts), Universal Weapon Mastery (speed x1.5, damage, parry and reach +50 %, 95 % block, true
  damage), Shadow Sovereign (sure stealth knock-out, unseen while sneaking, shakes off attackers, no crimes), Cybernetic Overlord (own robotic
  upkeep, hits stun robots 15 s, carry robots to recruit), Wilderness Sovereign (its squad's animals: no hunger, half bleeding, +20 % speed and
  stats; beasts stay peaceful; carry beasts to recruit), Heavenly Artifice (faster, better and triple crafting, faster building, no darkness
  penalty).
- **Cards, monitor, config and codex** (HUD overlay): scope, speed cap, realms switch, Ryu style, Assassinate mode, Perk Config, Perk Codex and
  language cards at the top centre; under them the inspected character's active perks with the original's codex text, or the config window
  (every perk switch, a click flips it) or the codex (every perk, marked when the character has it). F8 shows and hides them; Ctrl+F8 cycles
  the scope; V pickpockets; "Switch the Supreme Realms" is unbound (Controls page).
- **Settings**: every ini value the port uses, each perk switch naming its ini key, and the global effect scale, under Mod settings.

## Facts (Verified: decompile of `DesolationSystem.dll`, unless marked)

- Perk table: one 0x58-byte record per perk in `.rdata` (id twice, Chinese and English names and short names, status, notes, Chinese and English
  "requirement|effect" texts). Ids 1-27 (4 revoked, 14 folded into 207), 101-106 (realms), 201-222. The `[PerkSwitch]` keys are `P<id>`, with
  `TOUGHPHP` (220), `RYU_SYS` (216), `ASSN_SYS` (218), `P6_RESIST` (221) and `ASC_*` for the realms' effects; 2 is on, 0 off; P222 is off in the
  release ini. The port reads the table at runtime for the monitor's texts (**Verified** by reading the installed DLL with a script).
- Mastery factor: 0 to 60, ((L - 60) / 40)^2 to 100, 1 + (L - 100) / 25 beyond; a perk's value is v60 + (v100 - v60) x factor (the codex's
  "doubling levels above 60 quadruples extra gains"). Ryu factor: (L - 60) / 40 to 100, 1 + (L - 100) / 60 beyond.
- Speed hook (`calculateAttackOrBlockSpeed`): cap = the tier or custom value clamped to [0.1, 100], rounded to 0.01, default 4.8. An attack at
  1.199 or more with the cap above 1.201 takes max(rate, weapon term x (0.00228 dex + 0.00152 skill + 0.765) x gear speed); then x Ryu speed,
  x Universal 1.5 (`ASC_MART_SWIFT`), x Athletics (P210), x Martial Burst 1.08 + 0.22 x factor (P13, category unarmed), x the polearm slow;
  held to the cap (blocks: 4, or 8 when the cap is 9.59 or more); then x the Lightning Counter burst (1 + 0.05 x min(10, tuning), 1.5 at the
  default). A Wilderness Sovereign's pack animal's attack is x1.2 first.
- Ryu (style 0 Gale, 1 Balance, 2 Crush, factor t): Gale speed 1 + 0.2t, damage 1 + max(-0.9, -0.2t); Balance 1 + 0.1t, 1 + 0.2t; Crush
  1 + max(-0.45, -0.2t), 1 + 0.4t, penetration +0.2t. Gate: melee attack 60.
- `[Thresholds]` MasteryThreshold 60, StrengthGapThreshold 30, RecoilDamagePer10 5, DefenseMastery 105, CounterThreshold 100, HyperArmorStrReq 85,
  ShockwaveStrReq 120, PickpocketMaxDist 35; `[Synergy]` thresholds as in the ini (porter and chef 70).
- The perks' numbers (each codex text, English): Strength 1 % true penetration per 10 (cap 100 %); recoil 5 blunt per arm per 10 Strength gap
  above 30, none above 105 melee defence; Hyper Armor Strength 85 and (Heavy or Blunt 20 or Strength 100), not against a foe 30 Strength
  stronger; Shockwave 30 chest blunt +8 per 10 Strength above 120, cap 75; Shockwave Resist 2 % per point of the first 20 above 60 in toughness
  and dodge, cap 80 %, immune above 95; Katana cut +4/7/16 %, blood loss +1/1.75/4, bleed +0.1/0.2/0.5 (cap 2); Sabre parry +1/2.75/8, riposte
  0.5 % per level above 60 (cap 30 %), 4 + 1 per 10 levels, half chest, half both arms; Hackers penetration +3/6/15 %, +5/8.75/20 % vs
  skeletons; Blunt break +5/10/25 %, head hit 4/6.5/14 % for 12/16.5/30; Heavy stagger +10/20/50 %; Polearm slow 4/6.75/15 % for 5 s, cap 60 %;
  Martial attack speed +8/13.5/30 %; Crossbow 1x at 40, +1x per 30 levels to 100, +0.8x per 10 beyond, humanoids -6 % per 10 melee defence
  (cap 60 %); Squad Volley 0/1/4 % per humanoid squadmate (cap 40 %); Iron Guard limb 2 from 60, vital 6 from 80, +1 per 10 to 100, +0.16 per
  level beyond; Perception +1 melee attack and defence per 10 (cap 10); Last Light (toughness 80) all stats +50 % with a head or torso part below
  25 %; Toughness Reduction 12 % per 10 above 50 (cap 85 %); Tough Vitality part HP +2 % per point 60-100 (base 500+ HP +1 %, 1000+ +0.5 %);
  Evasion dodge +0/5/20; Lightning Counter (dexterity above 75) two foes within 5 s, then a block, x1.5 for 1.2 s; Athletics +3.75 % per 10 to
  100, +5 % per 10 beyond; Aquatic +0.2 Str/Dex/Tough/Perc per swimming level above 60; Hemostasis bleed -0.6 % per medic point (from 50, cap
  95 %); Hemopoiesis blood 0.05/0.0875/0.2 per second; Battlefield Healing 0.08 HP/s per 10 medic on the worst flesh part; Nano-Repair 0.05 HP/s
  per 10 robotics on robotic parts above 25 %, wear 0.2 HP/s at 80 +0.5 per further 10; Field Rations hunger -30 %, 5 % of the meter to
  squadmates below 90 % every 10 s per chef; Field Nutrition +1 per 10 medic above 60 (cap 4) to squadmates' Str/Dex/Tough/Perc; Master Porter
  weight halved; Blade Unity +0.5 % cut and blunt per weapon smithing level above 60; Steel Body -0.5 % cut and blunt per armour smithing level
  above 60 (cap 95 %), harpoons -0.75 per level; Wind-Eroded Wall Strength and toughness 100, melee defence above 80.
- Realm gates as the description lists (Universal: melee attack and defence, martial arts, dodge 90 and any 3 of the 8 weapon skills 90).
  Realms are player-only; `EnableAscension` 1 in the release ini.
- `DivineGraceUI.dll` is the control bar (Scope, Speed Cap, Supreme Master Switch, Ryu, Config cards; language card), the monitor (F8,
  `[UI] ToggleVK=119`) and the config and codex windows; it imports KenshiExtensionPlugin. **Observed** (strings): F6 cycles the scope, 9 + 0
  toggles the realms.
- **Unknown**: not checked in game. **Unknown**: whether the original's Ryu choice can be changed once made (the description says permanent,
  the codex notes say switched explicitly; the port lets the card cycle it).

## Port choices

- Gates read stored stat levels, so one perk's bonus never qualifies another; Meitou keeps medic and hive medic in one stat.
- Perks that hurt someone outside the hit (Shockwave, Blunt's head hit, Katana's blood loss, recoil, riposte) are queued by the hooks and dealt
  at the end of the same tick. Polearm slow and Lightning Counter take effect from the next swing. Stat modifiers and regeneration update every
  10 ticks.
- Heavy Onslaught's longer stagger is a light stagger made heavy when the stagger damage times (1 + bonus) passes the victim's threshold.
  Hemostasis lowers the bleed of the hits the character takes. Master Porter doubles the carrying capacity (the original halves item weights).
- Patched perks read a per-character table the system fills every 10 ticks in its serial step, so they follow the stats with that delay.
- Shadow Step speeds the dodge clip (its play rate over 1 - the cut); Phantom Movement clears the defence, recheck and stagger timers after a
  block. Reach scales the weapon length the attack zone, the attack choice and the approach read. Absolute block re-picks a facing block for a
  blow the game would let through, 95 % of the time. True damage is 10 % of the struck part's maximum as cut, after the hit, through no armour.
- Turret Barrage turns a turret bolt's reaction heavy when its stagger damage times the factor passes the victim's threshold (the gunner: a
  character that used the turrets skill within the last second).
- Pickpocket: the nearest other-faction person within reach, one unit of a random main-inventory item; the chance's terms count from the 80
  gate (**Unknown** whether the original's +2.5 % per 10 counts from 0); a failure is the game's caught theft.
- Silent Verdict: Sneak keeps a landed stealth knock-out's target out 270 s (the memory wipe is not ported: Meitou's aggro has no forget call
  a patch could reach cleanly); Harvest kills it, and its assault books no crime. Silent Touch and Shadow Sovereign book no pending crime at all
  while sneaking (Shadow: ever), which removes both the witness alarm and the bounty.
- Heavenly Artifice's copies go to the bench's output, not the smith's inventory; its build reach (x30) is not ported (see Missing).
- Grand Fletcher raises the held crossbow's bolt bundles' charges (from the item's own base, so it never compounds); bundles keep the raised
  maximum after the perk is lost.
- Keys: F6 cannot be a mod's default, so the scope is Ctrl+F8; "9 + 0" is not a chord Meitou binds, so the realms key is unbound.
- Incompatible with the Attack & Block Speed Uncapped port (`incompatible` in `meitou.json`), as the originals warn about each other.

## Harmony

Patched methods (prefix, postfix, or a transpiler that swaps one call inside the method for the port's wrapper of the same shape, used where
the called method is small enough for the JIT to inline):

- Harmony: `Meitou.Simulation.Bodies.MedicalState.ApplyHit` (prefix): Iron Guard, Physical Sanctification's limb lock.
- Harmony: `Meitou.Simulation.Bodies.StatMultiplier.Compute` (prefix, postfix): Heavenly Artifice's darkness, Toughness Reduction, Physical
  Sanctification's injury immunity.
- Harmony: `Meitou.Simulation.Bodies.StatMultiplier.LimbItemFactor` (postfix): Prosthetic Tuning.
- Harmony: `Meitou.Simulation.Bodies.MedicalState.VitalPartsKeepDown` (postfix) and `Meitou.Simulation.Bodies.MedicalState.CheckPart`
  (transpiler, its `RestartKo` call): Physical Sanctification's KO immunity.
- Harmony: `Meitou.Simulation.Bodies.BodySystem.ActOn` (transpiler, its `TopSpeedNow` call): Master Porter's speed.
- Harmony: `Meitou.Simulation.Combat.CombatSystem.ZoneOf`, `CombatSystem.StartAttack`, `CombatSystem.Move`,
  `Meitou.Simulation.Combat.PursuitSystem.Chase`, `PursuitSystem.SetBand` (transpilers, their `Fighter.WeaponLength` calls): reach.
- Harmony: `Meitou.Simulation.Combat.CombatSystem.StartDefence` (transpiler, its `PlayRate` call): Shadow Step.
- Harmony: `Meitou.Simulation.Combat.CombatSystem.ApplyBlocked` (postfix): Phantom Movement.
- Harmony: `Meitou.Simulation.Locks.Lockpicking.Roll` (prefix) and `Lockpicking.Allowed` (postfix): Master Key.
- Harmony: `Meitou.Simulation.Carrying.CarrySystem.StealthKoChance` (postfix): Shadow Sovereign's sure kill.
- Harmony: `Meitou.Simulation.Carrying.CarrySystem.KnockOutTarget` (postfix): Silent Verdict.
- Harmony: `Meitou.Simulation.Crime.CrimeSystem.SetPending` (prefix): Silent Touch, Shadow Sovereign, Harvest.
- Harmony: `Meitou.Simulation.Aggro.AggroSystem.SenseOne` (postfix): Shadow Sovereign's blue eye.
- Harmony: `Meitou.Simulation.Production.ProductionSystem.CraftStep` (prefix, postfix, and a transpiler on its `CraftBench.CriticalChance`
  call), `ProductionSystem.Finish` (prefix, postfix), `Meitou.Simulation.Construction.ConstructionSystem.BuildStep` (prefix): Heavenly Artifice.
- Not Harmony: `Meitou.Simulation.Dialogue.DialogueSystem.JoinPlayer` (internal) is called by reflection for carry-to-recruit.

## Missing

- Heavenly Artifice's build reach (x30): reach is the walk-to distance of the build task and its stand points; stretching it would let builders
  work from outside walls and through closed buildings, so it is left out.
- Silent Verdict's memory wipe and Silent Touch's "wakes without a grudge": Meitou's aggro memory has no single forget step to patch; the
  knock-out and the absent crime are ported.
- The per-perk effect and requirement scales and the NPC switches (`PerkNpcSwitch`): the global effect scale is ported; per-perk tuning and
  NPC-only switches are UI state of the original's tuning panel, not in the release ini.
