using Meitou.Simulation.Player;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Substitutes a posed hit point inside the stock path, swing and sweep calculations for an exact attack order.</summary>
internal static class StockTargetAim
{
    static readonly FieldInfo position = AccessTools.Field(typeof(CharacterHot), nameof(CharacterHot.Position));
    static readonly FieldInfo targetSlot = AccessTools.Field(typeof(CombatSlot), nameof(CombatSlot.TargetSlot));
    static readonly MethodInfo gap = AccessTools.Method(typeof(CombatTuning), "Gap"), footprint = AccessTools.Method(typeof(CombatTuning), "Footprint"),
        otherFloor = AccessTools.Method(typeof(CombatSystem), "OtherFloor"), stand = AccessTools.Method(typeof(CombatTuning), nameof(CombatTuning.StandGap));
    static readonly Func<Vector3, Vector3, CharacterCold?, CharacterCold?, float> nativeGap = gap.CreateDelegate<Func<Vector3, Vector3, CharacterCold?, CharacterCold?, float>>();
    static bool enabled;

    internal static void Install(ModContext context)
    {
        var combat = typeof(CombatSystem);
        var pursuit = typeof(PursuitSystem);
        var methods = new (MethodInfo Method, string Hook)[]
        {
            (AccessTools.Method(combat, "StartAttack"), nameof(StartAttack)),
            (AccessTools.Method(combat, nameof(CombatSystem.SwingAim)), nameof(SwingAim)),
            (AccessTools.Method(combat, nameof(CombatSystem.SteersAt)), nameof(SteeringLine)),
            (AccessTools.Method(pursuit, "Chase"), nameof(Chase)),
            (AccessTools.Method(pursuit, "Steer"), nameof(Steer)),
        };
        var installed = new List<(MethodInfo Method, MethodInfo Hook)>();
        try
        {
            if (!StockTargetHooks.ZoneAimReady) throw new InvalidOperationException("No posed contact point in stock sweep.");
            foreach (var (method, hook) in methods)
            {
                var patch = AccessTools.Method(typeof(StockTargetAim), hook);
                installed.Add((method, patch));
                context.Harmony.Patch(method, transpiler: new HarmonyMethod(patch));
            }
            enabled = true;
        }
        catch (Exception error)
        {
            foreach (var (method, patch) in installed.AsEnumerable().Reverse())
                if (method is not null) context.Harmony.Unpatch(method, patch);
            context.Log($"Brutality posed attack aim unavailable: {error.GetBaseException().Message}");
        }
    }

    static Vector3? Point(World world, int attacker, int target) => enabled ? StockTargetSystem.AimPoint(world.Characters, attacker, target) : null;
    internal static Vector3? ZonePoint(CharacterTable table, int attacker, int target) => enabled ? StockTargetSystem.AimPoint(table, attacker, target) : null;
    static Vector3 At(Vector3 native, Vector3? point) => point ?? native;
    static Vector3 AtFloor(Vector3 native, Vector3? point) => point is { } limb ? new(limb.X, native.Y, limb.Z) : native;
    static float PartGap(Vector3 from, Vector3 aim, CharacterCold? me, CharacterCold? target, Vector3? point) => point is { } limb
        ? MathF.Max(0, Vector3.Distance(from, limb) - 1.5f * (me?.Race?.PathfindFootprintRadius ?? CombatTuning.DefaultFootprint) * (me?.Medical?.ScaleK ?? 1))
        : nativeGap(from, aim, me, target);
    static float AimFootprint(float native, Vector3? point) => point is null ? native : 0;
    static float AimStand(float native, Vector3? point) => point is null ? native : 1;
    static float AimRepath(float native, Vector3? point) => point is null ? native : 4;
    static float AimTurn(float native, Vector3? point) => point is null ? native : .0025f;
    static int AimFace(int native, Vector3? point) => point is null ? native : -1;
    static bool DisableRootSteering(bool native, World world, int attacker, int target) => native || Point(world, attacker, target) is not null;
    static bool Blocked(bool native, World world, int attacker, int target, Vector3? point) => native || point is not null
        && world.Systems.OfType<CombatSystem>().FirstOrDefault() is { } combat && !combat.SteersAt(world, attacker, target);
    internal static bool ContactBlocked(World world, int attacker, int target) =>
        Blocked(false, world, attacker, target, Point(world, attacker, target));

