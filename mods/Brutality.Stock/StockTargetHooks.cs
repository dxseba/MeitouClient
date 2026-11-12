using Meitou.Simulation.Player;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Lets only explicit focused orders through the stock down-target guards, with stock contact and damage.</summary>
internal static class StockTargetHooks
{
    static readonly FieldInfo Down = AccessTools.Field(typeof(CombatSlot), nameof(CombatSlot.Down));
    static readonly FieldInfo Target = AccessTools.Field(typeof(CombatSlot), nameof(CombatSlot.TargetSlot));
    static readonly FieldInfo Read = AccessTools.Field(typeof(CombatSystem), "_read");
    static readonly MethodInfo Characters = AccessTools.PropertyGetter(typeof(World), nameof(World.Characters));
    static readonly MethodInfo Incapacitated = AccessTools.PropertyGetter(typeof(MedicalState), nameof(MedicalState.Incapacitated));
    internal static bool ZoneAimReady { get; private set; }

    readonly record struct Contact(MedicalState Medical, int Part);
    [ThreadStatic] static Contact? contact;

    internal static bool Install(ModContext context)
    {
        var combat = typeof(CombatSystem);
        var zone = AccessTools.Inner(combat, "ZoneScan");
        var scan = combat.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SingleOrDefault(method => method.Name.StartsWith("<Scan>", StringComparison.Ordinal)
                && zone is not null && method.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(int), zone.MakeByRefType()]));
        var act = AccessTools.Method(combat, "ReviewTarget");
        var mode = AccessTools.Method(combat, "ChooseMode");
        var apply = AccessTools.Method(combat, nameof(CombatSystem.Apply), [typeof(World), typeof(Effect).MakeByRefType()]);
        var choose = AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ChoosePart),
            [typeof(MedicalContext).MakeByRefType(), typeof(bool), typeof(bool)]);
        if (scan is null || act is null || mode is null || apply is null || choose is null)
        { context.Log("Brutality body-part targeting unavailable on this host"); return false; }

        var patches = new (MethodInfo Original, string Hook, bool Transpiler)[]
        {
            (act, nameof(AllowInAct), true), (scan, nameof(AllowInZone), true), (mode, nameof(KeepMelee), true),
            (apply, nameof(ScopeContact), false), (choose, nameof(SelectPart), false),
        };
        var installed = new List<(MethodInfo Original, MethodInfo Hook)>();
        try
        {
            foreach (var (original, name, transpiler) in patches)
            {
                var method = AccessTools.Method(typeof(StockTargetHooks), name);
                var hook = new HarmonyMethod(method);
                installed.Add((original, method));
                if (transpiler) context.Harmony.Patch(original, transpiler: hook);
                else if (original == apply)
                {
                    var end = AccessTools.Method(typeof(StockTargetHooks), nameof(CloseContact));
                    installed.Add((original, end));
                    context.Harmony.Patch(original, prefix: hook, finalizer: new HarmonyMethod(end));
                }
                else context.Harmony.Patch(original, prefix: hook);
            }
        }
        catch (Exception error)
        {
            foreach (var (original, hook) in installed.AsEnumerable().Reverse()) context.Harmony.Unpatch(original, hook);
            context.Log($"Brutality body-part targeting unavailable: {error.GetBaseException().Message}");
            return false;
        }
        StockTargetSystem.Install(context);
        StockTargetUi.Install(context);
        StockTargetGroundSwing.Install(context);
        StockTargetAim.Install(context);
        context.Log("Brutality stock body-part targeting initialized");
        return true;
    }

    static bool Unavailable(bool native, CharacterTable table, int attacker, int victim) =>
        StockTargetSystem.TryFocus(table, attacker, victim, out var order, out var focus)
            ? !StockTargetSystem.Allowed(table, attacker, order, focus) : native;

    static bool ZoneUnavailable(bool native, CharacterTable table, int attacker, int candidate, int target)
    {
        if (candidate != target && StockTargetSystem.TryFocus(table, attacker, target, out _, out var focus) && focus.Part >= 0) return true;
        return Unavailable(native, table, attacker, candidate);
    }

    static bool Ranged(bool native, World world, int attacker, in CombatSlot slot) => native
        && (!slot.Picked || !StockTargetSystem.TryFocus(world.Characters, attacker, slot.TargetSlot, out var order, out var focus)
            || !StockTargetSystem.Allowed(world.Characters, attacker, order, focus)
            || !StockTargetSystem.GroundTarget(world, order.Target));

    static CodeInstruction Call(string name) => new(OpCodes.Call, AccessTools.Method(typeof(StockTargetHooks), name));
    static int Single(List<CodeInstruction> code, Func<int, bool> match, string name)
    {
        var positions = Enumerable.Range(0, code.Count).Where(match).ToArray();
        return positions.Length == 1 ? positions[0] : throw new InvalidOperationException($"Unexpected stock combat guard: {name}.");
    }

    static IEnumerable<CodeInstruction> AllowInAct(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        int down = Single(code, i => i >= 4 && code[i].LoadsField(Down)
            && code[i - 2].LoadsField(Target) && code[i - 4].LoadsField(Read), "ReviewTarget Down");
        int medical = Single(code, i => code[i].Calls(Incapacitated), "ReviewTarget Incapacitated");
        // ReviewTarget receives World, attacker and the current CombatSlot by reference.
        CodeInstruction[] Query() => [new(OpCodes.Ldarg_1), new(OpCodes.Callvirt, Characters), new(OpCodes.Ldarg_2),
            new(OpCodes.Ldarg_3), new(OpCodes.Ldfld, Target), Call(nameof(Unavailable))];
        code.InsertRange(medical + 1, Query());
        code.InsertRange(down + 1, Query());
        return ExpandBranches(code);
    }

    static IEnumerable<CodeInstruction> AllowInZone(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
    {
        var code = instructions.ToList();
        int down = Single(code, i => code[i].LoadsField(Down), "Scan target Down");
        int medical = Single(code, i => code[i].Calls(Incapacitated), "Scan target Incapacitated");
        var zone = __originalMethod.GetParameters()[1].ParameterType.GetElementType()!;
        var table = AccessTools.Field(zone, "Table");
        var attacker = AccessTools.Field(zone, "Self");
        var target = AccessTools.Field(zone, "Target");
        int candidateArg = __originalMethod.IsStatic ? 0 : 1, zoneArg = candidateArg + 1;
        CodeInstruction[] Query() => [CodeInstruction.LoadArgument(zoneArg), new(OpCodes.Ldfld, table),
            CodeInstruction.LoadArgument(zoneArg), new(OpCodes.Ldfld, attacker), CodeInstruction.LoadArgument(candidateArg),
            CodeInstruction.LoadArgument(zoneArg), new(OpCodes.Ldfld, target), Call(nameof(ZoneUnavailable))];
        code.InsertRange(medical + 1, Query());
        code.InsertRange(down + 1, Query());
        var position = AccessTools.Field(typeof(CharacterHot), nameof(CharacterHot.Position));
        var footprint = AccessTools.Method(typeof(CombatTuning), "Footprint");
        int where = code.FindIndex(op => op.LoadsField(position)), width = code.FindIndex(op => op.Calls(footprint));
        ZoneAimReady = false;
        if (where >= 0 && width > where)
        {
            int radius = code.FindIndex(width, op => op.opcode == OpCodes.Mul);
            if (radius > width)
            {
                int point = generator.DeclareLocal(typeof(Vector3?)).LocalIndex;
                code.InsertRange(radius + 1, [CodeInstruction.LoadLocal(point), Call(nameof(FocusedRadius))]);
                code.InsertRange(where + 1, [CodeInstruction.LoadArgument(zoneArg), new(OpCodes.Ldfld, table),
                    CodeInstruction.LoadArgument(zoneArg), new(OpCodes.Ldfld, attacker), CodeInstruction.LoadArgument(candidateArg),
                    new(OpCodes.Call, AccessTools.Method(typeof(StockTargetAim), nameof(StockTargetAim.ZonePoint))),
                    new(OpCodes.Dup), CodeInstruction.StoreLocal(point), Call(nameof(FocusedPosition))]);
                ZoneAimReady = true;
            }
        }
        return ExpandBranches(code);
    }

    static Vector3 FocusedPosition(Vector3 fallback, Vector3? point) => point ?? fallback;
    static float FocusedRadius(float fallback, Vector3? point) => point is null ? fallback : 1f;

    static IEnumerable<CodeInstruction> KeepMelee(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var mode = AccessTools.Method(typeof(Orders), nameof(Orders.RangedMode));
        int index = Single(code, i => code[i].Calls(mode), "ChooseMode RangedMode");
        code.InsertRange(index + 1, [new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2), new(OpCodes.Ldarg_3), Call(nameof(Ranged))]);
        return ExpandBranches(code);
    }

    // Dynamic method emission does not resize branches after extra instructions are inserted.
    internal static List<CodeInstruction> ExpandBranches(List<CodeInstruction> code)
    {
        foreach (var op in code)
        {
            if (op.opcode == OpCodes.Br_S) op.opcode = OpCodes.Br;
            else if (op.opcode == OpCodes.Brfalse_S) op.opcode = OpCodes.Brfalse;
            else if (op.opcode == OpCodes.Brtrue_S) op.opcode = OpCodes.Brtrue;
            else if (op.opcode == OpCodes.Beq_S) op.opcode = OpCodes.Beq;
            else if (op.opcode == OpCodes.Bne_Un_S) op.opcode = OpCodes.Bne_Un;
            else if (op.opcode == OpCodes.Bge_S) op.opcode = OpCodes.Bge;
            else if (op.opcode == OpCodes.Bge_Un_S) op.opcode = OpCodes.Bge_Un;
            else if (op.opcode == OpCodes.Bgt_S) op.opcode = OpCodes.Bgt;
            else if (op.opcode == OpCodes.Bgt_Un_S) op.opcode = OpCodes.Bgt_Un;
            else if (op.opcode == OpCodes.Ble_S) op.opcode = OpCodes.Ble;
            else if (op.opcode == OpCodes.Ble_Un_S) op.opcode = OpCodes.Ble_Un;
            else if (op.opcode == OpCodes.Blt_S) op.opcode = OpCodes.Blt;
            else if (op.opcode == OpCodes.Blt_Un_S) op.opcode = OpCodes.Blt_Un;
            else if (op.opcode == OpCodes.Leave_S) op.opcode = OpCodes.Leave;
        }
        return code;
    }

    static bool ScopeContact(World world, in Effect e, out Contact? __state)
    {
        __state = contact;
        contact = null;
        var table = world.Characters;
        if (e.Kind != CombatSystem.BlowEffect || !StockTargetSystem.TryFocus(table, e.Source, e.Target, out var order, out var focus)) return true;
        if (!StockTargetSystem.Allowed(table, e.Source, order, focus) || StockTargetAim.ContactBlocked(world, e.Source, e.Target)) return false;
        if (focus.Part >= 0) contact = new(table.Cold(e.Target)!.Medical!, focus.Part);
        return true;
    }

    static Exception? CloseContact(Exception? __exception, Contact? __state)
    { contact = __state; return __exception; }

    static bool SelectPart(MedicalState __instance, ref int __result)
    {
        if (contact is not { } exact || !ReferenceEquals(__instance, exact.Medical)) return true;
        var part = __instance.Parts[exact.Part];
        // Apply the same per-part hit weight update as the stock chooser, skipping its random selection only.
        if (part.Flesh <= -part.MaxHp) part.HitMult = 0;
        else
        {
            float cap = part.Fraction < 0 ? 1 : 4;
            if (cap == 1) part.HitMult = MathF.Min(part.HitMult, 2);
            if (part.HitMult < cap) part.HitMult += 1;
        }
        __result = exact.Part;
        return false;
    }
}
