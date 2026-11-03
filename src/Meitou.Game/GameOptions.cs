using System.Globalization;

namespace Meitou.Game;

/// <summary>Game-only options; everything else is the world options shared with the viewer (<see cref="WorldOptions"/>).</summary>
sealed class GameOptions
{
    public bool TitlePreview;
    public int? FpsLimit;
    public ulong Seed;
    /// <summary><c>--seed</c> was given: a new game from the menu keeps it instead of rolling a fresh one.</summary>
    public bool SeedGiven;
    public bool? VSync;
    public Meitou.Data.Ui.UiMode? UiMode;
    /// <summary><c>--low-end</c> (also passed on to WorldOptions): the Faithful UI for this run, whatever <c>--ui</c> or the saved mode says.</summary>
    public bool LowEnd;
    public bool UiShowcase;
    /// <summary>With <c>--screenshot</c>: one HUD/panel to open for the picture (null: the HUD only).</summary>
    public string? UiPanel;
    /// <summary>With <c>--screenshot</c>: the settings menu's Graphics page in a set of states (docs/native-ui.md "Settings"), one picture each.</summary>
    public bool SettingsShowcase;
    /// <summary>With <c>--screenshot</c>: control ticks (and their real time of simulation, at speed 1) run before the picture, with no input.</summary>
    public int Ticks;
    public bool NoPopulation, NewGame, ListStarts, DataCheck;
    /// <summary>The animation sandbox (<c>--sandbox [n]</c>): 0 off, else the number of characters (1 or 2) on a flat wireframe floor.</summary>
    public int Sandbox;
    /// <summary>Flat scenes skip map/weather placers; combat particles still draw unless --no-particles was explicit.</summary>
    public bool FlatCombatParticles = true;
    /// <summary>The CHARACTER record (name or id) the sandbox spawns (null: the default new-game start's first squad member).</summary>
    public string? SandboxCharacter, SandboxPart, SandboxHitSide, SandboxHitMechanism;
    public bool SandboxBare;
    public bool SandboxDummy, SandboxSever, SandboxClothed, SandboxTorso, SandboxFemale, SandboxBehead, SandboxSevereOnly, SandboxBisect;
    public float? SandboxYaw, SandboxPower, SandboxHitAngle;
    /// <summary>Set every stored attribute and skill of the sandbox hero before the first tick.</summary>
    public float? SandboxStats;
    public float? SandboxBrutality;
    public string? SandboxHeadCut;
    public int? SandboxHeadWound, SandboxCutDirection, SandboxHitWeaponCategory;
    public ulong? SandboxBisectVariant;
    public Meitou.Simulation.Wounds.TorsoBisectionStyle SandboxBisectStyle;
    public float SandboxHitDamage = 30;
    public int? SandboxSeverLevel;
    public float SandboxZoom = 30;
    /// <summary>Camera target height above the sandbox character, for wound close-ups.</summary>
    public float SandboxHeight;
    /// <summary>The sandbox hero's start state, comma-separated: walk, run or free; a fixed speed in units/s; patrol (a 400-unit line), for pictures.</summary>
    public string? SandboxMode;
    /// <summary><c>--sandbox-races</c>: one character of every person race (a CHARACTER record listing only that race) in rings round the pair, whatever n is.</summary>
    public bool SandboxRaces;
    /// <summary><c>--terrain-sandbox [x,z]</c>: the empty world round a flat spot whose ground is a field of tiles, one per ground texture (GroundRelief.FieldCentre).</summary>
    public (float X, float Z)? TerrainSandbox;
    /// <summary>The terrain sandbox's default spot: the flattest 2000-unit square of the heightmap found (16 cm of relief, 0.25% slope; docs/render-terrain.md "Ground relief").</summary>
    public static readonly (float X, float Z) TerrainSandboxSpot = (38376, -8280);
    /// <summary><c>--terrain-sandbox cliffs</c>: the field of cliff textures on a steep face instead.</summary>
    public bool TerrainSandboxCliffs;
    /// <summary>The cliff field's default spot: an even 45-degree face about 1 km across facing −Z, the steadiest found (plane-fit residual 2.7 units over 800).</summary>
    public static readonly (float X, float Z) CliffSandboxSpot = (92164, 123124);
    /// <summary>The looping duel for trailers (<c>--duel [x,z]</c>, docs/simulation.md "Duel"): two Samurai Heavy fight on rails at sunset, the same fight every lap.</summary>
    public bool Duel;
    /// <summary>Where the duel is fought (null: the built-in dune crest).</summary>
    public (float X, float Z)? DuelAt;
    /// <summary>The battle (<c>--battle n unit m unit</c>, docs/simulation.md "Battle"): two groups of NPCs, each a count of one CHARACTER or ANIMAL_CHARACTER record (name or id).</summary>
    public (int Count, string Unit)[]? Battle;
    /// <summary>The optional fifth <c>--battle</c> value: each block in this many rows (the back row takes the rest), or null for rows of 25.</summary>
    public int? BattleRows;
    /// <summary><c>--battle-width a[,b]</c>: fighters per row of each side (one value: both), over <see cref="BattleRows"/>; null for a side that keeps its rows.</summary>
    public int?[] BattleWidth = [null, null];
    /// <summary><c>--battle-map sandbox</c>: the battle is fought on the sandbox's flat floor instead of the map.</summary>
    public bool BattleFlat;
    /// <summary><c>--battle-map x,z</c>: where on the map the battle is fought (null: the plateau, <see cref="GameHost.BattleSpot"/>).</summary>
    public (float X, float Z)? BattleAt;
    /// <summary><c>--battle-no-fear</c>: nobody runs away, every fighter fights until it is down (no flee system).</summary>
    public bool BattleNoFear;
    /// <summary>The start to play (null = the default); with <c>--move-to</c> the picture shows a selected squad walking.</summary>
    public string? NewGameName;
    public (float X, float Z)? MoveTo;
    /// <summary>Interactive run that closes itself after this many seconds and prints the frame rate (an unattended smoke test).</summary>
    public double? QuitAfter;
    /// <summary>A save folder (Meitou's or the original game's) started in instead of a new world, loaded as the game menu's Load does (<c>--load</c>).</summary>
    public string? Load;
    /// <summary><c>--play-bench &lt;dir&gt;</c>: the interactive game driven by a script, every frame measured, the results written there (GameHost.PlayBench.cs).</summary>
    public string? PlayBench;
    /// <summary><c>--no-precompile</c>: the methods are compiled on their first call only, not ahead on a background thread (JitWarmup).</summary>
    public bool NoPrecompile;
    /// <summary>The renderer for the whole frame: <c>vulkan</c> (the default) or <c>metal</c> (macOS: the scene, HUD and faithful panels, the port's current slice).</summary>
    public string Renderer = "vulkan";
    /// <summary>With <c>--screenshot</c>: a staged scene to check behaviour offscreen (<c>--scene</c>: fight, turret, bed, acid, menu, bisect, guts; GameHost.Scenes.cs).</summary>
    public string? Scene;
    /// <summary>The scene's frames and the control ticks between them (<c>--scene-frames</c>, <c>--scene-every</c>).</summary>
    public int SceneFrames = 8, SceneEvery = 30;
    /// <summary>The CHARACTER record (name or id) the fight and turret scenes spawn as enemies (<c>--scene-npc</c>).</summary>
    public string SceneNpc = "Bandit";
    /// <summary>The town (name, part of it) the turret scene moves the squad to first (<c>--scene-town</c>; null: the nearest turret to where the save is).</summary>
    public string? SceneTown;

