using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Audio;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// The blood on the ground (docs/game/blood-decals.md): with the game's <c>Blood</c> setting on, the simulation's <see cref="World.BloodMarks"/> become terrain decals in
/// a <see cref="TerrainDecalStore"/> that ages with game time, drawn by the world frame's <see cref="TerrainDecalRenderer"/> within <c>Decal Range</c> at
/// <c>Decal Resolution</c> (the install's <c>settings.cfg</c>). Display only: the simulation is not affected.
/// </summary>
sealed partial class GameHost
{
    /// <summary>The <c>Blood</c> resource group's folders by decal type (<see cref="TerrainDecalType"/>), the game's names for them.</summary>
    static readonly string[] BloodFolders = ["line", "splash", "pool", "smear"];

    /// <summary>How far above the ground a mark is still left on it (an engine rule: a character on an upper floor of a building bleeds onto the floor, which has no decals).</summary>
    const float BloodAboveGround = 20;

    TerrainDecalStore? decalStore;
    World? bloodFor;
    long bloodTick;
    double debrisClock;
    readonly ConcurrentQueue<BloodMark> bloodQueue = new();
    readonly Random bloodRandom = new();

    /// <summary>Makes the store and the renderer when the install's <c>Blood</c> setting is on (missing: on).</summary>
    void SetUpBlood(IGpuContext context)
    {
        gpu.Debris ??= new DebrisRenderer(context, gpu.Objects?.Resolver, assets.Find);   // made now: its shaders compile here, not at the first burst
        gpu.Particles?.PrepareBursts(BurstSplash, BurstSpray, SparksSystem);
        if (gpu.Decals is not null || AudioSystem.Setting(install, "Blood", 1) == 0) return;
        var files = BloodTextureFiles(assets.Configured);
        decalStore = new TerrainDecalStore([.. files.Select(list => (IReadOnlyList<(int, int)>)[.. list.Select(DdsSize)])]);
        gpu.Decals = TerrainDecalRenderer.Create(context, files);
        gpu.Decals.Meitou = o.MeitouBlood;
        gpu.Decals.GroundHeight = (x, z) => gpu.Terrain.HeightAt(x, z);
        gpu.Decals.Surfaces = o.MeitouBloodSurfaces;
        gpu.Decals.Resolution = (int)AudioSystem.Setting(install, "Decal Resolution", 1024, 512, 2048);
        gpu.Decals.Range = AudioSystem.Setting(install, "Decal Range", 1000, 0, 5000);
        gpu.DecalStore = decalStore;
    }