    static IEnumerable<CodeInstruction> StartAttack(IEnumerable<CodeInstruction> source, ILGenerator il)
    {
        var code = source.ToList();
        var (target, point) = CapturePoint(code, il, attacker: 2);
        var positions = code.Where((_, i) => PositionOf(code, i, target)).ToArray();
        if (positions.Length != 2) throw new InvalidOperationException("Unexpected attack aim positions.");
        ChangePosition(code, positions[1], il, [Load(point)], nameof(At)); // first use is the native floor check
        RewriteGap(code, point);
        RequireClearLine(code, point, target, attacker: 2);
        return StockTargetHooks.ExpandBranches(code);
    }

    static IEnumerable<CodeInstruction> SwingAim(IEnumerable<CodeInstruction> source, ILGenerator il)
    {
        var code = source.ToList();
        var (target, point) = CapturePoint(code, il, attacker: 2);
        var positions = code.Where((_, i) => PositionOf(code, i, target)).ToArray();
        if (positions.Length != 2) throw new InvalidOperationException("Unexpected swing aim positions.");
        foreach (var use in positions) ChangePosition(code, use, il, [Load(point)], nameof(At));
        foreach (var threshold in code.Where(op => op.opcode == OpCodes.Ldc_R4 && op.operand is 25f or 8f).ToArray())
            Append(code, threshold, Load(point), Call(nameof(AimTurn)));
        return StockTargetHooks.ExpandBranches(code);
    }

    static IEnumerable<CodeInstruction> SteeringLine(IEnumerable<CodeInstruction> source, ILGenerator il)
    {
        var code = source.ToList();
        var loads = code.Where(op => op.operand is FieldInfo field && field == position).ToArray();
        if (loads.Length != 2) throw new InvalidOperationException("Unexpected steering line positions.");
        ChangePosition(code, loads[1], il, [Arg(1), Arg(2), Arg(3), Call(nameof(Point))], nameof(AtFloor));
        return StockTargetHooks.ExpandBranches(code);
    }

    static IEnumerable<CodeInstruction> Chase(IEnumerable<CodeInstruction> source, ILGenerator il)
    {
        var code = source.ToList();
        const int attacker = 3, target = 6;
        int point = il.DeclareLocal(typeof(Vector3?)).LocalIndex;
        Insert(code, code[0], Arg(1), Arg(attacker), Arg(target), Call(nameof(Point)), CodeInstruction.StoreLocal(point));
        int floor = code.FindIndex(op => op.Calls(otherFloor));
        if (floor < 0) throw new InvalidOperationException("Missing stock pursuit floor check.");
        var positions = code.Where((op, i) => i >= 2 && i != floor - 1 && op.operand is FieldInfo field && field == position
            && code[i - 2].IsLdarg(target) && code[i - 1].operand is MethodInfo item && item.Name == "get_Item").ToArray();
        if (positions.Length < 5) throw new InvalidOperationException("Unexpected pursuit aim positions.");
        foreach (var use in positions) ChangePosition(code, use, il, [Load(point)], nameof(AtFloor));

        // UpdateRunning owns the running-distance calculation. Here only the target radius in aimGap changes.
        var radii = code.Where(op => op.Calls(footprint)).ToArray();
        if (radii.Length != 2) throw new InvalidOperationException("Unexpected stock pursuit footprints.");
        Append(code, radii[1], Load(point), Call(nameof(AimFootprint)));
        Append(code, code.Where(op => op.Calls(stand)).Last(), Load(point), Call(nameof(AimStand)));
        Append(code, code.Single(op => op.opcode == OpCodes.Ldc_R4 && op.operand is 144f), Load(point), Call(nameof(AimRepath)));
        var face = code.Single(op => op.Calls(AccessTools.PropertySetter(typeof(CharacterCold), nameof(CharacterCold.FaceSlot))));
        Insert(code, face, Load(point), Call(nameof(AimFace)));
        RewriteGap(code, point);
        Append(code, code.Single(op => op.Calls(otherFloor)), Arg(1), Arg(attacker), Arg(target), Load(point), Call(nameof(Blocked)));
        return StockTargetHooks.ExpandBranches(code);
    }

