using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;

namespace Meitou.Brutality;

/// <summary>A deliberate attack on a living helpless enemy; Part -1 retains ordinary part selection.</summary>
public sealed record TargetBodyPartAttackOrder(IReadOnlyList<CharacterId> Attackers, CharacterId Target, int Part) : OrderCommand;

/// <summary>Owns focused orders; the native combat controller keeps its original animation, timing and damage path.</summary>
public sealed partial class IncapacitatedCombatAdapter(CombatSystem? combat) : ICombatTargetRules, IOrderOwner, IStateHashed, IModSaveData
{
    sealed record Focus(string? Part);
    World? world;
    readonly Dictionary<CharacterCold, List<RememberedOrder>> remembered = new(ReferenceEqualityComparer.Instance);
    public static void Install(ModContext context) => context.AddSystem("targeting", build => new IncapacitatedCombatAdapter(build.Find<CombatSystem>()),
        SystemAnchor.Before(StandardParts.Combat));

    public static bool IsHelpless(CharacterSnapshot target) => target.Body is { Dead: false } body
        && (body.Unconscious || body.Coma || body.Crawling || body.PlayingDead || body.StayingLow || target.StayingLow || target.InBed
            || target.Animations.Any(layer => layer.Name.StartsWith("standing up", StringComparison.OrdinalIgnoreCase)));
    public static bool CanAttack(CharacterSnapshot attacker, CharacterSnapshot target, FactionRelations? relations, int part = -1) =>
        attacker.Id != target.Id && attacker.Body is { Dead: false, Unconscious: false } && attacker.CarriedBy.IsNone && !attacker.Imprisoned
        && target.Body is { Dead: false } && target.CarriedBy.IsNone && !target.Imprisoned && IsHelpless(target)
        && CharacterFacts.RegardsEnemy(relations, attacker.Faction, target.Faction)
        && (part == -1 || (uint)part < (uint)target.HealthParts.Count && target.HealthParts[part].Limb != LimbState.Stump);