    /// <summary>
    /// Once a frame: luckyren Mode handed to the world, the marks of the ticks since the last frame into the store (bursts thrown), and the decals aged and the
    /// pieces moved by the game time they took.
    /// </summary>
    void UpdateBlood()
    {
        var world = session.World;
        world.Luckyren = config.LuckyrenMode;
        UpdateBlockSparks(world);
        if (decalStore is null && !config.LuckyrenMode && pieces.Count == 0)
        {
            world.BloodMarks = null;
            gpu.DebrisPieces.Clear();
            AddBolts();   // the bolts are drawn whatever the Blood setting
            UpdateFalls(world);   // and the severed limbs falling
            UpdateGuts(world, 1f / 60);
            return;
        }
        if (!ReferenceEquals(bloodFor, world)) BloodForNewWorld(null);
        world.BloodMarks = bloodQueue;
        float tick = world.TickSeconds;
        while (bloodQueue.TryDequeue(out var m))
        {
            if (m.Kind == BloodMarkKind.Explosion)
            {
                Burst(m, world);
                continue;
            }
            if (decalStore is not { } store) continue;
            uint opaque = 0xFF000000 | Abgr(m.Colour);
            long who = (long)(uint)m.Who.Slot << 32 | (uint)m.Who.Generation;
            if (gpu.Decals is { Surfaces: true } surfaces) SurfaceMark(surfaces, m, who, opaque, tick);   // the walls, floors and objects too, upper floors included
            if (m.Position.Y > gpu.Terrain.HeightAt(m.Position.X, m.Position.Z) + BloodAboveGround) continue;
            switch (m.Kind)
            {
                case BloodMarkKind.Pool: store.Bleed(who, m.Position, m.Amount, tick, opaque, bloodRandom); break;
                case BloodMarkKind.Smear:
                    store.Add(TerrainDecalType.Smear, m.Position, m.Direction, 0.2f, (uint)(Math.Clamp(m.Amount, 0, 1) * 255) << 24 | Abgr(m.Colour), TerrainDecalStore.NewLife, bloodRandom);
                    break;
                case BloodMarkKind.Drip: store.Add(TerrainDecalType.Pool, m.Position, m.Direction, m.Amount, opaque, TerrainDecalStore.NewLife, bloodRandom); break;
                case BloodMarkKind.Splash: store.Add(TerrainDecalType.Splash, m.Position, m.Direction, m.Amount, opaque, TerrainDecalStore.NewLife, bloodRandom); break;
            }
        }
        long ticks = world.Tick - bloodTick;
        bloodTick = world.Tick;
        if (ticks > 0 && decalStore is { } aging && gpu.Decals is { } decals)
        {
            aging.Age(ticks * tick, decals.Drawn);
            decals.AgeSurfaces(ticks * tick);
        }
        // The pieces move by the game time between frames, the tick count plus the simulation's alpha, so they fly smoothly at the display rate, not in tick steps.
        double clock = session.ShownTick + session.SimulationAlpha;   // the shown tick: one run ahead is not on screen yet
        UpdateDebris(world, (float)Math.Clamp((clock - debrisClock) * tick, 0, 0.5));
        debrisClock = clock;
    }

    /// <summary>One mark as the <c>bloodsurfaces</c> switch's decals (<see cref="TerrainDecalRenderer.Surfaces"/>); the same marks and sizes as the terrain's.</summary>
    void SurfaceMark(TerrainDecalRenderer surfaces, BloodMark m, long who, uint opaque, float tick)
    {
        switch (m.Kind)
        {
            case BloodMarkKind.Pool: surfaces.BleedSurface(who, m.Position, m.Amount, tick, opaque, bloodRandom); break;
            case BloodMarkKind.Smear:
                surfaces.AddSurface(TerrainDecalType.Smear, m.Position, m.Direction, 0.2f, (uint)(Math.Clamp(m.Amount, 0, 1) * 255) << 24 | Abgr(m.Colour), bloodRandom);
                break;
            case BloodMarkKind.Drip: surfaces.AddSurface(TerrainDecalType.Pool, m.Position, m.Direction, m.Amount, opaque, bloodRandom); break;
            case BloodMarkKind.Splash: surfaces.AddSurface(TerrainDecalType.Splash, m.Position, m.Direction, m.Amount, opaque, bloodRandom); break;
        }
    }

    /// <summary>The EFFECT `small- sparks1` (5463-nodes_furniture1_otto.mod) the original throws on a parry: its particle system.</summary>
    const string SparksSystem = "kenshi_sparks";
    readonly ConcurrentQueue<CharacterId> sparkQueue = new();
    readonly ConcurrentQueue<Meitou.Simulation.Combat.CombatEvent> woundQueue = new();
    readonly Meitou.Simulation.Wounds.WoundTracker wounds = new();
    static readonly Meitou.Simulation.Wounds.WoundBody NoWounds = new();

    const float BloodRunSeconds = 5, GutLifeSeconds = 600, StumpBoneOut = 1.1f;

    sealed class GutFall { public Vector3 Position, Velocity; public Quaternion Rotation = Quaternion.Identity; public float Floor, Age; }
    readonly Dictionary<CharacterId, GutFall> gutFalls = [];
    readonly HashSet<CharacterId> gutsSpilled = [];

