using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Shows landed weapons through stock debris when a flat arena has no world-object renderer.</summary>
internal sealed class StockCutEquipmentDraw(GameDatabase data, FieldInfo session, FieldInfo gpu, FieldInfo debris,
    FieldInfo pieces, MethodInfo activeObjects)
{
    sealed class Landed(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<int, DebrisPiece> Pieces = [];
        public long Revision = -1;
    }

    static StockCutEquipmentDraw? installed;
    readonly ConditionalWeakTable<object, Landed> worlds = new();

    readonly GameDatabase database = data;
    readonly FieldInfo sessionField = session, gpuField = gpu, debrisField = debris, piecesField = pieces;
    readonly MethodInfo objectsGetter = activeObjects;

    internal static void Install(ModContext context)
    {
        if (installed is not null) return;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var gpu = AccessTools.Field(host, "gpu");
            var draw = new StockCutEquipmentDraw(context.Data, AccessTools.Field(host, "session"), gpu,
                AccessTools.Field(gpu.FieldType, "Debris"), AccessTools.Field(gpu.FieldType, "DebrisPieces"),
                AccessTools.PropertyGetter(host, "ActiveObjects"));
            var show = AccessTools.Method(host, "ShowGround", [typeof(GroundItemSnapshot), typeof(Quaternion), typeof(float)]);
            context.Harmony.Patch(show, postfix: new(typeof(StockCutEquipmentDraw), nameof(Rest)));
            context.Harmony.Patch(AccessTools.Method(host, "UpdateModHits"), postfix: new(typeof(StockCutEquipmentDraw), nameof(Frame)));
            installed = draw;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock arena weapons unavailable: {error.GetBaseException().Message}");
        }
    }

    static void Rest(object __instance, GroundItemSnapshot g, Quaternion rotation, float y)
    {
        if (installed is not { } draw || g.Type is not (FcsRecordType.WEAPON or FcsRecordType.CROSSBOW)
            || draw.objectsGetter.Invoke(__instance, null) is not null || draw.sessionField.GetValue(__instance) is not WorldSession shown
            || draw.gpuField.GetValue(__instance) is not { } frame || draw.debrisField.GetValue(frame) is not DebrisRenderer renderer
            || draw.database.Find(g.Record) is not { } record) return;
        string mesh = WorldObjectLayout.ItemMesh(record);
        if (renderer.Centre(mesh) is not { } local) return;
        var centre = new Vector3(g.Position.X, y, g.Position.Z) + Vector3.Transform(local, rotation);
        if (draw.worlds.TryGetValue(__instance, out var stale) && !ReferenceEquals(stale.World, shown.World))
            draw.worlds.Remove(__instance);
        draw.worlds.GetValue(__instance, _ => new(shown.World)).Pieces[g.Id] = new(mesh, 1, rotation, centre);
    }

    static void Frame(object __instance)
    {
        if (installed is not { } draw || draw.gpuField.GetValue(__instance) is not { } frame
            || draw.debrisField.GetValue(frame) is not DebrisRenderer native) return;
        if (!draw.worlds.TryGetValue(__instance, out var landed) || draw.sessionField.GetValue(__instance) is not WorldSession shown
            || !ReferenceEquals(landed.World, shown.World))
        {
            draw.worlds.Remove(__instance);
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
            StockFlatWeaponTint.Queue(__instance, shown, native, landed.Pieces, list);
    }
}