    static bool Helpless(CharacterCold target, long tick) => target.Medical is { Dead: false } medical
        && (medical.Incapacitated || medical.Prone || medical.PlayingDead || target.InSomething == HeldState.Bed || CharacterFacts.GettingUp(target, tick));
    public static bool CanAttack(World world, CharacterId attacker, CharacterId target, int part = -1) =>
        Valid(world, attacker, target, part, requireHelpless: true);
    static bool Valid(World world, CharacterId attacker, CharacterId target, int part, bool requireHelpless)
    {
        var table = world.Characters;
        if (attacker == target || !table.TryResolveNext(attacker, out int a) || !table.TryResolveNext(target, out int t)
            || table.Cold(a) is not { Fighter: not null, Race: not null, Stats: not null, Medical: { Dead: false, Incapacitated: false }, CarriedBy.IsNone: true, IsImprisoned: false } source
            || table.Cold(t) is not { Fighter: not null, Race: not null, Stats: not null, Medical: { Dead: false } medical, CarriedBy.IsNone: true, IsImprisoned: false } victim
            || !CharacterFacts.RegardsEnemy(world.Systems.OfType<CombatSystem>().FirstOrDefault()?.Relations, source.Faction, victim.Faction)
            || requireHelpless && !Helpless(victim, world.Tick)) return false;
        return part == -1 || (uint)part < (uint)medical.Parts.Count && medical.Parts[part].Limb != LimbState.Stump;
    }
    static int Index(CharacterCold target, string? part)
    {
        if (part is null) return -1;
        if (target.Medical is not { } medical) return -2;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (medical.Parts[i].Template.StringId == part && medical.Parts[i].Limb != LimbState.Stump) return i;
        return -2;
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        Bind(world);
        if (combat is null) return;
        // This system runs before combat's Inputs. Its native task-start handler needs Sync to have sized the slot buffers.
        // The public native empty input pass performs that initialization without fabricating an order or combat state.
        combat.Inputs(world, []);
        foreach (var command in commands.OfType<TargetBodyPartAttackOrder>())
            foreach (var id in command.Attackers.Count > 0 ? command.Attackers : world.Player.Selection.ToArray())
            {
                if (!CanAttack(world, id, command.Target, command.Part) || !world.Characters.TryResolveNext(id, out int slot)) continue;
                var target = world.Characters.Cold(command.Target.Slot)!;
                var part = command.Part >= 0 ? target.Medical!.Parts[command.Part].Template.StringId : null;
                PlayerOrders.Give(world, slot, new OrderEntry(TaskType.FOCUSED_MELEE_ATTACK, this) { Target = command.Target, Detail = new Focus(part) }, command.Queued);
            }
    }
    public void Schedule(World world)
    {
        Bind(world);
        if (pending.Count > 0) combat?.Inputs(world, []); // A sidecar can be restored before the first native input pass.
        RestorePending(world); Remember(world);
    }
    void Bind(World value)
    {
        if (world is not null && !ReferenceEquals(world, value)) throw new InvalidOperationException("A targeting system belongs to one world.");
        world = value;
    }
    public OrderCheck CanRun(World world, int slot, OrderEntry order)
    {
        if (order.Detail is not Focus focus || !world.Characters.TryResolveNext(order.Target, out int target)) return OrderCheck.Gone;
        int part = Index(world.Characters.Cold(target)!, focus.Part);
        var source = world.Characters.Cold(slot);
        var victim = world.Characters.Cold(target)!;
        if (part < -1 || victim is not { Medical: { Dead: false }, CarriedBy.IsNone: true, IsImprisoned: false }
            || focus.Part is not null && !Helpless(victim, world.Tick)
            || source is null || !CharacterFacts.RegardsEnemy(combat?.Relations, source.Faction, victim.Faction)) return OrderCheck.Gone;
        return source is { Fighter: not null, Stats: not null, Race: not null, Medical.Incapacitated: false, CarriedBy.IsNone: true, IsImprisoned: false }
            ? OrderCheck.Run : OrderCheck.Blocked();
    }
    public void Start(World world, int slot, OrderEntry order)
    {
        if (combat is null || !CanRun(world, slot, order).CanRun) return;
        TaskStart.Start(world, slot, new TaskRequest(CharacterTask.Attack, TaskType.FOCUSED_MELEE_ATTACK, TaskSource.Order) { Target = order.Target });
        if (world.Characters.TryResolveNext(order.Target, out int target))
            combat.Crimes?.Assault(world, slot, target, world.Characters.Cold(target)!.InCombat);
    }
    // These orders are stored by this system's sidecar, not as a vanilla row with a lost anatomy detail.
    public int SaveKind(OrderEntry order) => -1;

    public bool TryTarget(World world, CharacterId attacker, CharacterId target, bool picked, out CombatTargetOverride rule)
    {
        rule = default;
        if (!picked || !world.Characters.TryResolveNext(attacker, out int slot)
            || world.Characters.Cold(slot)?.OrderQueue.Front is not { Detail: Focus focus } order || !ReferenceEquals(order.Owner, this) || order.Target != target) return false;
        if (!world.Characters.TryResolveNext(target, out int victim)) { rule = new(false); return true; }
        var cold = world.Characters.Cold(victim)!;
        int part = Index(cold, focus.Part);
        bool allowed = part >= -1 && Valid(world, attacker, target, part, focus.Part is not null);
        Vector3? point = allowed && part >= 0 && TryPartPosition(world, target, cold, part, out var position) ? position : null;
        rule = new(allowed, AllowIncapacitated: true, Part: focus.Part is not null ? part : null, AimPoint: point, GroundTarget: Helpless(cold, world.Tick));
        return true;
    }
    public void Hash(ref StateHasher hash)
    {
        if (world is not { } value) { HashPending(ref hash); return; }
        bool marker = false;
        for (int slot = 0; slot < value.Characters.HighWater; slot++)
        {
            if (!value.Characters.Next[slot].Alive || value.Characters.Cold(slot) is not { } cold) continue;
            for (int index = 0; index < cold.OrderQueue.Count; index++)
                if (cold.OrderQueue[index] is { Detail: Focus focus } order && ReferenceEquals(order.Owner, this))
                {
                    if (!marker) { hash.Add(0x47544149); marker = true; }
                    hash.Add(value.Characters.IdOf(slot)); hash.Add(index); hash.Add(order.Target); hash.Add(focus.Part is not null);
                    if (focus.Part is { } part) hash.Add(Rng.StableHash(part));
                }
        }
        HashPending(ref hash);
    }
}
