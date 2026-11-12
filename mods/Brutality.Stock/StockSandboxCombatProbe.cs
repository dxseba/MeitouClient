#if BRUTALITY_STOCK_SAVE_PROBE
using Meitou.Simulation.Population;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Controls sandbox opponents and native hit settings without manufacturing combat hits.</summary>
internal static class StockSandboxCombatProbe
{
    static string? foe, part;
    static float? multiplier;
    static bool delayedAttack, combined;
    static System.Reflection.FieldInfo? sceneHost, sceneSession;

    internal static void Install(ModContext context)
    {
        if (!Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)) return;
        string? name = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_FOE");
        if (!string.IsNullOrWhiteSpace(name))
            foe = context.Data.Records.Values.FirstOrDefault(record => record.Type == FcsRecordType.Character
                && (record.StringId.Equals(name, StringComparison.OrdinalIgnoreCase) || record.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))?.StringId
                ?? throw new ArgumentException($"Unknown probe opponent: {name}");
        part = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_PART");
        if (float.TryParse(Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_DAMAGE"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float scale) && float.IsFinite(scale) && scale is > 0 and <= 100) multiplier = scale;
        delayedAttack = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_DELAY") == "1";
        bool close = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_CLOSE") == "1";
        if (close)
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost");
            var scenes = AccessTools.TypeByName("Meitou.Game.DevTools.ScreenshotScenes");
            sceneHost = AccessTools.Field(scenes, "<host>P");
            sceneSession = AccessTools.Field(host, "Session");
            context.Harmony.Patch(AccessTools.Method(scenes, "StageScene"), postfix: new HarmonyMethod(typeof(StockSandboxCombatProbe), nameof(FocusFoe)));
            if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_TRACK_CHEST") == "1")
                context.Harmony.Patch(AccessTools.Method(host, "ApplyCamera"), prefix: new HarmonyMethod(typeof(StockSandboxCombatProbe), nameof(FocusChest)));
        }
        combined = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_COMBINED") == "1";
        if (foe is null && part is null && multiplier is null && !delayedAttack && !combined) return;
        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_NO_NATIVE_HEAD") == "1")
        {
            var decapitation = AccessTools.TypeByName("Meitou.Game.Effects.Decapitation");
            context.Harmony.Patch(AccessTools.Method(decapitation, "CutHead"), prefix: new HarmonyMethod(typeof(StockSandboxCombatProbe), nameof(WithholdNativeHead)));
        }
        context.Harmony.Patch(AccessTools.Method(typeof(PopulationSystem), nameof(PopulationSystem.SpawnSandbox)),
            prefix: new HarmonyMethod(typeof(StockSandboxCombatProbe), nameof(BeforeSpawn)),
            postfix: new HarmonyMethod(typeof(StockSandboxCombatProbe), nameof(AfterSpawn)));
    }

    static void FocusFoe(object __instance, ref Vector3? __result)
    {
        if (sceneHost?.GetValue(__instance) is not { } host || sceneSession?.GetValue(host) is not WorldSession session
            || session.CurrentSnapshot.Characters.FirstOrDefault(actor => actor.Id.Slot == 1) is not { } foe) return;
        __result = foe.Position + new Vector3(Offset("OFFSET_X", 0), Offset("RISE", 3), Offset("OFFSET_Z", 0));

        static float Offset(string name, float fallback) =>
            float.TryParse(Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_" + name),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed) ? parsed : fallback;
    }

    static void FocusChest(WorldSession ___Session, CharacterRigs? ___CharacterRigs, ref CameraState s)
    {
        if (___Session.CurrentSnapshot.Characters.FirstOrDefault(actor => actor.Id.Slot == 1) is not
            { Ragdoll: { } pose, Appearance: { } look } foe || ___CharacterRigs?.For(look) is not { } setup
            || setup.Rig.Handle("Bip01 Spine2") is not int chest) return;
        var rig = setup.Rig;
        var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(setup.Shape, [], pose);
            var world = CharacterRigs.Placement(setup, foe.Position, float.IsNaN(foe.DrawYaw) ? foe.Yaw : foe.DrawYaw, foe.Scale);
            var target = Vector3.Transform(scratch.DerivedPosition[chest], world);
            var chestRotation = scratch.DerivedRotation[chest] * rig.BindRotationInverse[chest];
            var front = Vector3.Normalize(Vector3.TransformNormal(Vector3.Transform(Vector3.UnitZ, chestRotation), world));
            float yaw = MathF.Atan2(front.X, front.Z);
            float pitch = MathF.Asin(Math.Clamp(front.Y, .11f, .995f));
            s = s with { Target = target, Yaw = yaw, Pitch = pitch };
            if (___Session.World.Tick is > 395 and < 415)
                Console.WriteLine(FormattableString.Invariant($"Brutality probe chest target={target} front={front}"));
        }
        finally { rig.Return(scratch); }
    }

    static bool WithholdNativeHead(CharacterSnapshot c, CharacterAppearance look) => c.Id.Slot != 1 || !look.Headless;

    static void BeforeSpawn(ref string recordId, bool player, int variant, int? faction)
    {
        if (!player && variant == 0 && faction is null && foe is not null) recordId = foe;
    }

    static void AfterSpawn(World world, bool player, int variant, int? faction, CharacterId __result)
    {
        if (combined && variant == 1 && world.Characters.TryResolveNext(__result, out int bystander))
        {
            var at = world.Characters.Next[bystander].Position;
            world.Commands.Enqueue(new DebugTeleport([__result], new(21, at.Y, 3)) { Tick = world.Tick + 110 });
            world.Commands.Enqueue(new DebugDamage([__result], 5000, Meitou.Data.Gameplay.Combat.ArmourData.ChestPartId) { Tick = world.Tick + 140 });
        }
        if (variant != 0 || faction is not null || !world.Characters.TryResolveNext(__result, out int slot)) return;
        if (!player)
        {
            if (delayedAttack)
                for (int i = 0; i < world.Characters.HighWater; i++)
                    if (world.Characters.Next[i].Alive && world.Characters.Cold(i)?.IsPlayer == true)
                    {
                        var hero = world.Characters.IdOf(i);
                        world.Commands.Enqueue(new AttackOrder([hero], __result) { Tick = world.Tick + 250 });
                        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_CLOSE") == "1")
                            world.Commands.Enqueue(new DebugTeleport([hero], world.Characters.Next[i].Position - Vector3.UnitX * 60)
                                { Tick = world.Tick + 380 });
                        break;
                    }
            return;
        }
        if (part is { } name && world.Characters.Cold(slot)?.Medical?.Parts.FirstOrDefault(body =>
            body.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } location)
            world.Commands.Enqueue(new DebugForceHits((location.Template.StringId, location.Template.Name)) { Tick = world.Tick });
        if (multiplier is { } damage)
            world.Commands.Enqueue(new DebugDamageMultiplier(damage) { Tick = world.Tick });
        Console.WriteLine(FormattableString.Invariant($"Brutality stock combat probe foe={foe} part={part} damage={multiplier}"));
    }
}
#endif
