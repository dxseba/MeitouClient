using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

internal static class StockTorsoProxySave
{
    const string SaveKey = "dxseba brutality proxy";
    const int SaveVersion = 1, MaxRows = 512, MaxLength = 120_000;
    static readonly JsonSerializerOptions json = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 10 };
    static readonly ConditionalWeakTable<CharacterCold, Loaded> loaded = new();
    static readonly ConditionalWeakTable<World, Restoring> worlds = new();
    static readonly MethodInfo link = AccessTools.Method(typeof(CarrySystem), "Link", [typeof(CharacterTable), typeof(int), typeof(int)]);

    sealed record SavedRow(int Index, TaskType Row, int Kind, Hand Owner, bool Started);
    sealed record Saved(int Version, Hand? CarriedLower, Vector3? LowerPosition, SavedRow[] Orders, bool Imported = false);
    sealed class Loaded(Saved? data)
    {
        public readonly Saved? Data = data;
        public bool Placed, CarrySettled;
    }
    sealed class Restoring
    {
        public long Spawns = -1;
        public readonly Dictionary<Hand, CharacterId> Live = [];
        public readonly List<(CharacterId Id, CharacterCold Cold, Loaded State)> Pending = [];
    }
    sealed class Capture(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<CharacterId, (CharacterCold Cold, SaveCharacter Record, bool HasOrders)> Actors = [];
        public readonly Dictionary<Hand, Hand> Remapped = [];
    }
    sealed record RestoreScope(CharacterTable Table, Dictionary<Hand, CharacterId> Aliases);
    [ThreadStatic] static Capture? capturing;
    [ThreadStatic] static RestoreScope? restoring;

    internal static void Install(ModContext context)
    {
        var save = AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture));
        var writer = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
        context.Harmony.Patch(save, prefix: Hook(nameof(BeforeCapture)), finalizer: Hook(nameof(AfterCapture)));
        context.Harmony.Patch(writer, postfix: Hook(nameof(Written)));
        context.Harmony.Patch(AccessTools.Method(typeof(OrderSave), "Restore"),
            prefix: Hook(nameof(BeforeRestore)), finalizer: Hook(nameof(AfterRestore)));
        context.Harmony.Patch(AccessTools.Method(typeof(OrderSave), "Find"), prefix: Hook(nameof(ResolveAlias)));
    }

    static HarmonyMethod Hook(string name) => new(typeof(StockTorsoProxySave), name);

    static void BeforeCapture(World world, out Capture? __state)
    { __state = capturing; capturing = StockTorsoRules.Enabled ? new(world) : null; }
    static Exception? AfterCapture(Exception? __exception, Capture? __state)
    { capturing = __state; return __exception; }

    static void Written(World world, CharacterCold cold, CharacterId id, bool orders, SaveCharacter __result)
    {
        if (capturing is not { } capture || !ReferenceEquals(world, capture.World) || __result.Handle.IsNull) return;
        if (!id.IsNone) capture.Actors[id] = (cold, __result, orders);
        if (cold.Save is { Source.Handle.IsNull: false } old) capture.Remapped[old.Source.Handle] = __result.Handle;
    }

    // Called after the torso writer adds standalone corpses, so even an originally unwritten owner has a real save hand.
    internal static void FinishCapture(SaveGame save)
    {
        if (capturing is not { } capture) return;
        var world = capture.World;
        var table = world.Characters;
        var frame = StockTorsoProxies.For(world)?.Frame;
        var inSave = save.Characters.Select(actor => actor.Handle).ToHashSet();
        Hand? Fresh(Hand owner)
        {
            var mapped = capture.Remapped.GetValueOrDefault(owner, owner);
            return inSave.Contains(mapped) ? mapped : null;
        }
        Hand? OwnerOf(CharacterId proxy) => frame is not null && frame.TryOwner(proxy, out var owner)
            && capture.Actors.TryGetValue(owner, out var record) ? record.Record.Handle : null;

        foreach (var (id, (cold, record, hasOrders)) in capture.Actors)
        {
            if (record.GameState is not { } target) continue;
            target.Strings.Remove(SaveKey);
            var previous = StateOf(cold);
            Hand? carried = OwnerOf(cold.Carrying);
            if (carried is null && cold.Carrying.IsNone && !previous.CarrySettled && previous.Data?.CarriedLower is { } pendingCarrier)
                carried = Fresh(pendingCarrier);

            Vector3? position = null;
            if (frame is not null && frame.TryProxy(id, out var lower) && table.TryResolveNext(lower, out int slot)
                && table.Cold(slot) is { CarriedBy.IsNone: true } && Valid(table.Next[slot].Position)) position = table.Next[slot].Position;
            else if (!previous.Placed) position = previous.Data?.LowerPosition;

            var rows = new List<SavedRow>();
            if (hasOrders && record.Ai is not null)
            {
                if (cold.SavedOrders is { } pending && previous.Data is { } old)
                {
                    foreach (var row in old.Orders)
                        if ((uint)row.Index < (uint)pending.Count && Matches(pending[row.Index], row) && Fresh(row.Owner) is { } handle)
                            rows.Add(row with { Owner = handle });
                }
                else
                {
                    int index = 0;
                    foreach (var entry in cold.OrderQueue)
                    {
                        int kind = entry.Row == TaskType.None ? -1 : entry.Owner.SaveKind(entry);
                        if (kind < 0) continue;
                        if (index >= MaxRows) break;
                        if (entry.Owner is CarrySystem && AllowedRow(entry.Row, kind) && OwnerOf(entry.Target) is { } owner)
                            rows.Add(new(index, entry.Row, kind, owner, entry.Started));
                        index++;
                    }
                }
            }
            if (carried is not null || position is not null || rows.Count != 0)
                target.Strings[SaveKey] = JsonSerializer.Serialize(new Saved(SaveVersion, carried, position, rows.ToArray()), json);
        }
    }

    internal static void MigratePosition(SaveCharacter owner, Vector3 position)
    {
        if (owner.GameState is { } record && !record.Strings.ContainsKey(SaveKey) && Valid(position))
            record.Strings[SaveKey] = JsonSerializer.Serialize(new Saved(SaveVersion, null, position, [], Imported: true), json);
    }

    internal static bool MigrateCarrier(SaveCharacter carrier, Hand owner)
    {
        if (carrier.GameState is not { } record || record.Strings.ContainsKey(SaveKey)) return false;
        record.Strings[SaveKey] = JsonSerializer.Serialize(new Saved(SaveVersion, owner, null, [], Imported: true), json);
        return true;
    }

    internal static Vector3? MigratedLowerPosition(World world, CharacterId owner) =>
        world.Characters.TryResolveNext(owner, out int slot) && world.Characters.Cold(slot) is { } cold
            && StateOf(cold).Data is { Imported: true, LowerPosition: { } position } ? position : null;

    static bool AllowedRow(TaskType row, int kind) => row switch
    {
        TaskType.LiftPersonPlayerOrder or TaskType.KidnapOrder or TaskType.PutDownObject => kind == 0,
        TaskType.LootTarget => kind is 0 or 1,
        _ => false,
    };

    static bool Matches(SavedOrder order, SavedRow row) => !order.Lost && order.Hand.IsNull && order.Site is null
        && order.Row == row.Row && order.Kind == row.Kind;

    static bool Valid(Vector3 position) => float.IsFinite(position.LengthSquared()) && position.LengthSquared() < 1e14f;
    static bool Character(Hand handle) => handle.RecordType == FcsRecordType.Character;

    /// <summary>Collects cut owners needed by a loaded character before native orders and carries restore.</summary>
    internal static void AddRequiredOwners(CharacterCold cold, HashSet<Hand> required)
    {
        if (cold.Save is not { } link) return;
        if (link.CarryIsCarrier && Character(link.CarryPartner)) required.Add(link.CarryPartner);
        if (StateOf(cold).Data is not { } saved) return;

        if (saved.CarriedLower is { } carry) required.Add(carry);
        if (cold.SavedOrders is not { } orders) return;
        foreach (var row in saved.Orders)
            if ((uint)row.Index < (uint)orders.Count && Matches(orders[row.Index], row)) required.Add(row.Owner);
    }

    static Loaded StateOf(CharacterCold cold) => loaded.GetValue(cold, static actor =>
        new Loaded(Read(actor.Save?.Source.GameState)));

    static Saved? Read(FcsRecord? state)
    {
        if (state?.Strings.GetValueOrDefault(SaveKey) is not { Length: > 0 and <= MaxLength } text) return null;
        try
        {
            if (JsonSerializer.Deserialize<Saved>(text, json) is not { Version: SaveVersion, Orders.Length: <= MaxRows } saved) return null;
            var rows = saved.Orders.Where(row => row is not null && row.Index is >= 0 and < MaxRows
                && Character(row.Owner) && AllowedRow(row.Row, row.Kind)).DistinctBy(row => row.Index).ToArray();
            return saved with
            {
                CarriedLower = saved.CarriedLower is { } carry && Character(carry) ? carry : null,
                LowerPosition = saved.LowerPosition is { } at && Valid(at) ? at : null,
                Orders = rows,
            };
        }
        catch (JsonException) { return null; }
    }

    static Restoring LiveActors(World world)
    {
        var state = worlds.GetOrCreateValue(world);
        var table = world.Characters;
        if (state.Spawns == table.Spawns) return state;
        state.Spawns = table.Spawns;
        state.Live.Clear();
        state.Pending.Clear();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Save.Source.Handle.IsNull: false } cold) continue;
            var id = table.IdOf(slot);
            state.Live[cold.Save.Source.Handle] = id;
            var saved = StateOf(cold);
            if ((!saved.CarrySettled && saved.Data?.CarriedLower is not null) || (!saved.Placed && saved.Data?.LowerPosition is not null))
                state.Pending.Add((id, cold, saved));
        }
        return state;
    }

    static bool TryLower(World world, Restoring state, Hand handle, out CharacterId proxy, out int slot)
    {
        proxy = CharacterId.None;
        slot = -1;
        var table = world.Characters;
        return state.Live.TryGetValue(handle, out var owner) && table.TryResolveNext(owner, out int ownerSlot)
            && table.Cold(ownerSlot) is { Medical.Dead: true, Save.Source.Handle: { } actual } && actual == handle
            && StockTorsoProxies.For(world)?.Frame.TryProxy(owner, out proxy) == true && table.TryResolveNext(proxy, out slot)
            && table.Cold(slot)?.Medical?.Dead == true;
    }

    internal static void AfterProxies(World world)
    {
        if (world.Saved is null) return;
        var state = LiveActors(world);
        if (state.Pending.Count == 0) return;
        var table = world.Characters;
        foreach (var (_, cold, saved) in state.Pending)
        {
            if (saved.Placed || saved.Data?.LowerPosition is not { } at || cold.Save is not { Source.Handle: { } owner }
                || !TryLower(world, state, owner, out _, out int lower)) continue;
            saved.Placed = true;
            if (table.Cold(lower) is { CarriedBy.IsNone: true, InSomething: HeldState.None }) table.Place(lower, at, table.Next[lower].Yaw);
        }
        foreach (var (id, cold, saved) in state.Pending)
        {
            if (saved.CarrySettled || saved.Data?.CarriedLower is not { } owner || !table.TryResolveNext(id, out int carrier)
                || !ReferenceEquals(cold, table.Cold(carrier)) || !TryLower(world, state, owner, out var proxy, out int lower)) continue;
            saved.CarrySettled = true;
            if (cold.Save is { CarryPartner.IsNull: false } || !CarrySystem.CanCarry(world, cold, ordered: true)
                || table.Cold(lower) is not { CarriedBy.IsNone: true, InSomething: HeldState.None } body || !CarrySystem.CanBeLifted(body)) continue;
            link.Invoke(null, [table, carrier, lower]);
            if (preview is "lower" or "upper") Console.WriteLine($"Brutality stock torso carry restored carrier={id} target={proxy}");
        }
        state.Pending.RemoveAll(entry => !table.TryResolveNext(entry.Id, out _)
            || (entry.State.Placed || entry.State.Data?.LowerPosition is null) && (entry.State.CarrySettled || entry.State.Data?.CarriedLower is null));
    }

    static void BeforeRestore(World world, CharacterCold cold, out RestoreScope? __state)
    {
        __state = restoring;
        restoring = null;
        if (cold.OrderQueue.Count != 0 || cold.SavedOrders is not { } orders || StateOf(cold).Data is not { Orders.Length: > 0 } saved) return;
        var state = LiveActors(world);
        var selected = saved.Orders.ToDictionary(row => row.Index);
        var rows = new List<SavedOrder>(orders.Count);
        var aliases = new Dictionary<Hand, CharacterId>();
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            if (!selected.TryGetValue(i, out var row) || !Matches(order, row)) { rows.Add(order); continue; }
            if (!TryLower(world, state, row.Owner, out var proxy, out _)) continue;
            // A pickup that already attached before the save has completed its purpose. Do not block later queued orders.
            if (row.Started && cold.Carrying == proxy && (row.Row is TaskType.LiftPersonPlayerOrder or TaskType.KidnapOrder)) continue;
            var alias = new Hand((int)FcsRecordType.Character, int.MinValue, int.MinValue, i, int.MinValue);
            aliases[alias] = proxy;
            rows.Add(order with { Hand = alias });
        }
        cold.SavedOrders = rows;
        restoring = new(world.Characters, aliases);
    }

    static Exception? AfterRestore(Exception? __exception, RestoreScope? __state)
    { restoring = __state; return __exception; }

    static bool ResolveAlias(CharacterTable table, Hand hand, ref CharacterId __result)
    {
        if (restoring is not { } scope || !ReferenceEquals(table, scope.Table) || !scope.Aliases.TryGetValue(hand, out var target)) return true;
        __result = target;
        return false;
    }

    static readonly string? preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        ? Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_TORSO_CARRY_PREVIEW") : null;
    static readonly ConditionalWeakTable<World, PreviewState> previews = new();
    sealed class PreviewState
    {
        internal CharacterId Carrier = CharacterId.None, Target = CharacterId.None;
        internal long Began;
        internal bool Complete;
    }

    internal static void Preview(World world, ConcurrentDictionary<CharacterId, Vector3> drawn)
    {
        if (preview is not ("upper" or "lower")) return;
        var state = previews.GetOrCreateValue(world);
        if (state.Complete) return;
        var table = world.Characters;
        if (!state.Target.IsNone)
        {
            if (table.TryResolveNext(state.Carrier, out int slot) && table.Cold(slot)?.Carrying == state.Target)
            {
                Console.WriteLine($"Brutality stock torso carry preview carried side={preview} carrier={state.Carrier} target={state.Target} tick={world.Tick}");
                state.Complete = true;
            }
            else if (world.Tick - state.Began > 300)
            {
                Console.WriteLine($"Brutality stock torso carry preview timed out side={preview} target={state.Target}");
                state.Complete = true;
            }
            return;
        }
        var frame = StockTorsoProxies.For(world)?.Frame;
        if (frame is null) return;
        foreach (var owner in StockTorsoRules.CutOwners(world))
        {
            if (!frame.TryProxy(owner, out var proxy)) continue;
            var target = preview == "lower" ? proxy : owner;
            if (!drawn.TryGetValue(target, out var position)) continue;
            for (int slot = 0; slot < table.HighWater; slot++)
            {
                if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold
                    || !CarrySystem.CanCarry(world, cold, ordered: true)) continue;
                state.Target = target;
                state.Carrier = table.IdOf(slot);
                state.Began = world.Tick;
                world.Commands.Enqueue(new StockTorsoProxies.PositionOrder(target, position) { Tick = world.Tick + 1 });
                world.Commands.Enqueue(new PickUpOrder([state.Carrier], target) { Tick = world.Tick + 1 });
                Console.WriteLine($"Brutality stock torso carry preview ordered side={preview} carrier={state.Carrier} target={target} tick={world.Tick}");
                return;
            }
        }
    }
}
