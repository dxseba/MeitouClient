using System.Collections.ObjectModel;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

[Flags]
public enum WeaponBloodPresentationGap { None = 0, AmbiguousAttachment = 1 }
public readonly record struct WeaponStainAttachment(ItemInstance Item, CharacterPart SourcePart, Vector4 Blood, long Revision);
public sealed record WeaponStainCharacter(CharacterId Character, CharacterAppearance Appearance,
    IReadOnlyList<WeaponStainAttachment> Parts, WeaponBloodPresentationGap Gaps);
public sealed record WeaponBloodFrame(long Tick, IReadOnlyDictionary<CharacterId, WeaponStainCharacter> Characters);

public sealed partial class WeaponBloodState
{
    readonly Queue<WeaponBloodFrame> frameHistory = new();
    volatile WeaponBloodFrame[] frames = [];
    public WeaponBloodFrame? FrameAt(long tick)
    {
        var available = frames;
        for (int i = available.Length - 1; i >= 0; i--) if (available[i].Tick <= tick) return available[i];
        return null;
    }
    public void SlowWorld(World world)
    {
        var result = new Dictionary<CharacterId, WeaponStainCharacter>();
        var previous = frames.LastOrDefault();
        if (stains.Count > 0 || durable.Count > 0)
            for (int slot = 0; slot < world.Characters.HighWater; slot++)
            {
                if (!world.Characters.Next[slot].Alive || world.Characters.Cold(slot) is not { Appearance: { } look, Fighter: { } fighter, Inventory: { } inventory } cold) continue;
                var parts = new List<WeaponStainAttachment>(); var gaps = WeaponBloodPresentationGap.None;
                foreach (var part in look.Parts)
                {
                    if (part.Mode != AttachMode.Bone || part.Record.Type != FcsRecordType.Weapon || part.Field == "sheath" || part.Point == "back2") continue;
                    ItemInstance? item = part.Shown switch
                    {
#if BRUTALITY_STOCK_MASTER
                        WeaponShown.Drawn or WeaponShown.Sheathed => StockWeaponItems.InSlot(cold, back: false),
                        WeaponShown.BackDrawn or WeaponShown.BackSheathed => StockWeaponItems.InSlot(cold, back: true),
#else
                        WeaponShown.Drawn or WeaponShown.Sheathed => fighter.HipWeapon?.SourceItem,
                        WeaponShown.BackDrawn or WeaponShown.BackSheathed => fighter.BackWeapon?.SourceItem,
#endif
                        WeaponShown.Always => Unambiguous(part, cold),
                        _ => null,
                    };
                    if (item is not null && item.Record == part.Record.StringId && TryGet(item, out var blood))
                        parts.Add(new(item, part, blood.Blood, blood.Revision));
                    else if (item is null && inventory.Items.Any(candidate => candidate.Type == FcsRecordType.Weapon && candidate.Record == part.Record.StringId && stains.ContainsKey(candidate)))
                        gaps |= WeaponBloodPresentationGap.AmbiguousAttachment;
                }
                if (parts.Count == 0 && gaps == WeaponBloodPresentationGap.None) continue;
                var id = world.Characters.IdOf(slot);
                var old = previous?.Characters.GetValueOrDefault(id);
                result[id] = old is not null && ReferenceEquals(old.Appearance, look) && old.Gaps == gaps && old.Parts.SequenceEqual(parts)
                    ? old : new(id, look, Array.AsReadOnly(parts.ToArray()), gaps);
            }
#if BRUTALITY_STOCK_MASTER
        StockWeaponGround.Publish(world, this);
#endif
        frameHistory.Enqueue(new(world.Tick + 1, new ReadOnlyDictionary<CharacterId, WeaponStainCharacter>(result)));
        while (frameHistory.Count > 64) frameHistory.Dequeue();
        frames = frameHistory.ToArray();
    }
    static ItemInstance? Unambiguous(CharacterPart part, CharacterCold cold)
    {
#if BRUTALITY_STOCK_MASTER
        var source = part.Point switch
        {
            "hip" => StockWeaponItems.InSlot(cold, back: false),
            "back" => StockWeaponItems.InSlot(cold, back: true),
            "hands" when cold.Fighter is { Weapon: not null } active => StockWeaponItems.InSlot(cold, active.WeaponIsBack),
            _ => null,
        };
#else
        var source = part.Point switch { "hip" => cold.Fighter?.HipWeapon?.SourceItem, "back" => cold.Fighter?.BackWeapon?.SourceItem, "hands" => cold.Fighter?.Weapon?.SourceItem, _ => null };
#endif
        if (source is null || cold.Inventory is not { } inventory || source.Record != part.Record.StringId) return null;
        // The serializer's Always tag can also name spare weapons. Only a unique actual item at that point is safe.
        var candidates = inventory.Items.Where(item => item.Type == FcsRecordType.Weapon && item.Record == part.Record.StringId
            && (part.Point == "hands" || (item.Section == "back" && item.Y > 0 ? "back2" : item.Section) == part.Point)).ToArray();
        return candidates.Length == 1 && ReferenceEquals(candidates[0], source) ? source : null;
    }
}
