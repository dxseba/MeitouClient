using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.MedkitMerge;

/// <summary>
/// Port of Medkit &amp; Bolt Merge v0.5.2 (Workshop 3803535940): partial first-aid kits, splint kits, robotics repair kits and crossbow bolt packs of
/// the same record, quality and capacity are poured together in a player character's inventory and worn backpack, after the character receives
/// one and after the inventory's Sort button. No supplies are created or lost (README "Facts").
/// </summary>
public sealed class MedkitMergeMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("merge", _ => new MergeSystem(context.Log), SystemAnchor.End);
        context.Log("MedkitMerge initialized: first-aid, splint and robotics repair kits and crossbow bolts; ownership ignored");
    }
}
