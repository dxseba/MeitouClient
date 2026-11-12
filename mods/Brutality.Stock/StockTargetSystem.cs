using Meitou.Simulation.Movement;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Player;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;

namespace Meitou.Brutality;

internal sealed record StockTargetOrder(IReadOnlyList<CharacterId> Attackers, CharacterId Target, int Part, string? Template) : OrderCommand;

/// <summary>The stock task owner for an explicit melee order against a living helpless enemy.</summary>
internal sealed class StockTargetSystem(CombatSystem combat) : ITickSystem, IOrderOwner, IStateHashed, ICombatHitObserver
{
    internal sealed record Focus(int Part, string? Template);
    internal readonly record struct MenuPart(string Template, string Name, bool Present);

    World? world;
    readonly Dictionary<CharacterId, MenuPart[]> menuParts = [];
    readonly HashSet<CharacterId> menuTargets = [];
    readonly List<CombatHit> ragdollHits = [];
    volatile IReadOnlyDictionary<CharacterId, MenuPart[]> published = new Dictionary<CharacterId, MenuPart[]>();
    static readonly bool preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_TARGET_PREVIEW") == "1";
    CharacterId probeAttacker = CharacterId.None, probeTarget = CharacterId.None;
    Focus? probePart;
    long probeStarted;
    int probeStage;

    internal IReadOnlyList<MenuPart> MenuParts(CharacterId target) => published.TryGetValue(target, out var parts) ? parts : [];

    internal static void Install(ModContext context)
    {
        StockTargetSave.Install(context);
        context.AddSystem("targeting", build =>
            new StockTargetSystem(build.Find<CombatSystem>() ?? throw new InvalidOperationException("No stock combat system.")),
            SystemAnchor.Before(StandardParts.Combat));
    }

    internal static bool Helpless(CharacterSnapshot target) => target.Body is { Dead: false } body
        && (target.Down || target.Asleep || body.Unconscious || body.Coma || body.Crawling || body.PlayingDead
            || body.StayingLow || target.StayingLow || target.InBed
            || target.Animations.Any(layer => layer.Name.StartsWith("standing up", StringComparison.OrdinalIgnoreCase)));

    static bool Helpless(World world, int slot, CharacterCold target) => target.Medical is { Dead: false } medical
        && (medical.Incapacitated || medical.Prone || medical.Crawling || medical.PlayingDead
            || target.InSomething == HeldState.Bed || CarrySystem.Asleep(world.Characters, slot) || CharacterFacts.GettingUp(target, world.Tick));

    internal static bool GroundTarget(World world, CharacterId target) => world.Characters.TryResolveNext(target, out int slot)
        && world.Characters.Cold(slot) is { } victim && Helpless(world, slot, victim);

    static bool SourceReady(CharacterCold? source) => source is
    {
        IsPlayer: true, Fighter: { CanAttack: true, IsAnimal: false }, Stats: not null, Race: not null,
        Medical: { Dead: false, Incapacitated: false }, CarriedBy.IsNone: true, IsImprisoned: false, Gunning: null,
    } && source.InSomething == HeldState.None;

    static bool PartPresent(MedicalState medical, Focus focus) => focus.Part == -1
        || (uint)focus.Part < (uint)medical.Parts.Count && medical.Parts[focus.Part] is { Limb: not LimbState.Stump } part
            && part.Template.StringId == focus.Template;

    bool TargetValid(World world, int attacker, CharacterId target, Focus focus, bool starting = false)
    {
        var table = world.Characters;
        return attacker != target.Slot && table.TryResolveNext(target, out int slot)
            && table.Cold(attacker) is { IsPlayer: true } source
            && table.Cold(slot) is { IsPlayer: false, Fighter: not null, Race: not null, Stats: not null,
                Medical: { Dead: false } medical, CarriedBy.IsNone: true, IsImprisoned: false } victim
            && (victim.InSomething is HeldState.None or HeldState.Bed) && source.Faction != victim.Faction
            && Hostile(world, target, victim)
            && ((!starting && focus.Part == -1) || Helpless(world, slot, victim)) && PartPresent(medical, focus);
    }

    bool Hostile(World world, CharacterId target, CharacterCold victim)
    {
        var fight = combat.StateOf(target);
        var table = world.Characters;
        bool fightsPlayer = victim.InCombat && table.TryResolveNext(new(fight.TargetSlot, fight.TargetGeneration), out int opponent)
            && table.Cold(opponent) is { IsPlayer: true };
        return CharacterFacts.HostileToPlayer(combat.Relations, world.Player.Faction, victim.Faction, fightsPlayer);
    }

