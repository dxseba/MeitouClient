using System.Numerics;
using System.Reflection;
using Meitou.Data.World;
using Meitou.Simulation;

namespace Meitou.Earthworks;

/// <summary>
/// The cleared circles of the Earthworks buildings, given to the game as extra player outposts: an owned outpost's circle keeps <c>avoid towns</c>
/// scenery away and keeps resource rocks (iron and copper) in place (docs/formats/foliage.md "Towns"), and the navmesh and the foliage renderer
/// both follow the list. Host thread only (the game's building sync runs them).
/// </summary>
static class Clearings
{
    /// <summary>The original's building records (Earthworks.mod) and what each is (native_earthworks.lua).</summary>
    public static readonly Dictionary<string, string> Kinds = new()
    {
        ["880010-TerrainLab.mod"] = "Leveling",
        ["880011-TerrainLab.mod"] = "Passage",
        ["880012-TerrainLab.mod"] = "Clearing",
    };

    /// <summary>The original's clearing steps: progress counts in sixteenths.</summary>
    const int Steps = 16;

    /// <summary>The level's towns (centre and no-foliage range): a clearing inside one is left out, so it never becomes the nearest town there.</summary>
    public static IReadOnlyList<(Vector2 At, float Range)> Towns = [];

    /// <summary>The building sync's <c>_foliageOutposts</c>, emptied to have it give the towns again.</summary>
    public static FieldInfo? Outposts;

    public static Action<string>? Log;

    static IReadOnlyList<FoliageTown> s_current = [];
    static bool s_logged;

    /// <summary>
    /// Harmony prefix on <c>BuildingSync.UpdateFoliageTowns</c>: the clearings from the snapshot's buildings; when they changed, the sync's
    /// remembered outposts are dropped so it gives the towns (and so the clearings) again this time.
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter name
    public static void UpdatePrefix(object __instance, WorldSnapshot snapshot)
#pragma warning restore IDE1006
    {
        try
        {
            var next = EarthworksMod.Settings.Enabled ? From(snapshot) : [];
            if (next.SequenceEqual(s_current)) return;

            s_current = next;
            Outposts?.SetValue(__instance, null);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Harmony prefix on <c>NavSystem.SetPlayerTowns</c>: the clearings after the outposts, those inside an outpost's circle left out.</summary>
    public static void GivePrefix(ref IReadOnlyList<FoliageTown> towns)
    {
        try
        {
            if (s_current.Count == 0) return;

            var outposts = towns;
            var added = s_current.Where(c => !outposts.Any(o => Vector2.Distance(o.At, c.At) < o.Range));
            towns = [.. outposts, .. added];
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// The circles covering each Earthworks building's area (a rectangle turned with the building, its own size or the settings' default) for a
    /// building not destroyed or being dismantled, outside the level's towns. The rectangle grows with construction in sixteenths of its area
    /// (a finished one clears it whole).
    /// </summary>
    static List<FoliageTown> From(WorldSnapshot snapshot)
    {
        var list = new List<FoliageTown>();
        var areas = AreaSystem.Published;
        foreach (var b in snapshot.Buildings)
        {
            if (!Kinds.ContainsKey(b.Record) || b.Destroyed || b.Dismantling) continue;

            var at = new Vector2(b.Position.X, b.Position.Z);
            if (Towns.Any(t => t.Range > 0 && Vector2.Distance(t.At, at) < t.Range)) continue;

            float done = b.Complete ? 1 : b.Target > 0 ? Math.Clamp(b.Progress / b.Target, 0, 1) : 0;
            int step = (int)(done * Steps);
            if (step == 0) continue;

            var size = areas.TryGetValue(b.Id, out var own) ? own : EarthworksMod.Settings.DefaultSize;
            Cover(list, at, b.Rotation, size * MathF.Sqrt(step / (float)Steps));
        }

        return list;
    }

    /// <summary>Grid spacing of the circles covering a rectangle; each circle reaches its cell's corners.</summary>
    const float Spacing = 144;

    /// <summary>
    /// Covers the rectangle <paramref name="size"/> (across, along) centred at <paramref name="at"/> and turned as <paramref name="rotation"/>
    /// with owned circles on a grid: the nearest circle decides a point, so every point inside lies within its nearest circle.
    /// </summary>
    public static void Cover(List<FoliageTown> list, Vector2 at, Quaternion rotation, Vector2 size)
    {
        var across = Flat(Vector3.Transform(Vector3.UnitX, rotation), Vector2.UnitX);
        var along = new Vector2(-across.Y, across.X);
        int nx = Math.Max(1, (int)MathF.Ceiling(size.X / Spacing)), nz = Math.Max(1, (int)MathF.Ceiling(size.Y / Spacing));
        float cx = size.X / nx, cz = size.Y / nz;
        float radius = MathF.Sqrt(cx * cx + cz * cz) / 2 + 1;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                var local = new Vector2((i + .5f) * cx - size.X / 2, (j + .5f) * cz - size.Y / 2);
                list.Add(new FoliageTown(at + across * local.X + along * local.Y, radius, Owned: true));
            }
        }
    }

    static Vector2 Flat(Vector3 v, Vector2 fallback)
    {
        var flat = new Vector2(v.X, v.Z);
        return flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : fallback;
    }

    static void Fault(Exception e)
    {
        if (!s_logged) Log?.Invoke($"clearing failed, the scenery stays as the game places it: {e.Message}");
        s_logged = true;
    }
}
