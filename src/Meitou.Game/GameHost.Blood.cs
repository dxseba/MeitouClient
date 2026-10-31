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

    void UpdateWounds(World world)
    {
        Meitou.Rendering.Characters.WoundMasks.Sample ??= static (x, y, w) =>
        {
            var p = Meitou.Simulation.Wounds.WoundPolicy.Sample(x, y, new Meitou.Simulation.Wounds.WoundShape(w.Cosine, w.Sine, w.Aspect, w.Depth));
            return new Vector3(p.Core, p.Area, p.Ring);
        };
        if (!ReferenceEquals(world.CombatEvents, woundQueue)) { woundQueue.Clear(); world.CombatEvents = woundQueue; }
        while (woundQueue.TryDequeue(out var e))
        {
            if (e.Part < 0 || world.Characters.Cold(e.Victim.Slot) is not { Medical: { } medical } cold || e.Part >= medical.Parts.Count || cold.Race is { IsRobot: true }) continue;   // robots have no flesh
            var part = medical.Parts[e.Part];
            wounds.Apply(e, part.Template.StringId switch { "101-gamedata.quack" => Meitou.Simulation.Wounds.WoundRegion.Chest, "100-gamedata.quack" => Meitou.Simulation.Wounds.WoundRegion.Abdomen, _ => Meitou.Simulation.Wounds.WoundRegion.Unknown }, part.MaxHp);
        }
    }

    /// <summary>The character's open wounds as the renderer places them (segments as <see cref="Meitou.Simulation.Wounds.Wound"/>).</summary>
    IReadOnlyList<Meitou.Rendering.CharacterWoundMark> WoundMarks(CharacterId id)
    {
        if (wounds.Of(id) is not { } body) return [];
        var marks = new List<Meitou.Rendering.CharacterWoundMark>();
        for (int i = 0; i < body.Torso.Length; i++)
            if (body.Torso[i] is { } w)
                {
                // Off the centre line: each wound keeps its own slant and height.
                float off = w.Age * 37 % 7 / 3f - 1;
                marks.Add(new("Bip01 Pelvis", "Bip01 Neck", (i == 0 ? 0.7f : i == 1 ? 0.35f : 0.5f) + off * 0.13f, Vector3.Normalize(new Vector3(off * 0.95f, 0, i == 2 ? -1 : 1)), w.Shape.Cosine, w.Shape.Sine, w.Shape.Aspect, w.Shape.Depth));
            }
        foreach (var slot in body.Limbs)
            if (slot is { } w && marks.Count < 16)
            {
                if (w.Segment == 8)
                {
                    marks.Add(new("Bip01 Neck", "Bip01 Head", 1.5f + w.T, w.Side, w.Shape.Cosine, w.Shape.Sine, w.Shape.Aspect, w.Shape.Depth));
                    continue;
                }
                string side = w.Segment < 2 || w.Segment is 4 or 5 ? "L" : "R";
                bool lower = (w.Segment & 1) != 0;
                var (a, b) = w.Segment < 4 ? (lower ? ("Forearm", "Hand") : ("UpperArm", "Forearm")) : (lower ? ("Calf", "Foot") : ("Thigh", "Calf"));
                marks.Add(new($"Bip01 {side} {a}", $"Bip01 {side} {b}", w.T, w.Side, w.Shape.Cosine, w.Shape.Sine, w.Shape.Aspect, w.Shape.Depth));
            }
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
