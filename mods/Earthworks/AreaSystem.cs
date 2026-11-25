using System.Collections.Immutable;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.Earthworks;

/// <summary>Sets an Earthworks building's area (width across its facing, depth along it, game units).</summary>
public sealed record SetAreaCommand(string Site, float Width, float Depth) : SimCommand, IPausedCommand;

/// <summary>
/// The world's Earthworks state: each building's area (saved in the mod's sidecar, hashed), published for the host's clearing as an immutable
/// map; and the shovel the original's engineers dig with, hung on the right hand of each character working an Earthworks site.
/// </summary>
sealed class AreaSystem : ITickSystem, IModSaveData, IStateHashed, IPausedCommandHandler
{
    /// <summary>The original's shovel mesh (its <c>assets</c> folder; found by bare name in the mods' folders).</summary>
    const string Shovel = "TerrainLabShovel7.mesh";

    const string ShovelKey = "dxseba.earthworks.shovel";

    /// <summary>The areas by building site id, sorted.</summary>
    readonly SortedDictionary<string, Vector2> _areas = new(StringComparer.Ordinal);

    /// <summary>Characters the shovel was hung on, by slot.</summary>
    readonly HashSet<int> _digging = [];

    /// <summary>The areas as last published (host reads).</summary>
    public static volatile ImmutableDictionary<string, Vector2> Published = ImmutableDictionary<string, Vector2>.Empty;

    public AreaSystem() => Published = ImmutableDictionary<string, Vector2>.Empty;

    public string Name => "dxseba.earthworks.areas";

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        bool changed = false;
        foreach (var command in commands)
        {
            if (command is not SetAreaCommand set || world.Buildings.Find(set.Site) is not { } site || !Clearings.Kinds.ContainsKey(site.Record))
                continue;

            _areas[set.Site] = Clamp(new Vector2(set.Width, set.Depth));
            changed = true;
        }

        if (changed) Publish();
    }

    /// <summary>Within the original's planner limits: 144 to 1152 units a side (14.4 to 115.2 m).</summary>
    public static Vector2 Clamp(Vector2 size) => Vector2.Clamp(size, new(EarthworksSettings.Least), new(EarthworksSettings.Most));

    public void SlowWorld(World world)
    {
        DropGone(world);
        HangShovels(world);
    }

    /// <summary>Areas of buildings no longer in the world are forgotten.</summary>
    void DropGone(World world)
    {
        var gone = _areas.Keys.Where(id => world.Buildings.Find(id) is null).ToList();
        foreach (string id in gone) _areas.Remove(id);
        if (gone.Count > 0) Publish();
    }

    /// <summary>The shovel on each character whose build task works an Earthworks site; off again when it stops.</summary>
    void HangShovels(World world)
    {
        var table = world.Characters;
        var previous = table.Previous;
        for (int slot = 0; slot < previous.Length; slot++)
        {
            var cold = table.Cold(slot);
            bool digging = cold is not null && EarthworksMod.Settings.Shovel && previous[slot].Task == (byte)CharacterTask.Build
                && cold.BuildSite is { } id && world.Buildings.Find(id) is { } site && Clearings.Kinds.ContainsKey(site.Record);
            if (digging && _digging.Add(slot))
                cold!.SetBoneMesh(new BoneMesh(ShovelKey, Shovel, "Bip01 R Hand", Vector3.Zero, Quaternion.Identity));
            else if (!digging && _digging.Remove(slot))
                cold?.RemoveBoneMesh(ShovelKey);
        }
    }

    void Publish() => Published = _areas.ToImmutableDictionary(StringComparer.Ordinal);

    public void Hash(ref StateHasher h)
    {
        foreach (var (id, size) in _areas)
        {
            h.Add(id.Length);
            foreach (char c in id) h.Add((int)c);
            h.Add(size);
        }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(1);
        w.Write(_areas.Count);
        foreach (var (id, size) in _areas)
        {
            w.Write(id);
            w.Write(size.X);
            w.Write(size.Y);
        }
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != 1) return;

        int count = r.ReadInt32();
        _areas.Clear();
        for (int i = 0; i < count; i++) _areas[r.ReadString()] = Clamp(new Vector2(r.ReadSingle(), r.ReadSingle()));
        Publish();
    }
}