    internal static bool TryFocus(CharacterTable table, int attacker, int target, out OrderEntry order, out Focus focus)
    {
        order = null!;
        focus = null!;
        if (table.Cold(attacker)?.OrderQueue.Front is not { Owner: StockTargetSystem, Detail: Focus selected, Started: true } entry
            || entry.Target.Slot != target) return false;
        order = entry;
        focus = selected;
        return true;
    }

    internal static bool Allowed(CharacterTable table, int attacker, OrderEntry order, Focus focus) =>
        order.Owner is StockTargetSystem { world: { } world } owner && ReferenceEquals(world.Characters, table)
            && (uint)order.Target.Slot < (uint)table.Previous.Length
            && table.Previous[order.Target.Slot].Generation == order.Target.Generation
            && SourceReady(table.Cold(attacker)) && owner.TargetValid(world, attacker, order.Target, focus);

    internal static Vector3? AimPoint(CharacterTable table, int attacker, int target) =>
        TryFocus(table, attacker, target, out var order, out var focus) && focus.Part >= 0
            && order.Owner is StockTargetSystem { world: { } world } && Allowed(table, attacker, order, focus)
            ? StockTargetRagdoll.PartPoint(world, order.Target, focus.Part) : null;

    public void Inputs(World value, IReadOnlyList<SimCommand> commands)
    {
        world = value;
        var table = value.Characters;
        foreach (var command in commands.OfType<StockTargetOrder>())
        {
            if (!table.TryResolveNext(command.Target, out int target)
                || table.Cold(target)?.Medical is not { } medical
                || command.Part != -1 && (uint)command.Part >= (uint)medical.Parts.Count) continue;
            var focus = new Focus(command.Part, command.Template);
            // The stock task handler initializes its slot storage in Inputs. This system runs first.
            combat.Inputs(value, []);
            foreach (var id in command.Attackers)
            {
                if (!table.TryResolveNext(id, out int attacker) || !SourceReady(table.Cold(attacker))
                    || !TargetValid(value, attacker, command.Target, focus, starting: true)) continue;
                PlayerOrders.Give(value, attacker, new OrderEntry(TaskType.FocusedMeleeAttack, this)
                { Target = command.Target, Detail = focus }, command.Queued);
            }
        }
    }

