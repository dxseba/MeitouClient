using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using Meitou.Data.World;

namespace Meitou.ContainerHighlight;

/// <summary>
/// Which containers the original highlights, and where its marker sits (<b>Verified</b>, DLL decompile, its building filter and marker anchor), by
/// BUILDING record, worked out once each on the host's thread: a building with an inventory whose functionality is BF_RESOURCE_STORAGE (2) or
/// BF_GENERAL_STORAGE (13), or whose name holds "storage", "barrel", "crate", "chest", "container" or "bin" (any case). The marker stands 0.3 above
/// the top of the building's meshes when that is above 0.02, else 0.9 above its origin.
/// </summary>
sealed class ContainerKinds(GameDatabase db, Action<string> log)
{
    const int ResourceStorage = 2, GeneralStorage = 13;

    static readonly string[] Words = ["storage", "barrel", "crate", "chest", "container", "bin"];

    /// <summary>
    /// The marker's height without mesh bounds, and its gap above them, in world units (the DLL's 0.9 and 0.3; tops at 0.02 or below count as none).
    /// </summary>
    const float NoBounds = 0.9f, AboveTop = 0.3f, LowestTop = 0.02f;

    readonly Dictionary<string, Kind> _kinds = new(StringComparer.Ordinal);
    readonly Dictionary<string, GameRecord?> _functions = new(StringComparer.Ordinal);
    readonly GameInstall? _install = GameInstall.Locate();

    /// <summary>
    /// A record's verdict: whether it is highlighted, why (for the probe log), and its marker height.
    /// </summary>
    public readonly record struct Kind(bool Matches, string Reason, float MarkerHeight);

    public Kind Of(string record)
    {
        if (_kinds.TryGetValue(record, out var known)) return known;

        return _kinds[record] = Decide(record);
    }

    Kind Decide(string record)
    {
        if (db.Find(record) is not { } building) return new(false, "reject_filtered (no record)", NoBounds);

        int function = TownBuildings.FunctionOf(db, building, _functions)?.GetInt("function", -1) ?? -1;
        bool storage = function is ResourceStorage or GeneralStorage;
        bool named = Named(building.BaseName) || Named(building.Name);
        if (!storage && !named) return new(false, "reject_filtered", NoBounds);

        return new(true, storage ? "match (storage function)" : "match (name)", MarkerHeightOf(building));
    }

    /// <summary>
    /// Whether a name holds one of the words (the game's name and the record's own, when a translation renamed it).
    /// </summary>
    static bool Named(string name) => Array.Exists(Words, word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    float MarkerHeightOf(GameRecord building)
    {
        try
        {
            float top = Top(building);
            return top > LowestTop ? top + AboveTop : NoBounds;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"mesh bounds of '{building.StringId}' not read: {e.Message}");
            return NoBounds;
        }
    }

    /// <summary>
    /// The highest point of the building's meshes as placed at its origin, unrotated (a yaw does not change it); 0 when none can be read.
    /// </summary>
    float Top(GameRecord building)
    {
        if (_install is null) return 0;

        float top = 0;
        var meshes = WorldObjectLayout.Building(db, building, "container-highlight", Vector3.Zero, Quaternion.Identity, new BuildingState());
        foreach (var mesh in meshes)
        {
            if (CollisionPaths.Resolve(_install, mesh.MeshPath) is not { } file || !OgreMeshReader.TryReadBounds(file, out var b)) continue;

            for (int corner = 0; corner < 8; corner++)
            {
                float x = (corner & 1) == 0 ? b.Min.X : b.Max.X, y = (corner & 2) == 0 ? b.Min.Y : b.Max.Y;
                var point = new Vector3(x, y, (corner & 4) == 0 ? b.Min.Z : b.Max.Z);
                top = Math.Max(top, Vector3.Transform(point, mesh.Transform).Y);
            }
        }

        return top;
    }
}
