using System.Numerics;
using System.Text;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.MapMarkers;

/// <summary>
/// The markers of one world, saved with it in the mod's sidecar (<see cref="IModSaveData"/>) and, as the original keeps them, in
/// <c>Map-markers.json</c> beside each save the game writes (<see cref="WriteFile"/>); a loaded or imported save's <c>Map-markers.json</c>, the
/// original's own store, replaces the sidecar's (<see cref="Ready"/>). Presentation only: the tick never reads it, so it is not hashed. The
/// interface edits it on its own thread and the save captures it between ticks, so every access takes the lock.
/// </summary>
public sealed class MarkerBook(Action<string> log) : ITickSystem, IModSaveData, IWorldReady
{
    const int Version = 1;

    readonly Lock _lock = new();
    readonly List<Marker> _markers = [];

    /// <summary>A copy of the markers, in the order they were added (later ones draw on top).</summary>
    public Marker[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _markers];
        }
    }

    public Marker? Find(int id)
    {
        lock (_lock)
        {
            return _markers.Find(m => m.Id == id);
        }
    }

    /// <summary>A new marker at <paramref name="at"/>, its id one past the highest in use (the original's).</summary>
    public Marker Add(Vector2 at, MarkerKind kind)
    {
        lock (_lock)
        {
            int id = _markers.Count == 0 ? 1 : _markers.Max(m => m.Id) + 1;
            var marker = new Marker(id, at, kind, "");
            _markers.Add(marker);
            return marker;
        }
    }

    public void Remove(int id)
    {
        lock (_lock)
        {
            _markers.RemoveAll(m => m.Id == id);
        }
    }

    /// <summary>Changes a marker under the lock, so a save never sees it half written.</summary>
    public void Edit(Marker marker, Action<Marker> change)
    {
        lock (_lock)
        {
            change(marker);
        }
    }

    /// <summary>A loaded or imported save's <c>Map-markers.json</c> (the original's file) replaces the markers the sidecar gave.</summary>
    public void Ready(World world, bool fromSave)
    {
        if (world.Start.SaveFolder is not { } folder || MarkerFile.Read(Path.Combine(folder, MarkerFile.Name), log) is not { } markers) return;

        lock (_lock)
        {
            _markers.Clear();
            _markers.AddRange(markers);
        }
    }

    /// <summary>Writes <c>Map-markers.json</c> into a save folder the game has just written (host thread; the book only).</summary>
    public void WriteFile(string folder) => MarkerFile.Write(Path.Combine(folder, MarkerFile.Name), Snapshot(), log);

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        lock (_lock)
        {
            writer.Write(Version);
            writer.Write(_markers.Count);
            foreach (var marker in _markers)
            {
                writer.Write(marker.Id);
                writer.Write(marker.At.X);
                writer.Write(marker.At.Y);
                writer.Write((int)marker.Kind);
                writer.Write(marker.Label);
            }
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) throw new InvalidDataException("Unknown Map markers save version");

        lock (_lock)
        {
            _markers.Clear();
            for (int count = reader.ReadInt32(), i = 0; i < count; i++) _markers.Add(ReadMarker(reader));
        }
    }

    static Marker ReadMarker(BinaryReader reader)
    {
        int id = reader.ReadInt32();
        var at = Vector2.Clamp(new(reader.ReadSingle(), reader.ReadSingle()), Vector2.Zero, Vector2.One);
        var kind = MarkerKinds.FromIndex(reader.ReadInt32());
        return new Marker(id, at, kind, MarkerKinds.Clean(reader.ReadString(), trimEnd: true));
    }
}