    /// <summary>A deep belly wound spills the bowel as one loose body, as the GTA V FLESH port's guts prop: it falls from the wound and lies where it lands. Visual only.</summary>
    void UpdateGuts(World world, float dt, bool draw = true)
    {
        if (gpu?.Debris is not { } debris) return;
        debris.AnatomyFile ??= Meitou.Content.GameInstall.Locate() is { } install ? Path.Combine(install.ModsDirectory, "Gauntlet of Crowns", "Ecorche.anatomy") : null;
        if (debris.Extent(Meitou.Rendering.DebrisRenderer.Bowel) is null) return;
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            var hot = world.Characters.Previous[slot];
            var id = world.Characters.IdOf(slot);
            if (!hot.Alive || wounds.Of(id) is not { DeepAbdomen: true } || gutFalls.ContainsKey(id) || gutsSpilled.Contains(id) || world.Characters.Cold(slot)?.Race?.Name is not ("Greenlander" or "Scorchlander")) continue;
            var forward = new Vector3(MathF.Sin(hot.Yaw), 0, MathF.Cos(hot.Yaw));
            gutFalls[id] = new GutFall { Position = hot.Position + new Vector3(0, 11.5f, 0) + forward * 1.2f, Velocity = forward * 1.5f, Floor = hot.Position.Y };
        }
        foreach (var (who, gone) in gutFalls.ToArray())
            if (gone.Age > GutLifeSeconds) { gutFalls.Remove(who); gutsSpilled.Add(who); }   // spilled once: the wound stays and does not spill again
        foreach (var fall in gutFalls.Values)
        {
            fall.Age += dt;
            if (fall.Position.Y > fall.Floor + 0.35f)
            {
                // Slow at first, dragged out of the wound, then falling free.
                fall.Velocity.Y -= (fall.Age < 0.8f ? 8 : 40) * dt;
                fall.Position += fall.Velocity * dt;
                fall.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3 * dt) * fall.Rotation);
            }
            if (draw) gpu.DebrisPieces.Add(new Meitou.Rendering.DebrisPiece(Meitou.Rendering.DebrisRenderer.Bowel, 1, fall.Rotation, fall.Position));
        }
    }

    // A deep wound (dose 1.2) closes over about two hours of play at speed 1.
    const int WoundHealTicks = 3000;
    const float WoundHealDose = 0.01f;
    long woundHealStep;

    const string WoundFile = "wounds.meitou";
    const int JawBlows = 4;
    readonly Dictionary<CharacterId, int> jawBlows = [];
    readonly HashSet<CharacterId> jawsLost = [];
    /// <summary>The head wound's other forms: bit 7 the scalp gone, bit 8 the skull broken as well.</summary>
    readonly Dictionary<CharacterId, uint> headsOpen = [];
    string? woundsFrom;

    void UpdateWounds(World world)
    {
        Meitou.Rendering.Characters.WoundMasks.Sample ??= static (x, y, w) =>
        {
            var p = Meitou.Simulation.Wounds.WoundPolicy.Sample(x, y, new Meitou.Simulation.Wounds.WoundShape(w.Cosine, w.Sine, w.Aspect, w.Depth));
            return new Vector3(p.Core, p.Area, p.Ring);
        };
        if (!ReferenceEquals(world.CombatEvents, woundQueue))
        {
            // Another world: its characters are not these. Wounds are not saved yet.
            woundQueue.Clear();
            wounds.Clear();
            gutFalls.Clear();
            gutsSpilled.Clear();
            jawBlows.Clear();
            jawsLost.Clear();
            headsOpen.Clear();
            // Only right after a load: a new game must not take the last save's wounds.
            string? loadedFrom = woundsFrom;
            woundsFrom = null;
            if (loadedFrom is { } folder && File.Exists(Path.Combine(folder, WoundFile)))
                wounds.Read(File.ReadLines(Path.Combine(folder, WoundFile)), key => Meitou.Simulation.Saving.SaveLoader.LastSpawned.TryGetValue(key, out var who) ? who : null);
            world.CombatEvents = woundQueue;
        }
        if (world.Tick / WoundHealTicks != woundHealStep) { woundHealStep = world.Tick / WoundHealTicks; wounds.Heal(WoundHealDose); }
        while (woundQueue.TryDequeue(out var e))
        {
            // A hit throws real drops along the blow, out of the wound.
            if (e.Kind == Meitou.Simulation.Combat.CombatEventKind.Hit && gpu?.Particles is { } drops)
            {
                float force = MathF.Min((e.Damage.Cut + e.Damage.Pierce) / 20, 1);
                var along = e.Direction.LengthSquared() > 1e-6f ? Vector2.Normalize(e.Direction) : Vector2.UnitX;
                var from = e.Position + new Vector3(0, 7 + bloodRandom.NextSingle() * 7, 0);
                for (int i = 0; i < 1 + (int)(force * 3); i++)
                {
                    var spray = Vector3.Normalize(new Vector3(along.X + (bloodRandom.NextSingle() - 0.5f), 0.3f + bloodRandom.NextSingle() * 0.6f, along.Y + (bloodRandom.NextSingle() - 0.5f)));
                    drops.Burst(BurstSpray, from, Turn(Vector3.UnitZ, spray), 0.1f + force * 0.2f, MistSize * 0.5f, MistLife * 0.6f);
                }
            }
            // A heavy blow knocks a splinter of bone and a scrap of flesh out of the wound.
            if (e.Kind == Meitou.Simulation.Combat.CombatEventKind.Hit && e.Damage.Cut + e.Damage.Pierce >= 15 && gpu?.Debris is { } shards)
                foreach (bool flesh in new[] { false, true })
                {
                    string mesh = flesh ? MeatMesh : BoneMeshes[bloodRandom.Next(BoneMeshes.Length)];
                    if (shards.Extent(mesh, flesh ? null : BoneMeshes[0]) is not { } size) continue;
                    float scale = flesh ? 0.16f : 0.035f;
                    var along = e.Direction.LengthSquared() > 1e-6f ? Vector2.Normalize(e.Direction) : Vector2.UnitX;
                    pieces.Add(new Piece
                    {
                        Mesh = mesh, Scale = scale, Extent = size * scale, Floor = float.NaN, Born = world.Tick, Flesh = flesh, Colour = 0, Bloody = true,
                        Position = e.Position + new Vector3(0, 6 + bloodRandom.NextSingle() * 8, 0),
                        Velocity = new Vector3(along.X * 18 + bloodRandom.NextSingle() * 12 - 6, 18 + bloodRandom.NextSingle() * 16, along.Y * 18 + bloodRandom.NextSingle() * 12 - 6),
                        Rotation = Quaternion.Identity, Spin = new Vector3(5, 3, 7),
                    });
                }
            if (e.Part < 0 || world.Characters.Cold(e.Victim.Slot) is not { Medical: { } medical } cold || e.Part >= medical.Parts.Count || cold.Race is { IsRobot: true }) continue;   // robots have no flesh
            var part = medical.Parts[e.Part];
            // Heavy blows to the head: the fourth takes the jaw.
            if (e.Kind == Meitou.Simulation.Combat.CombatEventKind.Hit && e.Damage.Cut + e.Damage.Pierce >= 15 && part.Template.Type == Meitou.Data.Gameplay.Bodies.BodyPartType.Head)
            {
                jawBlows[e.Victim] = jawBlows.GetValueOrDefault(e.Victim) + 1;
                if (jawBlows[e.Victim] >= JawBlows)
                    switch (g.SandboxHeadWound ?? e.Victim.Slot % 3)   // each character's own
                    {
                        case 1: headsOpen[e.Victim] = 1u << 7; break;
                        case 2: headsOpen[e.Victim] = 3u << 7; break;
                        default: jawsLost.Add(e.Victim); break;
                    }
            }
            wounds.Apply(e, part.Template.StringId switch { "101-gamedata.quack" => Meitou.Simulation.Wounds.WoundRegion.Chest, "100-gamedata.quack" => Meitou.Simulation.Wounds.WoundRegion.Abdomen, _ => Meitou.Simulation.Wounds.WoundRegion.Unknown }, part.MaxHp);
        }
    }

    /// <summary>A cut's axis, the blow's own direction, turned a little (within 20 degrees): the arm's swing is not known exactly.</summary>
    /// <summary>The body groups of a character's stumps (<see cref="Meitou.Rendering.Characters.WoundMasks.BodyGroup"/>: arms 1 and 2, legs 3 and 4).</summary>
    uint LostGroups(CharacterId id)
    {
        uint lost = 0;
        if (session.World.Characters.Cold(id.Slot)?.Medical is { } medical)
            foreach (var part in medical.Parts)
                if (part.Limb == Meitou.Simulation.Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb) lost |= 1u << (limb < 2 ? 1 + limb : 1 + limb);
        if (jawsLost.Contains(id)) lost |= 1u << Meitou.Rendering.Characters.WoundMasks.JawGroup;
        lost |= headsOpen.GetValueOrDefault(id);
        if (gutFalls.ContainsKey(id) || gutsSpilled.Contains(id)) lost |= 1u << 5;   // the bowel has left the body
        return lost;
    }

    /// <summary>How far a wound's blood has run, in 12 steps over <see cref="BloodRunSeconds"/>: each step repaints the mask.</summary>
    float Flowed(long born) => MathF.Ceiling(Math.Clamp((float)((session.World.Tick - born) * session.World.TickSeconds / BloodRunSeconds), 0, 1) * 12) / 12;

    /// <summary>Each lost limb's own cut (bind heights: shoulder, above the elbow, forearm; thigh, knee, shin), rolled by character and limb, visual: the first keeps the game's stump.</summary>
    uint LimbCuts(CharacterId id)
    {
        uint cuts = 0;
        if (session.World.Characters.Cold(id.Slot)?.Medical is { } medical)
            foreach (var part in medical.Parts)
                if (part.Limb == Meitou.Simulation.Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb)
                {
                    cuts |= (uint)(Meitou.Simulation.Wounds.SeveredLevels.Height(Meitou.Simulation.Wounds.SeveredLevels.Of(id, limb), limb) * 10) << (8 * limb);
                }
        return cuts;
    }

    static Vector2 Turned(in Meitou.Simulation.Wounds.WoundShape shape, int age)
    {
        float turn = (age * 2.399963f % MathF.Tau - MathF.PI) / MathF.PI * 0.35f, c = MathF.Cos(turn), s = MathF.Sin(turn);
        return new Vector2(shape.Cosine * c - shape.Sine * s, shape.Cosine * s + shape.Sine * c);
    }

    /// <summary>The character's open wounds as the renderer places them (segments as <see cref="Meitou.Simulation.Wounds.Wound"/>).</summary>
    IReadOnlyList<Meitou.Rendering.CharacterWoundMark> WoundMarks(CharacterId id)
    {
        var marks = new List<Meitou.Rendering.CharacterWoundMark>();
        var body = wounds.Of(id) ?? NoWounds;
        for (int i = 0; i < body.Torso.Length; i++)
            if (body.Torso[i] is { } w)
                {
                // Off the centre line: each wound keeps its own slant and height.
                float off = w.Age * 37 % 7 / 3f - 1;
                marks.Add(new("Bip01 Pelvis", "Bip01 Neck", (i == 0 ? 0.7f : i == 1 ? 0.35f : 0.5f) + off * 0.13f, Vector3.Normalize(new Vector3(off * 0.95f, 0, i == 2 ? -1 : 1)), Turned(w.Shape, w.Age).X, Turned(w.Shape, w.Age).Y, w.Shape.Aspect, w.Shape.Depth, w.Size, Flowed(w.Born)));
            }
        // The newest first: past the shader's 64, the oldest give way, not the newest.
        var newest = body.Limbs.ToArray().Where(w => w is not null).OrderByDescending(w => w!.Value.Age).ToArray();
        foreach (var slot in newest)
            if (slot is { } w && marks.Count < Meitou.Simulation.Wounds.WoundBody.LimbSlots + 3)
            {
                if (w.Segment == 9)
                {
                    marks.Add(new("Bip01 Pelvis", "Bip01 Neck", w.T, w.Side, Turned(w.Shape, w.Age).X, Turned(w.Shape, w.Age).Y, w.Shape.Aspect, w.Shape.Depth, w.Size, Flowed(w.Born)));
                    continue;
                }
                if (w.Segment == 8)
                {
                    marks.Add(new("Bip01 Neck", "Bip01 Head", 2.2f + w.T, new Vector3(w.Age % 2 == 0 ? 0.9f : -0.9f, 0.5f, 0.2f * w.Side.Z), Turned(w.Shape, w.Age).X, Turned(w.Shape, w.Age).Y, w.Shape.Aspect, w.Shape.Depth, w.Size, Flowed(w.Born)));
                    continue;
                }
                string side = w.Segment < 2 || w.Segment is 4 or 5 ? "L" : "R";
                bool lower = (w.Segment & 1) != 0;
                var (a, b) = w.Segment < 4 ? (lower ? ("Forearm", "Hand") : ("UpperArm", "Forearm")) : (lower ? ("Calf", "Foot") : ("Thigh", "Calf"));
                marks.Add(new($"Bip01 {side} {a}", $"Bip01 {side} {b}", w.T, w.Side, Turned(w.Shape, w.Age).X, Turned(w.Shape, w.Age).Y, w.Shape.Aspect, w.Shape.Depth, w.Size, Flowed(w.Born)));
            }
        // A thin race's limbs and trunk take a smaller cut.
        if (session.World.Characters.Cold(id.Slot)?.Race?.Name is { } race && (race.Contains("Hive", StringComparison.OrdinalIgnoreCase) || race.Contains("Drone", StringComparison.OrdinalIgnoreCase)))
            for (int i = 0; i < marks.Count; i++) marks[i] = marks[i] with { Size = marks[i].Size * 0.55f };
        // A stump's end stays open: a permanent deep cut on it, so its bone and flesh show.
        if (session.World.Characters.Cold(id.Slot)?.Medical is { } stumps)
            foreach (var part in stumps.Parts)
                if (part.Limb == Meitou.Simulation.Bodies.LimbState.Stump && part.Template.LimbSlot is >= 0 and <= 3 and var limb)
                {
                    string limbSide = limb % 2 == 0 ? "L" : "R";
                    int cutLevel = Meitou.Simulation.Wounds.SeveredLevels.Of(id, limb);
                    (string upper, string lower, float along) = (limb < 2, cutLevel) switch { (true, 0) => ("UpperArm", "Forearm", 0.45f), (true, 1) => ("UpperArm", "Forearm", 0.95f), (true, _) => ("Forearm", "Hand", 0.5f), (false, 0) => ("Thigh", "Calf", 0.3f), (false, 1) => ("Thigh", "Calf", 0.87f), _ => ("Calf", "Foot", 0.42f) };
                    marks.Add(new($"Bip01 {limbSide} {upper}", $"Bip01 {limbSide} {lower}", along, Vector3.Normalize(new Vector3(limb % 2 == 0 ? 1 : -1, -0.6f, 0.3f)), 1, 0, 0.35f, 0.5f, 0.5f));
                }
        // A stump without a wound still needs the (blank) mask that lets its anatomy draw: a mark on no bone paints nothing.
        if (marks.Count == 0 && LostGroups(id) != 0) marks.Add(new("", "", 0, default, 1, 0, 1, 0));
        return marks;
    }

    /// <summary>
    /// Once a frame: the parries of the ticks since the last frame throw <see cref="SparksSystem"/> at the parrying character's <c>Bip01 Prop2</c> (its weapon), 0 or 1 unit higher
    /// at random, when that character is drawn (the original's <c>FUN_1404394f0</c>: only for a character on screen; docs/game/combat.md "Block sparks").
    /// </summary>
    void UpdateBlockSparks(World world)
    {
        UpdateWounds(world);
        if (gpu.Particles is not { } particles || gpu.Characters is not { } characters)
        {
            world.BlockSparks = null;
            return;
        }
        if (!ReferenceEquals(world.BlockSparks, sparkQueue)) { sparkQueue.Clear(); world.BlockSparks = sparkQueue; }
        while (sparkQueue.TryDequeue(out var who))
            if (characters.BonePosition(DrawKey(who), "Bip01 Prop2") is { } at)
                particles.Burst(SparksSystem, at + new Vector3(0, bloodRandom.Next(2), 0), Quaternion.Identity, 0.1f);
    }

    /// <summary>
    /// The store emptied for the session's world and hooked to its marks, then given the decals of the save's TERRAIN_DECALS record <paramref name="saved"/> when the
    /// world was loaded from one (the load calls it before the new world's first frame).
    /// </summary>
    void BloodForNewWorld(FcsRecord? saved)
    {
        var world = session.World;
        pieces.Clear();
        if (gpu is not null) gpu.DebrisPieces.Clear();
        bloodQueue.Clear();
        bloodFor = world;
        bloodTick = world.Tick;
        debrisClock = session.ShownTick + session.SimulationAlpha;
        if (decalStore is not { } store) return;
        store.Clear();
        gpu?.Decals?.ClearSurfaces();
        world.BloodMarks = bloodQueue;
        if (saved is null) return;
        store.Load(SaveDecals.Read(saved));
        gpu?.Decals?.LoadSurfaces(SaveSurfaceDecals.Read(saved));   // the bloodsurfaces switch's marks (Meitou-only keys)
    }

    /// <summary>The store's decals and the surfaces' into the save's TERRAIN_DECALS record (with <c>Blood</c> off the record keeps what the loaded save had).</summary>
    void WriteDecals(SaveGame game)
    {
        if (decalStore is not { } store || game.Decals is not { } record) return;
        SaveDecals.Write(record, store.All);
        if (gpu?.Decals is { } decals) SaveSurfaceDecals.Write(record, decals.SavedSurfaces);
    }

    /// <summary>0xRRGGBB as the decal's ABGR colour bytes (alpha 0).</summary>
    static uint Abgr(uint rgb) => (rgb >> 16 & 0xFF) | (rgb & 0xFF00) | (rgb & 0xFF) << 16;

    /// <summary>The <c>.dds</c> files of the <c>Blood</c> group's folders, by <see cref="BloodFolders"/> type, in registration order and by name within a folder.</summary>
    static List<IReadOnlyList<string>> BloodTextureFiles(OgreScriptResources resources)
    {
        var files = BloodFolders.Select(_ => new List<string>()).ToList();
        foreach (var location in resources.Locations)
        {
            if (!location.Group.Equals("Blood", StringComparison.Ordinal) || !Directory.Exists(location.Path)) continue;
            int type = Array.FindIndex(BloodFolders, f => f.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(location.Path)), StringComparison.OrdinalIgnoreCase));
            if (type < 0) continue;
            files[type].AddRange(Directory.EnumerateFiles(location.Path, "*.dds").Order(StringComparer.OrdinalIgnoreCase));
        }
        return [.. files.Cast<IReadOnlyList<string>>()];
    }

    /// <summary>A DDS file's size in texels from its header (0 × 0 when unreadable).</summary>
    static (int Width, int Height) DdsSize(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[20];
            using var file = File.OpenRead(path);
            file.ReadExactly(header);
            return (BitConverter.ToInt32(header[16..]), BitConverter.ToInt32(header[12..]));
        }
        catch (IOException) { return (0, 0); }
    }
}
