using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Objects;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Tracks freshly severed organic limbs when their landing is handled by the stock renderer.</summary>
internal sealed class StockNativeLimbPools(GameDatabase data, FieldInfo sessionField, MethodInfo limbLook)
{
    static StockNativeLimbPools? installed;
    readonly FieldInfo session = sessionField;

    internal static bool Install(ModContext context)
    {
        if (installed is not null) return true;
        try
        {
            var owner = StockHostParts.Debris;
            var debris = owner.FieldType;
            var rest = AccessTools.Method(debris, "RestLimb", [typeof(GroundItemSnapshot), typeof(Quaternion), typeof(float)])
                ?? throw new MissingMethodException(debris.Name, "RestLimb");
            var look = AccessTools.Method(debris, "LimbLookOf", [typeof(string)])
                ?? throw new MissingMethodException(debris.Name, "LimbLookOf");
            var session = StockHostParts.Field(owner.DeclaringType!, "Session");
            context.Harmony.Patch(rest, postfix: new HarmonyMethod(typeof(StockNativeLimbPools), nameof(Rested)));
            installed = new(context.Data, session, look);
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock native limb puddles unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static void Rested(object __instance, GroundItemSnapshot g)
    {
        // Saved items and limbs dropped from an inventory or a dog's mouth have no fall source.
        if (installed is not { } hooks || !g.Dropped || g.Fall is not { } cut || g.Type == FcsRecordType.LimbReplacement
            || StockHostParts.HostOf(__instance) is not { } host || hooks.session.GetValue(host) is not WorldSession shown)
            return;
        var race = shown.CurrentSnapshot.Characters.FirstOrDefault(actor => actor.Id == cut.Character)?.Appearance?.Race
            ?? hooks.GetLimbRace(__instance, g.Record);
        if (race is null || !StockDetachedPools.CanBleed(race)) return;

        var limb = race.GetReferences("severed limbs")
            .FirstOrDefault(reference => reference.TargetStringId == g.Record && reference.Values.Value0 is >= 0 and <= 3);
        if (limb.TargetStringId is null) return;
        // The published item position was grounded at the drop. RestLimb's Y is the mesh origin above it.
        if (float.IsFinite(g.Position.LengthSquared()))
            StockDetachedPools.NativeLimb(host, shown, g.Id, g.Position, race, limb.Values.Value0 >= 2);
    }

    GameRecord? GetLimbRace(object debris, string item)
        => limbLook.Invoke(debris, [item]) is LimbLook { Race: { } id } ? data.Find(id) : null;
}
