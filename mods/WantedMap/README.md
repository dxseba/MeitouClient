# Wanted Map (partial port)

Port of *Wanted Map (QoL)* (workshop 3813761812), rebuilt from its Workshop description.

- **Posters**: items named `WANTED: <name>` (the vanilla bounty posters; **Verified** by `meitou-tools record`: ITEM `63883-Dialogue.mod` "WANTED: Shade", item function 13; a full-width colon too) in a player character's
  inventory. Every 60th tick the system matches each poster's name to characters of that name (`UiRegistration.AddMapMarkers` draws them).
- **Markers**: red on the target in the loaded world; blue when it is imprisoned; orange at its platoon's off-screen position (approximate);
  grey where it died, for 24 game hours (deaths seen through `ICharacterDeathObserver`, saved with the world). Only the selected characters'
  posters count, or every character's with *Posters of all characters*.
- **Missing** (partial): the yellow home-town marker of a squad that has not appeared yet, the poster icons in the inventory (tick, lock, 2/3),
  the override file for posters whose name does not match the target, translations; posters inside a backpack are not read (only the
  character's own inventory items). Matching is by name, so a common name can match the wrong character.
- **Unknown**: not checked in game.