    public const string Usage = """
        meitou [where] [options]     boots into the world with the Kenshi camera (default --town "The Hub")
          --fps-limit <n>            frame limit when vsync is off (default 150 from meitou.user.json; 0 = unlimited)
          --vsync / --no-vsync       vsync (default off)
          --seed <n>                 the world seed (default 0)
          --ui <faithful|meitou>      UI mode, independent of graphics; also available in the settings (default meitou; --low-end: faithful)
          --ui-showcase              with --screenshot: capture HUD, inventory, stats, map, menus, a trade with the nearest trader and a conversation in either UI mode
          --ui-panel <name>          with --screenshot: open one HUD/panel for the picture (hud, hud-expanded, inventory, stats, overview, menu, title, ...)
          --settings-showcase        with --screenshot: capture the settings menu's Graphics page (every tab, a preset with changes, the preset list, Save as, Low quality, Faithful)
          --title-preview            with --screenshot: capture the staged Meitou title scene
          --no-population            no town residents or movement (an empty world)
          --new-game [start]         a new game as the NEW_GAME_STARTOFF start (default Wanderer): the player squad at its town, camera on it
          --list-starts              print the available starts and exit
          --data-check               print every game data and level file problem of the load order (missing mod, changed record type...) and exit: 0 none, 1 some
          --sandbox [n]              animation sandbox: no world, a flat 1000x1000 wireframe floor (lines every 10, bold every 100) with 1 character (n = 2: two facing each other 30 units apart; more: the rest idle in rings round them, up to 1000); Z/X/C walk/run/free, [ ] fixed speed, P patrol, G fight, Y follow; works with --screenshot --ticks
          --sandbox-character <name> the CHARACTER or ANIMAL_CHARACTER record the sandbox spawns (default: the first member of the default new-game start)
          --sandbox-brutality <n>    hero Brutality level 0..100 (otherwise sandbox-stats, capped at 100)
          --sandbox-stats <n>        set native hero attributes and skills to n (including values above 100); Brutality caps at 100
          --sandbox-mode <list>      the sandbox hero's start, comma-separated: walk, run or free, a fixed speed in u/s, patrol (the 400-unit patrol), fight (the G key, with --sandbox 2), e.g. run,patrol (with --move-to or --screenshot)
          --sandbox-bisect          with --sandbox 2: force one lethal torso cleave at tick 120 (chest; --sandbox-part stomach targets the abdomen)
          --sandbox-bisect-style <kind> auto (default), transverse, vertical, diagonal, or oblique; oblique crosses the trunk toward a shoulder
          --sandbox-bisect-variant <n> repeat a torso cut's angle and height from this visual seed
          --sandbox-severe-only     with --sandbox-dummy: hide generic wound marks and falling jaw to inspect permanent head state without a wound mask
          --sandbox-head-cut <form>  force one lethal head cut: auto, neck, transverse, oblique, vertical, split
          --sandbox-head-wound <n>   with --sandbox-dummy: fourth heavy head hit opens 0 jaw, 1 scalp, or 2 broken skull
          --sandbox-cut-direction <n> with --sandbox-dummy: fixed blade axis 1 down, 2 left, 3 right
          --sandbox-hit-weapon-category <0..8> with --sandbox-dummy: actual FCS weapon skill category for visual comparison
          --sandbox-hit-mechanism <kind> with --sandbox-dummy: slash (default), thrust, or crush for visual comparison
          --sandbox-hit-angle <deg> with --sandbox 2: attacker approach around the victim (0 front, 90 right, 180 back); also fixes dummy impact direction
          --sandbox-hit-side <side>  with --sandbox-dummy: fixed victim-local incoming side front, left, right, or back
          --sandbox-hit-damage <n>   with --sandbox-dummy: every third hit cuts for n (default 30); head profiles enlarge at 35
          --sandbox-races            the sandbox adds one character of every person race (the first CHARACTER record listing only that race) in rings round the pair, whatever n is; the log lists them
          --terrain-sandbox [cliffs] [x,z]  the empty world round a flat spot (default the flattest found) whose ground is a field of 200-unit tiles, one per ground texture
                                     (cliffs: 100-unit tiles of every cliff texture on a 45-degree face),
                                     drawn with the whole ground relief (Tab sliders live); the tile under the camera is named at the top left
          --duel [x,z]               trailer scene: two helmeted Samurai duel at sunset in the desert dunes (or at x,z), on rails: every lap is the same fight, the clock, weather and clouds start again with it,
                                     and it restarts when a cinema flight is played or recorded (Backspace restarts it); the time of day is held (22:00, or --time) and the weather clear (or --weather)
          --battle <n> <unit> <m> <unit> [rows]  n NPCs of one CHARACTER or ANIMAL_CHARACTER record (name or id) fight m of another (at least 1 each), in two blocks facing each other
                                     (rows of 25, or each block in that many rows, the back row taking the rest; bigger races stand further apart);
                                     no towns, no aggro: a standing fighter without a target goes for the nearest standing enemy; Backspace starts the same fight again
          --battle-map <map>         where the battle is fought: plateau (default), mesa (the --duel spot in the dunes), fog (a plateau in the Fog Islands), sandbox (the flat floor) or x,z on the map (no building collision there)
          --battle-width <a>[,<b>]   fighters per row of the first side and of the second (one value: both), instead of the rows
          --battle-no-fear           nobody in the battle runs away: every fighter fights until it is down
          --move-to <x> <z>        with --new-game: select the squad and order it to walk (for screenshots; the picture is taken where the leader got to)
          --ticks <n>                with --screenshot: run n control ticks (and the same real time of the simulation, at speed 1) before the picture
          --quit-after <s>           close after s seconds and print the frame rate (smoke test)
          --scene <name>             with --screenshot (and --load): stage a scene and save a frame every --scene-every ticks (default 30), --scene-frames of them (default 8):
                                     fight (enemies of --scene-npc spawned beside the leader and attacked), turret (the nearest town turret, enemies spawned in its range),
                                     bed (the leader sleeps in the nearest bed), acid (acid rain forced on the characters), bisect (with --sandbox 2 --sandbox-bisect: visible impact and falling halves),
                                     guts (with --sandbox 2 --sandbox-bisect: physical bowel close-up), weapon-blood (ordinary sandbox combat, logs item blood), menu (the context menus of the nearest character,
                                     door, container and cage, one frame each), torso-carry (with --sandbox 3 --sandbox-bisect: independent pickups and put-downs), carry-reference (with --sandbox 2 --sandbox-bisect: intact lift then each half), downed-attack (with --sandbox 2: the real body-part attack menu against a living knocked-out enemy); the log prints the leader's state each frame; --scene-town <name> moves the squad to that town first (turret)
          --no-precompile            compile methods on their first call only (by default a background thread compiles the game's methods ahead)
          --play-bench <dir>         with --load (or a new game): the game driven by a script (idle, camera orbit, zoom, pan, a long travel, time x2 and x5, talk, trade,
                                     a container, doors, a pick-up, path requests, a walk, a fight, loot, the panels, a horizon view, the overview), every frame measured
                                     (frame, update, world tick steps and systems, render stages, GPU stages, collections); writes frames.csv and summary.md there and
                                     quits; frame limit and vsync off; MEITOU_BENCH_ONLY=a,b keeps those segments
          --load <save folder>       start in a save, Meitou's or the original game's (%LOCALAPPDATA%\kenshi\save\<name>), loaded as the game menu's Load does; works with --screenshot
          --renderer <vulkan|metal>  the renderer for the whole frame: vulkan (the default) or metal (macOS: the scene, HUD and faithful panels, the port's current slice)
          --yaw/--pitch/--distance   start view: heading, pitch above the horizon and boom (Kenshi: 30 degrees, boom 150; clamped to 10..2000)
          world options as meitou-viewer --world: --at, --zone, --town, --radius, --time, --screenshot, --size, --no-foliage, ...
        Keys: W/A/S/D or arrows move (or the pointer at a screen edge), Q/E rotate, Comma/Period tilt, wheel or Home/End zoom, PageUp/PageDown floor up/down (lens in the free camera), middle drag or Ctrl orbit, left click or drag selects, right click moves (shift queues), 1..9, 0 / ` select, [ / ] previous / next squad member, R stops,
          numpad 0..8 Block, Hold, Passive, Ranged, Sneak, Taunt, run speed, Medic (also H), Rescue (also J), L rest, K put down, I/C/M/T/Y windows, Tab next squad (all rebindable),
          ; free camera (R/F up/down), Space pause, F2/F3/F4 speed 1x/2x/5x, F8 or PrintScreen screenshot, Ctrl+/ Metal GPU capture (MTL_CAPTURE_ENABLED=1),
          F5 quicksave, F9 quickload (Meitou's own slot under %LOCALAPPDATA%\Meitou\save), Ctrl+F9 fps counter, F10 debug menu (teleport, spawn, items, money, heal, max skills, damage; Metal: settings), F11 statistics, F12 profiler (gpu, cpu, off), Esc game menu (save, load, exit). Shift+F1 Meitou on / off, Shift+F2 skin scattering on / off, Alt+Enter full screen,
          F6 cinematic camera editor (numpad keys as in the viewer; game-shots.json; Home the lens also in the free camera), F7 hide the interface,
          Backspace restarts the --duel lap or the --battle, PageUp/PageDown narrow/widen its lens, Home resets it.
        Settings (frame limit, vsync, full screen (--windowed for one run), tick rate (tickRate), simulation threads (simThreads), the settings sliders, key bindings) are kept in meitou.user.json.
        """;

