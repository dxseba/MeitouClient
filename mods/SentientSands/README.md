# Sentient Sands Rebirth

Port of the in-game side of *Sentient Sands Rebirth* (Workshop 3819590113), from its DLL (`SentientSands.dll`, Ghidra decompile: strings and
the request builders) and its server's Python sources (read as text, never run). The original is a required item: its
`SentientSandsRebirth.mod` and its server. **The port never starts the server**: the player starts the original's server themselves, and only
then does anything answer.

- **Chat** (`\`): a window on the NPC nearest your selected character (within 300); Enter or Send posts the line to `/chat`; the replies come
  back as speech bubbles and in the window. The NPC stops and stays while you talk.
- **Barter table** (Barter in the window): click the NPC's goods and yours onto the table, set cats both ways; every line then carries the
  table, and an agreed deal opens the **offer popup** (Accept checks each side still holds its part, Decline keeps talking).
- **Trade** opens the game's trade window with the NPC.
- **Actions** of the replies and deals: `JOIN_PARTY` (with its fee), `RELEASE_PRISONER`, `HIRE`, `END_HIRE`, `EXPIRE_HIRE`, `GIVE_CATS`,
  `TAKE_CATS`, `ADD_CATS`, `GIVE_ITEM`, `SELL_ITEM`, `TAKE_ITEM`, `BUY_ITEM`, `SPAWN_ITEM`, `RAISE_RELATION`, `ATTACK`, `FIRST_AID`,
  `TRAVEL_TO_TARGET_TOWN(_FAST)`, `RAID_TOWN`.
- **SSR hub** (F8): Characters (the Dialogue Library: profile, conversation log, favourites, bio), Journal (pages, search, new, edit, save,
  delete), Quests (open and done jobs, abandon), Events (event text, rumour editor: edit, generate, keep, delete; add and delete events) and
  Server (the dashboard's address).
- **Radiant talks**: every 10 minutes of unpaused play your characters near the lead and the NPCs near them are sent to `/radiant`; the server
  picks who talks and plays the lines as bubbles.
- **Reports**: every request carries the game events (your faction's attacks; knockouts, wake-ups, deaths and imprisonments near you), the towns
  an override changed and your outposts; the server's `REPORT:` asks for one; renames of your characters go to `/squad_rename`.

## Facts

- **Verified** (server `core/pipe.py`, `chat/routes.py`, `chat/offers.py`): one pipe message per connection to `\\.\pipe\SentientSands`, which
  the plugin serves; prefixes `NPC_SAY:`, `NPC_THINK:`, `PLAYER_SAY:`, `NPC_ACTION:`, `NOTIFY:`, `SET_CONFIG:`, `REPORT:`, `APPLY_TRANSLATION:`,
  else `CMD:`. A label is `Name|key`, optionally `>holder key`; actions `[ACTION: NAME: args]`, items as `name: count`. The offer command is
  `CMD: OFFER: ` then id, keys, name, text, `CHECK:`, `RESTOCK:`, `BOX:` and `DEAL:` lines; `/offer` takes `id`, `answer` (accept, decline,
  failed, show), `npc` and the game time. The barter window sends `barter: {squad, deal: {give, take, give_cats, take_cats}}`.
- **Verified** (server `core/routes.py`): the hub's routes and their JSON (`/characters`, `/history`, `/favorite`, `/read_bio`, `/journal*`,
  `/quests*`, `/events*`, rumours); writes send back the listing's `campaign`. `/report` and every request take `events`, `changed_towns`,
  `unique_states`, `player_towns`; events are `attack` (target, attacker), `knockout` / `up` (id, party), `death` / `imprisonment` (party),
  with `day`, `hour`, `minute` (`core/world_events.py`).
- **Verified** (decompile): `/radiant` is `{"participants", "npcs", "player_context", "events", "changed_towns", "unique_states",
  "player_towns"}`; `SET_CONFIG` keys `g_enableRadiant`, `g_radiantIntervalSeconds`, `g_proximityRadius`; `RAID_TOWN` and
  `TRAVEL_TO_TARGET_TOWN` give the NPC AI task 18 and 53 toward the named town. Server defaults: radiant on, 10 minutes, talk radius 150.
- **Unknown**: the original's `npc_id` scheme (`u:` uniques, `h:` others); the port sends `h:` with its own key, so uniques get no canon
  profile. The `squad_jobs` of a context (the AI tasks of the squad's package) are not sent, so the server cannot tell a bar crowd and NPC-only
  radiant talks rarely start. `unique_states` is sent as none.

## Port

- No Harmony patches: every gap closed through existing APIs or reflection on Meitou's internals: `Meitou.Simulation.Trading.OpenTradeNow`
  (DA_TRADE's command), `Meitou.Simulation.Dialogue.DialogueSystem.JoinPlayer`.
- Travel and raids: `world.GiveOrder` with task 53 / 18 and the town when the game takes it as an order; else the NPC's platoon is held and sent
  there (`PopulationSystem.HoldPlatoon`, `SendPlatoon`) and the NPC walks there. First aid: `FirstAidOrder` on the player's character. Hire:
  `StartContract` (the hire package setting, Bodyguard by default). Release: the cage unlocks (`FixtureRegistry.SetLocked`), `GetOutOrder`,
  `FixtureRegistry.FreeSlave`.
- Items move between inventories by shown name (a shop's stock for `SELL_ITEM` / `BUY_ITEM`); `SPAWN_ITEM` makes the record of that name.
- Events come from `ICharacterDeathObserver`, `ICombatHitObserver` (one attack per pair and game hour) and a look every second at the
  characters near the lead; nothing they keep enters the world's state.

## Missing

Nothing that needs an engine change. Not ported: the hub's settings page (the server's own dashboard does it), the cheat window, and the
original's UI translation (`APPLY_TRANSLATION`; the port's texts go through the game's lookup). **Unknown**: not checked in game.