    static IEnumerable<CodeInstruction> Steer(IEnumerable<CodeInstruction> source)
    {
        var code = source.ToList();
        var running = AccessTools.PropertyGetter(typeof(CharacterCold), nameof(CharacterCold.CombatRunning));
        var check = code.First(op => op.Calls(running));
        Append(code, check, Arg(1), Arg(2), Arg(6), Call(nameof(DisableRootSteering)));
        return StockTargetHooks.ExpandBranches(code);
    }

    static (int Target, int Point) CapturePoint(List<CodeInstruction> code, ILGenerator il, int attacker)
    {
        int read = code.FindIndex(op => op.LoadsField(targetSlot));
        if (read < 0 || read + 1 >= code.Count || !code[read + 1].IsStloc()) throw new InvalidOperationException("No stock target local.");
        var store = code[read + 1];
        int target = store.LocalIndex(), point = il.DeclareLocal(typeof(Vector3?)).LocalIndex;
        Append(code, store, Arg(1), Arg(attacker), Load(target), Call(nameof(Point)), CodeInstruction.StoreLocal(point));
        return (target, point);
    }

    static bool PositionOf(List<CodeInstruction> code, int index, int target) => index >= 2
        && code[index].operand is FieldInfo field && field == position && code[index - 2].IsLdloc()
        && code[index - 2].LocalIndex() == target && code[index - 1].operand is MethodInfo item && item.Name == "get_Item";

    static void ChangePosition(List<CodeInstruction> code, CodeInstruction at, ILGenerator il, CodeInstruction[] args, string select)
    {
        var use = args.Append(Call(select)).ToList();
        if (at.opcode == OpCodes.Ldflda)
        {
            at.opcode = OpCodes.Ldfld;
            int temporary = il.DeclareLocal(typeof(Vector3)).LocalIndex;
            use.Add(CodeInstruction.StoreLocal(temporary));
            use.Add(CodeInstruction.LoadLocal(temporary, useAddress: true));
        }
        Append(code, at, use.ToArray());
    }

    static void RewriteGap(List<CodeInstruction> code, int point)
    {
        var call = code.Single(op => op.Calls(gap));
        Insert(code, call, Load(point));
        call.operand = AccessTools.Method(typeof(StockTargetAim), nameof(PartGap));
    }

    static void RequireClearLine(List<CodeInstruction> code, int point, int target, int attacker) =>
        Append(code, code.Single(op => op.Calls(otherFloor)), Arg(1), Arg(attacker), Load(target), Load(point), Call(nameof(Blocked)));

    static CodeInstruction Arg(int value) => CodeInstruction.LoadArgument(value);
    static CodeInstruction Load(int value) => CodeInstruction.LoadLocal(value);
    static CodeInstruction Call(string name) => new(OpCodes.Call, AccessTools.Method(typeof(StockTargetAim), name));
    static void Append(List<CodeInstruction> code, CodeInstruction at, params CodeInstruction[] instructions) =>
        code.InsertRange(code.IndexOf(at) + 1, instructions);
    static void Insert(List<CodeInstruction> code, CodeInstruction at, params CodeInstruction[] instructions)
    {
        instructions[0].labels.AddRange(at.labels);
        at.labels.Clear();
        code.InsertRange(code.IndexOf(at), instructions);
    }
}
