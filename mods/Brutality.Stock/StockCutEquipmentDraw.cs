using Meitou.Game.Host;
using Meitou.Game.Rendering.Objects;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Shows landed weapons through stock debris when a flat arena has no world-object renderer.</summary>
internal sealed class StockCutEquipmentDraw(GameDatabase data, FieldInfo session, FieldInfo gpu, FieldInfo debris,
    FieldInfo pieces, MethodInfo activeObjects, FieldInfo assets, FieldInfo falls, FieldInfo fallMesh)
{
    sealed class Landed(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<int, DebrisPiece> Pieces = [];
        public long Revision = -1;
    }

    static StockCutEquipmentDraw? installed;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly ConditionalWeakTable<object, Landed> worlds = new();

    readonly GameDatabase database = data;
    readonly FieldInfo sessionField = session, gpuField = gpu, debrisField = debris, piecesField = pieces;
    readonly FieldInfo assetsField = assets, fallsField = falls, fallMeshField = fallMesh;
    readonly MethodInfo objectsGetter = activeObjects;

    internal static void Install(ModContext context)
    {
        if (installed is not null) return;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var gpu = AccessTools.Field(host, "Gpu");
            var falls = StockHostParts.Field(StockHostParts.Debris.FieldType, "Falls");
            var draw = new StockCutEquipmentDraw(context.Data, AccessTools.Field(host, "Session"), gpu,
                AccessTools.Field(gpu.FieldType, "Debris"), AccessTools.Field(gpu.FieldType, "DebrisPieces"),
                AccessTools.PropertyGetter(host, "ActiveObjects"),
                AccessTools.GetDeclaredFields(host).Single(field => field.FieldType == typeof(AssetLocator)),
                falls, StockHostParts.Field(falls.FieldType.GetGenericArguments()[0], "Mesh"));
            var show = AccessTools.Method(StockHostParts.GroundItems.FieldType, "ShowGround", [typeof(GroundItemSnapshot), typeof(Quaternion), typeof(float)]);
            var startFall = AccessTools.Method(StockHostParts.GroundItems.FieldType, "StartFall",
                [typeof(GroundItemSnapshot), typeof(FallSource), typeof(Quaternion), typeof(float)]);
            context.Harmony.Patch(show, postfix: new(typeof(StockCutEquipmentDraw), nameof(Rest)));
            context.Harmony.Patch(startFall, postfix: new(typeof(StockCutEquipmentDraw), nameof(Fall)));
            context.Harmony.Patch(AccessTools.Method(StockHostParts.Field(host, "Modding").FieldType, "UpdateModHits"), postfix: new(typeof(StockCutEquipmentDraw), nameof(Frame)));
            installed = draw;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock arena weapons unavailable: {error.GetBaseException().Message}");
        }
    }

    static void Fall(object __instance, GroundItemSnapshot g, bool __result)
    {
        if (!__result || g.Type is not (FcsRecordType.Weapon or FcsRecordType.Crossbow)
            || installed is not { } draw || StockHostParts.HostOf(__instance) is not { } host
            || draw.sessionField.GetValue(host) is not WorldSession shown || draw.database.Find(g.Record) is not { } record
            || draw.gpuField.GetValue(host) is not { } frame || draw.debrisField.GetValue(frame) is not DebrisRenderer renderer
            || draw.fallsField.GetValue(StockHostParts.Debris.GetValue(host)) is not IList { Count: > 0 } active) return;
        string mesh = draw.WeaponMesh(host, shown.World, g.Id, record);
        if (!mesh.Contains(DebrisPiece.TextureMark) || renderer.Centre(mesh) is null) return;
        draw.fallMeshField.SetValue(active[active.Count - 1], mesh);
        if (trace) Console.WriteLine($"Brutality stock falling weapon id={g.Id} mesh={mesh}");
    }

    static void Rest(object __instance, GroundItemSnapshot g, Quaternion rotation, float y)
    {
        if (installed is not { } draw || StockHostParts.HostOf(__instance) is not { } host || g.Type is not (FcsRecordType.Weapon or FcsRecordType.Crossbow)
            || draw.objectsGetter.Invoke(host, null) is not null || draw.sessionField.GetValue(host) is not WorldSession shown
            || draw.gpuField.GetValue(host) is not { } frame || draw.debrisField.GetValue(frame) is not DebrisRenderer renderer
            || draw.database.Find(g.Record) is not { } record) return;
        string mesh = draw.WeaponMesh(host, shown.World, g.Id, record);
        if (renderer.Centre(mesh) is not { } local) return;
        var centre = new Vector3(g.Position.X, y, g.Position.Z) + Vector3.Transform(local, rotation);
        if (draw.worlds.TryGetValue(host, out var stale) && !ReferenceEquals(stale.World, shown.World))
            draw.worlds.Remove(host);
        draw.worlds.GetValue(host, _ => new(shown.World)).Pieces[g.Id] = new(mesh, 1, rotation, centre);
        if (trace) Console.WriteLine($"Brutality stock landed weapon id={g.Id} mesh={mesh}");
    }

    string WeaponMesh(object host, World world, int id, GameRecord record)
    {
        string mesh = WorldObjectLayout.ItemMesh(record);
        return assetsField.GetValue(host) is AssetLocator art
            && StockWeaponGround.DiffuseOf(database, art, record, StockWeaponGround.MaterialId(world, id)) is { } diffuse
            ? mesh + DebrisPiece.TextureMark + diffuse : mesh;
    }

    static void Frame(object __instance)
    {
        if (installed is not { } draw || StockHostParts.HostOf(__instance) is not { } host || draw.gpuField.GetValue(host) is not { } frame
            || draw.debrisField.GetValue(frame) is not DebrisRenderer native) return;
        if (!draw.worlds.TryGetValue(host, out var landed) || draw.sessionField.GetValue(host) is not WorldSession shown
            || !ReferenceEquals(landed.World, shown.World))
        {
            draw.worlds.Remove(host);
            StockFlatWeaponTint.Clear(native);
            return;
        }
        var snapshot = shown.CurrentSnapshot;
        if (landed.Revision != snapshot.GroundItemsRevision)
        {
            landed.Revision = snapshot.GroundItemsRevision;
            var remaining = snapshot.GroundItems.Select(item => item.Id).ToHashSet();
            foreach (int id in landed.Pieces.Keys.Where(id => !remaining.Contains(id)).ToArray()) landed.Pieces.Remove(id);
        }
        if (draw.piecesField.GetValue(frame) is List<DebrisPiece> list)
            StockFlatWeaponTint.Queue(host, shown, native, landed.Pieces, list);
    }
}