    public void SlowWorld(World current)
    {
        StockTargetRagdoll.Apply(current, ragdollHits);
        ragdollHits.Clear();
        if (preview) StepProbe(current);
        // Stock only publishes health rows for selected/inspected characters. Publish immutable
        // menu labels for a pointed foe without changing selection or reading mutable health on the UI thread.
        bool changed = false;
        var table = current.Characters;
        menuTargets.Clear();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot] is not { Alive: true, Dormant: false }
                || table.Cold(slot) is not { IsPlayer: false, Medical: { Dead: false } medical, CarriedBy.IsNone: true, IsImprisoned: false } victim
                || !Helpless(current, slot, victim)) continue;
            var id = table.IdOf(slot);
            if (!Hostile(current, id, victim)) continue;
            menuTargets.Add(id);
            if (menuParts.TryGetValue(id, out var existing) && Matches(medical, existing)) continue;
            menuParts[id] = medical.Parts.Select(part => new MenuPart(part.Template.StringId,
                part.Template.Name.Length != 0 ? part.Template.Name : part.Template.Type.ToString(), part.Limb != LimbState.Stump)).ToArray();
            changed = true;
        }
        if (menuParts.Count > menuTargets.Count)
            foreach (var id in menuParts.Keys.Where(id => !menuTargets.Contains(id)).ToArray())
                changed |= menuParts.Remove(id);
        if (changed) published = new Dictionary<CharacterId, MenuPart[]>(menuParts);

        static bool Matches(MedicalState medical, MenuPart[] known)
        {
            if (known.Length != medical.Parts.Count) return false;
            for (int i = 0; i < known.Length; i++)
                if (known[i].Template != medical.Parts[i].Template.StringId
                    || known[i].Present != (medical.Parts[i].Limb != LimbState.Stump)) return false;
            return true;
        }
    }

    void StepProbe(World current)
    {
        if (probeStage >= 3 || current.Tick < 90) return;
        var table = current.Characters;
        if (probeStage == 0)
        {
            for (int slot = 0; slot < table.HighWater; slot++)
                if (table.Next[slot].Alive && SourceReady(table.Cold(slot)))
                { probeAttacker = table.IdOf(slot); break; }
            if (probeAttacker.IsNone) return;
            for (int slot = 0; slot < table.HighWater; slot++)
            {
                if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: false, Medical: { Dead: false } medical } victim) continue;
                var id = table.IdOf(slot);
                if (!Hostile(current, id, victim)) continue;
                var head = medical.Parts.FirstOrDefault(part => part.Template is { Vital: true, Type: BodyPartType.Head });
                int index = -1;
                for (int i = 0; i < medical.Parts.Count; i++)
                    if (medical.Parts[i] is { Template.Type: BodyPartType.Arm, Limb: not LimbState.Stump }) { index = i; break; }
                if (head is null || index < 0) continue;
                probeTarget = id;
                probePart = new(index, medical.Parts[index].Template.StringId);
                probeStarted = current.Tick;
                probeStage = 1;
                current.Commands.Enqueue(new DebugMaxSkills([probeAttacker]) { Tick = current.Tick + 1 });
                current.Commands.Enqueue(new DebugDamage([id], MathF.Max(0, head.Flesh + head.MaxHp * .12f), head.Template.StringId)
                { Tick = current.Tick + 1 });
                Console.WriteLine($"Brutality stock target preview KO target={id} part={index} tick={current.Tick}");
                return;
            }
            return;
        }
        if (current.Tick - probeStarted > 300)
        {
            Console.WriteLine($"Brutality stock target preview timed out stage={probeStage} target={probeTarget}");
            probeStage = 3;
            return;
        }
        if (probeStage != 1 || probePart is not { } focus || !TargetValid(current, probeAttacker.Slot, probeTarget, focus)) return;
        probeStage = 2;
        current.Commands.Enqueue(new StockTargetOrder([probeAttacker], probeTarget, focus.Part, focus.Template) { Tick = current.Tick + 1 });
        Console.WriteLine($"Brutality stock target preview ordered target={probeTarget} part={focus.Part} tick={current.Tick}");
    }

    public void HitResolved(World current, in CombatHit hit)
    {
        if (hit is { Source: CombatHitSource.Melee, Intended: true, VictimWasAlive: true, VictimWasIncapacitated: true }
            && ragdollHits.Count < StockTargetRagdoll.MaxHitsPerTick
            && TryFocus(current.Characters, hit.Attacker.Slot, hit.Victim.Slot, out var order, out _)
            && order.Target == hit.Victim) ragdollHits.Add(hit);
        if (!preview || probeStage != 2 || probePart is not { } focus || hit.Source != CombatHitSource.Melee
            || hit.Attacker != probeAttacker || hit.Victim != probeTarget) return;
        probeStage = 3;
        Console.WriteLine($"Brutality stock target preview landed target={hit.Victim} expected={focus.Part} actual={hit.Part} incapacitated={hit.VictimWasIncapacitated} tick={hit.Tick}");
    }

    public OrderCheck CanRun(World value, int slot, OrderEntry order) =>
        order.Detail is not Focus focus || !TargetValid(value, slot, order.Target, focus) ? OrderCheck.Gone
        : SourceReady(value.Characters.Cold(slot)) ? OrderCheck.Run : OrderCheck.Blocked();

    public void Start(World value, int slot, OrderEntry order) =>
        TaskStart.Start(value, slot, new TaskRequest(CharacterTask.Attack, TaskType.FocusedMeleeAttack, TaskSource.Order)
        { Target = order.Target });

    public int SaveKind(OrderEntry order) => StockTargetSave.Ready && order.Detail is Focus focus ? focus.Part + 1 : -1;

    public object? LoadDetail(World value, CharacterId self, TaskType row, int kind, CharacterId target) =>
        StockTargetSave.FocusOf(value, row, kind);

    public void Hash(ref StateHasher hash)
    {
        if (world is not { } current) return;
        var table = current.Characters;
        bool wroteHeader = false;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { } cold) continue;
            for (int index = 0; index < cold.OrderQueue.Count; index++)
            {
                var entry = cold.OrderQueue[index];
                if (!ReferenceEquals(entry.Owner, this) || entry.Detail is not Focus focus) continue;
                if (!wroteHeader) { hash.Add(0x5354464F); wroteHeader = true; }
                hash.Add(table.IdOf(slot));
                hash.Add(index);
                hash.Add(entry.Target);
                hash.Add(focus.Part);
                if (focus.Template is { } template) hash.Add(Rng.StableHash(template));
            }
        }
    }
}
