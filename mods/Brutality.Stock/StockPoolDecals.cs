using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.World;
using Meitou.Game.Rendering.Terrain;

namespace Meitou.Brutality;

/// <summary>Places detached-part pools through the game's terrain and floor decal paths.</summary>
internal sealed class StockPoolDecals(object host)
{
    const float TerrainTolerance = 1.25f;
    static readonly FieldInfo? gpuField = Field(AccessTools.TypeByName("Meitou.Game.GameHost"), "Gpu");
    static readonly FieldInfo? storeField = Field(gpuField?.FieldType, "DecalStore");
    static readonly FieldInfo? terrainField = Field(gpuField?.FieldType, "Terrain");
    static readonly FieldInfo? surfacesField = Field(gpuField?.FieldType, "Decals");
    readonly Dictionary<long, Pool> active = [];
    readonly Random random = new(53239);

    readonly record struct Pool(Vector3 At, float Size, TerrainDecalStore? Store, TerrainDecal Decal,
        TerrainDecalRenderer? Surface);

    internal bool Available => gpuField?.GetValue(host) is { } gpu && storeField?.GetValue(gpu) is TerrainDecalStore
        && terrainField?.GetValue(gpu) is TerrainRenderer;

    internal bool Grow(long key, ref Vector3 at, float deltaSize, uint abgr, bool first)
    {
        if (!float.IsFinite(deltaSize) || deltaSize <= 0 || gpuField?.GetValue(host) is not { } gpu
            || storeField?.GetValue(gpu) is not TerrainDecalStore store) return false;

        if (first) return Begin(gpu, store, key, ref at, deltaSize, abgr);
        if (!active.TryGetValue(key, out var pool)) return false;
        at = pool.At;
        float flow = deltaSize * 5 * MathF.Max(1, pool.Size * 10);
        if (pool.Store is not null)
        {
            // The terrain store looks up a pool by cell position. Give up if a stock bleeder grew
            // this decal or its cell was replaced, so this source cannot shrink an existing pool.
            if (pool.Store != store || !PoolAt(store, at, out var current) || current != pool.Decal)
            {
                Stop(key);
                return false;
            }
            store.Bleed(key, at, flow, 1, abgr, random);
            if (!PoolAt(store, at, out var grown))
            {
                Stop(key);
                return false;
            }
            active[key] = pool with { Size = pool.Size + deltaSize, Decal = grown };
            return true;
        }

        if (surfacesField?.GetValue(gpu) is not TerrainDecalRenderer { Surfaces: true } surface || pool.Surface != surface)
        {
            Stop(key);
            return false;
        }
        surface.BleedSurface(key, at, flow, 1, abgr, random);
        active[key] = pool with { Size = pool.Size + deltaSize };
        return true;
    }

    internal void Stop(long key)
    {
        if (active.Remove(key, out var pool)) pool.Store?.Stanch(key);
    }

    bool Begin(object gpu, TerrainDecalStore store, long key, ref Vector3 at, float size, uint abgr)
    {
        if (!float.IsFinite(at.LengthSquared()) || terrainField?.GetValue(gpu) is not TerrainRenderer terrain
            || active.ContainsKey(key)) return false;
        float ground = terrain.HeightAt(at.X, at.Z);
        if (!float.IsFinite(ground) || at.Y < ground - TerrainTolerance) return false;
        if (at.Y > ground + TerrainTolerance)
        {
            if (surfacesField?.GetValue(gpu) is not TerrainDecalRenderer { Surfaces: true } surface) return false;
            surface.BleedSurface(key, at, size * 5, 1, abgr, random);
            active.Add(key, new(at, size, null, default, surface));
            return true;
        }

        // Use a distinct quantized slot when another pool is directly beneath this part. A
        // neighbouring slot within half a unit still places the pool under the same fragment.
        ReadOnlySpan<Vector2> offsets =
            [new(0, 0), new(.32f, 0), new(0, .32f), new(-.32f, 0), new(0, -.32f),
                new(.32f, .32f), new(-.32f, .32f), new(-.32f, -.32f), new(.32f, -.32f)];
        foreach (var offset in offsets)
        {
            var spot = at + new Vector3(offset.X, 0, offset.Y);
            if (PoolAt(store, spot, out _)) continue;
            store.Bleed(key, spot, size * 5, 1, abgr, random);
            if (!PoolAt(store, spot, out var decal))
            {
                store.Stanch(key);
                return false;
            }
            active.Add(key, new(spot, size, store, decal, null));
            at = spot;
            return true;
        }
        return false;
    }

    static bool PoolAt(TerrainDecalStore store, Vector3 at, out TerrainDecal pool)
    {
        uint key = TerrainDecalCells.Key(at.X, at.Z);
        var (x, z) = TerrainDecalCells.Origin(key);
        short localX = (short)(int)((at.X - x) / TerrainDecalCells.Size * 1000);
        short localZ = (short)(int)((at.Z - z) / TerrainDecalCells.Size * 1000);
        foreach (var decal in store.In(key))
        {
            if (decal.Type != TerrainDecalType.Pool || decal.LocalX != localX || decal.LocalZ != localZ) continue;
            pool = decal;
            return true;
        }
        pool = default;
        return false;
    }

    static FieldInfo? Field(Type? type, string name) => type is null ? null : AccessTools.Field(type, name);
}
