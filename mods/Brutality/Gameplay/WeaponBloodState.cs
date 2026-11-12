using System.Numerics;
using System.Text.Json;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>An immutable stain snapshot belonging to this exact inventory item, even after it is put away or dropped.</summary>
public readonly record struct WeaponStainView(ItemInstance Item, Vector4 Blood, long Revision);

/// <summary>Item-owned display state; item identities come from the native save writer, never inventory slots or weapon records.</summary>
public sealed partial class WeaponBloodState(RngPurpose purpose = default) : ITickSystem, IModSaveData, IStateHashed
{
    sealed class Stain { public Vector4 Blood; public long Revision; public string? Key; }
    public sealed record SavedStain(string Key, float R, float G, float B, float Amount);
    sealed record Document(int SchemaVersion, SavedStain[] Items, string? ContainerNamespace = null, long ContainerSerial = 0);
    readonly Dictionary<ItemInstance, Stain> stains = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, Vector4> durable = new(StringComparer.Ordinal);
    long revision;
    bool dirty;
    public IReadOnlyList<WeaponStainView> Views { get; private set; } = [];
    internal static WeaponBloodState? For(World world) => world.Systems.OfType<WeaponBloodState>().FirstOrDefault();

    public bool TryGet(ItemInstance item, out WeaponStainView view)
    {
        Adopt(item);
        if (stains.TryGetValue(item, out var stain)) { view = new(item, stain.Blood, stain.Revision); return true; }
        view = default; return false;
    }
    public bool Deposit(ItemInstance? item, RaceData race, in HitDamage damage, bool aliveBeforeHit, bool organicBeforeHit,
        CombatWoundMechanism mechanism = CombatWoundMechanism.Unspecified)
    {
        if (item is not { Type: FcsRecordType.Weapon } || !aliveBeforeHit || !organicBeforeHit || race.IsRobot
            || !float.IsFinite(race.BleedRate) || race.BleedRate <= 0
            || !float.IsFinite(damage.Cut) || damage.Cut < 0 || !float.IsFinite(damage.Pierce) || damage.Pierce < 0
            || !float.IsFinite(damage.Blunt) || damage.Blunt < 0 || !float.IsFinite(damage.Scale) || damage.Scale <= 0) return false;
        double opening = (double)damage.Cut + damage.Pierce + (mechanism == CombatWoundMechanism.Crush ? .35 * damage.Blunt : 0);
        float deposit = (float)Math.Clamp(opening * damage.Scale * race.BleedRate / 100, 0, 1);
        if (deposit <= 0) return false;
        Adopt(item);
        if (!stains.TryGetValue(item, out var stain)) stains.Add(item, stain = new());
        var colour = new Vector3((race.BloodColour >> 16) & 255, (race.BloodColour >> 8) & 255, race.BloodColour & 255) / 255f;
        var before = stain.Blood;
        float amount = float.IsFinite(before.W) ? Math.Clamp(before.W, 0, 1) : 0;
        var previous = new Vector3(before.X, before.Y, before.Z);
        previous = Finite(previous) ? Vector3.Clamp(previous, Vector3.Zero, Vector3.One) : colour;
        float total = amount + deposit;
        var next = new Vector4((previous * amount + colour * deposit) / total, MathF.Min(total, 1));
        if (next == before) return false;
        stain.Blood = next; stain.Revision = ++revision; dirty = true;
        CutEquipmentAdapter.TrackItem(item, this);
        if (stain.Key is { } key) durable[key] = next;
        Publish(); return true;
    }
    static bool Finite(Vector3 colour) => float.IsFinite(colour.X) && float.IsFinite(colour.Y) && float.IsFinite(colour.Z);
    internal void AssignIdentity(ItemInstance item, string key)
    {
        ObserveContainerSerial(key);
        if (!stains.TryGetValue(item, out var stain))
        {
            if (!durable.TryGetValue(key, out var blood)) return;
            stains.Add(item, stain = new() { Blood = blood, Revision = ++revision }); dirty = true;
        }
        if (stain.Key == key) return;
        if (stain.Key is { } old && old != key) durable.Remove(old);
        stain.Key = key; durable[key] = stain.Blood; CutEquipmentAdapter.TrackItem(item, this);
    }
    void Adopt(ItemInstance item)
    {
        ImportLegacyItem(item);
        if (CutEquipmentAdapter.ItemKey(item) is { } key) AssignIdentity(item, key);
    }
    internal void Copy(ItemInstance source, ItemInstance copy)
    {
        if (!TryGet(source, out var view)) return;
        stains[copy] = new() { Blood = view.Blood, Revision = ++revision }; dirty = true; CutEquipmentAdapter.TrackItem(copy, this); Publish();
    }
    public void Schedule(World world)
    {
        BindWorld(world);
        if (durable.Count == 0 && authoritativeSidecar) return;
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Cold(slot)?.Inventory is { } inventory) AdoptInventory(inventory, 0);
        foreach (var ground in world.GroundItems.All) { Adopt(ground.Item); if (ground.Item.Contents is { } bag) AdoptInventory(bag, 0); }
        Publish();
    }
    void AdoptInventory(Inventory inventory, int depth)
    {
        if (depth > 4) return;
        foreach (var item in inventory.Items) { Adopt(item); if (item.Contents is { } bag) AdoptInventory(bag, depth + 1); }
    }
    void Publish()
    {
        if (!dirty) return;
        Views = Array.AsReadOnly(stains.Select(row => new WeaponStainView(row.Key, row.Value.Blood, row.Value.Revision)).ToArray()); dirty = false;
    }
    public void Write(Stream stream)
    {
        foreach (var (item, stain) in stains) { Adopt(item); if (stain.Key is { } key) durable[key] = stain.Blood; }
        JsonSerializer.Serialize(stream, new Document(1, durable.OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => new SavedStain(row.Key, row.Value.X, row.Value.Y, row.Value.Z, row.Value.W)).ToArray(), containerNamespace, containerSerial));
    }
    public void Read(Stream stream)
    {
        authoritativeSidecar = true; stains.Clear(); durable.Clear(); dirty = true; frameHistory.Clear(); frames = []; Views = [];
        try
        {
            if (JsonSerializer.Deserialize<Document>(stream) is not { SchemaVersion: 1, Items: { Length: <= 100000 } items } document) return;
            namespaceInitialized = Guid.TryParseExact(document.ContainerNamespace, "N", out var parsed);
            containerNamespace = namespaceInitialized ? parsed.ToString("N") : EmptyNamespace;
            containerSerial = Math.Max(0, document.ContainerSerial);
            foreach (var row in items)
                if (row is not null && row.Key is { Length: > 0 and <= 2048 } && Finite(new(row.R, row.G, row.B)) && float.IsFinite(row.Amount)
                    && row.R is >= 0 and <= 1 && row.G is >= 0 and <= 1 && row.B is >= 0 and <= 1 && row.Amount is > 0 and <= 1)
                    { durable[row.Key] = new(row.R, row.G, row.B, row.Amount); ObserveContainerSerial(row.Key); }
        }
        catch (JsonException) { }
        Publish();
    }
}