    /// <summary>Takes the game's own options out of <paramref name="args"/> and returns the rest.</summary>
    public static (GameOptions, string[]) Split(string[] args)
    {
        var g = new GameOptions();
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--ui-showcase": g.UiShowcase = true; break;
                case "--ui-panel": g.UiPanel = Next(); break;
                case "--settings-showcase": g.SettingsShowcase = true; break;
                case "--fps-limit": g.FpsLimit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--seed": g.Seed = ulong.Parse(Next(), CultureInfo.InvariantCulture); g.SeedGiven = true; break;
                case "--ui":
                    g.UiMode = Next().ToLowerInvariant() switch { "faithful" => Meitou.Data.Ui.UiMode.Faithful, "meitou" => Meitou.Data.Ui.UiMode.Meitou, _ => throw new ArgumentException("--ui must be faithful or meitou") };
                    break;
                case "--vsync": g.VSync = true; break;
                case "--no-vsync": g.VSync = false; break;
                case "--ticks": g.Ticks = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--no-population": g.NoPopulation = true; break;
                case "--title-preview": g.TitlePreview = true; g.UiMode = Meitou.Data.Ui.UiMode.Meitou; break;
                case "--new-game":
                    g.NewGame = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) g.NewGameName = args[++i];
                    break;
                case "--list-starts": g.ListStarts = true; break;
                case "--data-check": g.DataCheck = true; break;
                case "--sandbox":
                    g.Sandbox = i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? Math.Clamp(count, 1, 1000) : 1;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out _)) i++;
                    break;
                case "--sandbox-character": g.SandboxCharacter = Next(); break;
                case "--sandbox-bare": g.SandboxBare = true; break;
                case "--sandbox-yaw": g.SandboxYaw = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--sandbox-clothed": g.SandboxClothed = true; break;
                case "--sandbox-torso": g.SandboxTorso = true; break;
                case "--sandbox-female": g.SandboxFemale = true; break;
                case "--sandbox-cut-direction":
                    g.SandboxCutDirection = int.Parse(Next());
                    if (g.SandboxCutDirection is < 1 or > 3) throw new ArgumentException("--sandbox-cut-direction must be 1 (down), 2 (left), or 3 (right)");
                    break;
                case "--sandbox-hit-weapon-category":
                    g.SandboxHitWeaponCategory = int.Parse(Next(), CultureInfo.InvariantCulture);
                    if (g.SandboxHitWeaponCategory is < 0 or > 8) throw new ArgumentException("--sandbox-hit-weapon-category must be 0..8");
                    break;
                case "--sandbox-hit-mechanism":
                    g.SandboxHitMechanism = Next().ToLowerInvariant();
                    if (g.SandboxHitMechanism is not ("slash" or "thrust" or "crush")) throw new ArgumentException("--sandbox-hit-mechanism must be slash, thrust, or crush");
                    break;
                case "--sandbox-hit-angle":
                    float approach = float.Parse(Next(), CultureInfo.InvariantCulture);
                    if (!float.IsFinite(approach)) throw new ArgumentException("--sandbox-hit-angle must be finite degrees");
                    g.SandboxHitAngle = MathF.IEEERemainder(approach, 360);
                    break;
                case "--sandbox-hit-side":
                    g.SandboxHitSide = Next().ToLowerInvariant();
                    if (g.SandboxHitSide is not ("front" or "left" or "right" or "back")) throw new ArgumentException("--sandbox-hit-side must be front, left, right, or back");
                    break;
                case "--sandbox-hit-damage":
                    g.SandboxHitDamage = float.Parse(Next(), CultureInfo.InvariantCulture);
                    if (!float.IsFinite(g.SandboxHitDamage) || g.SandboxHitDamage <= 0) throw new ArgumentException("--sandbox-hit-damage must be positive and finite");
                    break;
                case "--sandbox-severe-only": g.SandboxSevereOnly = true; break;
                case "--sandbox-head-cut":
                    g.SandboxHeadCut = Next().ToLowerInvariant();
                    if (g.SandboxHeadCut is not ("auto" or "neck" or "transverse" or "oblique" or "vertical" or "split"))
                        throw new ArgumentException("--sandbox-head-cut must be auto, neck, transverse, oblique, vertical, or split");
                    g.SandboxDummy = g.SandboxBare = true;
                    break;
                case "--sandbox-head-wound": g.SandboxHeadWound = int.Parse(Next()); break;
                case "--sandbox-bisect-style":
                    g.SandboxBisectStyle = Next().ToLowerInvariant() switch
                    {
                        "auto" => Meitou.Simulation.Wounds.TorsoBisectionStyle.Auto,
                        "transverse" => Meitou.Simulation.Wounds.TorsoBisectionStyle.Transverse,
                        "vertical" => Meitou.Simulation.Wounds.TorsoBisectionStyle.Vertical,
                        "diagonal" => Meitou.Simulation.Wounds.TorsoBisectionStyle.Diagonal,
                        "oblique" => Meitou.Simulation.Wounds.TorsoBisectionStyle.Oblique,
                        _ => throw new ArgumentException("--sandbox-bisect-style must be auto, transverse, vertical, diagonal, or oblique"),
                    };
                    break;
                case "--sandbox-bisect-variant": g.SandboxBisectVariant = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                case "--sandbox-bisect": g.SandboxBisect = g.SandboxDummy = g.SandboxBare = true; break;
                case "--sandbox-behead": g.SandboxBehead = true; break;
                case "--sandbox-sever-level": g.SandboxSever = true; g.SandboxSeverLevel = int.Parse(Next()); break;
                case "--sandbox-brutality":
                    g.SandboxBrutality = float.Parse(Next(), CultureInfo.InvariantCulture);
                    if (!float.IsFinite(g.SandboxBrutality.Value) || g.SandboxBrutality.Value is < 0 or > 100)
                        throw new ArgumentException("--sandbox-brutality must be finite and between 0 and 100");
                    break;
                case "--sandbox-stats":
                    g.SandboxStats = float.Parse(Next(), CultureInfo.InvariantCulture);
                    if (!float.IsFinite(g.SandboxStats.Value) || g.SandboxStats.Value < 0) throw new ArgumentException("--sandbox-stats must be non-negative and finite");
                    break;
                case "--sandbox-power": g.SandboxPower = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--sandbox-part": g.SandboxPart = Next(); break;
                case "--sandbox-sever": g.SandboxSever = true; break;
                case "--sandbox-height": g.SandboxHeight = float.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--sandbox-zoom": g.SandboxZoom = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--sandbox-dummy": g.SandboxDummy = g.SandboxBare = true; break;
                case "--sandbox-mode": g.SandboxMode = Next(); break;
                case "--sandbox-races": g.SandboxRaces = true; break;
                case "--terrain-sandbox":
                    g.TerrainSandbox = TerrainSandboxSpot;
                    if (i + 1 < args.Length && args[i + 1] == "cliffs") (g.TerrainSandbox, g.TerrainSandboxCliffs, i) = (CliffSandboxSpot, true, i + 1);
                    if (i + 1 < args.Length && args[i + 1].Split(',') is [var tx, var tz] && float.TryParse(tx, NumberStyles.Float, CultureInfo.InvariantCulture, out float fx)
                        && float.TryParse(tz, NumberStyles.Float, CultureInfo.InvariantCulture, out float fz))
                    {
                        g.TerrainSandbox = (fx, fz);
                        i++;
                    }
                    break;
                case "--duel":
                    g.Duel = true;
                    if (i + 1 < args.Length && args[i + 1].Split(',') is [var dx, var dz] && float.TryParse(dx, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(dz, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        g.DuelAt = (x, z);
                        i++;
                    }
                    break;
                case "--battle":
                    g.Battle = [.. Enumerable.Range(0, 2).Select(_ => (Math.Max(int.Parse(Next(), CultureInfo.InvariantCulture), 1), Next()))];
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int rows))
                    {
                        g.BattleRows = Math.Max(rows, 1);
                        i++;
                    }
                    break;
                case "--battle-map":
                    switch (Next())
                    {
                        case "plateau": (g.BattleFlat, g.BattleAt) = (false, null); break;
                        case "mesa": (g.BattleFlat, g.BattleAt) = (false, (GameHost.DuelSpot.X, GameHost.DuelSpot.Y)); break;
                        case "fog": (g.BattleFlat, g.BattleAt) = (false, (GameHost.FogBattleSpot.X, GameHost.FogBattleSpot.Y)); break;
                        case "sandbox": (g.BattleFlat, g.BattleAt) = (true, null); break;
                        case var map when map.Split(',') is [var mx, var mz] && float.TryParse(mx, NumberStyles.Float, CultureInfo.InvariantCulture, out float bx)
                            && float.TryParse(mz, NumberStyles.Float, CultureInfo.InvariantCulture, out float bz):
                            (g.BattleFlat, g.BattleAt) = (false, (bx, bz));
                            break;
                        default: throw new ArgumentException("--battle-map must be plateau, mesa, fog, sandbox or x,z");
                    }
                    break;
                case "--battle-no-fear": g.BattleNoFear = true; break;
                case "--battle-width":
                    var widths = Next().Split(',').Select(w => (int?)Math.Max(int.Parse(w, CultureInfo.InvariantCulture), 1)).ToArray();
                    g.BattleWidth = widths switch { [var both] => [both, both], [var first, var second] => [first, second], _ => throw new ArgumentException("--battle-width takes one or two counts, a,b") };
                    break;
                case "--move-to": g.MoveTo = (float.Parse(Next(), CultureInfo.InvariantCulture), float.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--quit-after": g.QuitAfter = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--load": g.Load = Next(); break;
                case "--renderer":
                {
                    string renderer = Next().ToLowerInvariant();
                    if (renderer is not ("vulkan" or "metal")) throw new ArgumentException("--renderer must be vulkan or metal");
                    g.Renderer = renderer;
                    break;
                }
                case "--play-bench": (g.PlayBench, g.FpsLimit, g.VSync) = (Next(), 0, false); break;
                case "--no-precompile": g.NoPrecompile = true; break;
                case "--scene": g.Scene = Next().ToLowerInvariant(); break;
                case "--scene-frames": g.SceneFrames = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 200); break;
                case "--scene-every": g.SceneEvery = Math.Max(int.Parse(Next(), CultureInfo.InvariantCulture), 1); break;
                case "--scene-npc": g.SceneNpc = Next(); break;
                case "--scene-town": g.SceneTown = Next(); break;
                case "--low-end": g.LowEnd = true; rest.Add(a); break;
                default: rest.Add(a); break;
            }
        }
        if (g.LowEnd) g.UiMode = Meitou.Data.Ui.UiMode.Faithful;
        return (g, rest.ToArray());
    }
}
