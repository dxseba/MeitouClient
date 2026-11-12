using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Maps a resolved hit to the actual outer and inner layers that covered it.</summary>
internal static class StockGarmentHits
{
    internal static float MinimumSkinCut(float maximum, bool covered) => covered ? Math.Max(.8f, maximum * .025f) : .15f;

    internal static bool Observe(World world, in CombatHit hit, CharacterCold victim, BodyPartTemplate part, float maximum,
        in DamagePacket packet, CombatWoundMechanism mechanism, int weapon)
    {
        if (victim.Inventory is not { } inventory || victim.Fighter is not { } fighter || fighter.Armour.Length == 0)
            return false;
        var armour = fighter.Armour;
        var layers = new List<(ItemInstance Item, ArmourPiece Armour, bool Protected)>();
        bool covered = false;
        int index = 0;
        foreach (var item in inventory.Items)
        {
            if (index >= armour.Length) break;
            if (item.Type != FcsRecordType.Armour || !InventoryCommands.IsEquipmentSection(item.Section)
                || item.Record != armour[index].Data.StringId) continue;
            var piece = armour[index];
            float roll = CombatRolls.Float(world.Seed, Rng.Key(hit.Victim), CombatRoll.Cover,
                CombatRolls.BlowCounter(hit.Attacker.Slot, hit.Sequence, hit.Blow, index++));
            bool protects = ArmourStack.Covers(piece.Data, part.StringId, roll);
            covered |= protects;
            if (protects || piece.Data.Material == ArmourType.Cloth && FitsClothSlot(item.Section, part))
                layers.Add((item, piece, protects));
        }
        if (layers.Count == 0 || StockGarmentDamage.For(world) is not { } state) return covered;

        float cut = packet.Cut, pierce = packet.Pierce, blunt = packet.Blunt;
        var probe = new WoundBody();
        if (!probe.Apply(hit with { Damage = new(cut, blunt, pierce) }, part, maximum, mechanism, weapon,
                detailedSites: victim.Race is { IsRobot: false }) || probe.Snapshot().Entries is not [var wound, ..]) return covered;

        float minimum = Math.Max(1, maximum * .015f);
        float cutReduction = 0, bluntReduction = 0, pierceDefence = 0;
        foreach (var (item, piece, protects) in layers.OrderBy(layer => layer.Item.Section == "shirt"))
        {
            float pressure = mechanism == CombatWoundMechanism.Crush ? blunt + .25f * cut : cut + pierce;
            if (!float.IsFinite(pressure) || pressure < minimum) break;
            var material = piece.Data.Material switch
            {
                ArmourType.Leather => GarmentMaterial.Leather,
                ArmourType.Chain or ArmourType.MetalPlate => GarmentMaterial.Metal,
                _ => GarmentMaterial.Cloth,
            };
            float strength = material == GarmentMaterial.Cloth ? .24f : material == GarmentMaterial.Leather ? .36f : .54f;
            float severity = Math.Clamp(pressure / (maximum * strength), .05f, 1);
            if (mechanism != CombatWoundMechanism.Crush || material == GarmentMaterial.Metal || severity >= .48f)
            {
                float received = mechanism == CombatWoundMechanism.Crush ? cut + blunt + pierce : cut + pierce;
                float size = WoundPolicy.VisualSize(received, maximum, mechanism, weapon);
                state.Deposit(item, new(wound with { Mark = wound.Mark with { Size = size } }, material, severity));
            }
            if (!protects) continue;
            cutReduction += piece.CutDefence * (1 - cutReduction);
            bluntReduction += piece.BluntDefence * (1 - bluntReduction);
            pierceDefence += piece.PierceDefence;
            cut = packet.Cut * (1 - EffectiveReduction(cutReduction, packet.ArmourPenetration));
            blunt = packet.Blunt * (1 - EffectiveReduction(bluntReduction, packet.ArmourPenetration));
            pierce = Math.Max(0, packet.Pierce - pierceDefence);
        }
        return covered;
    }

    // Light shirts can have no coverage in the damage roll. Projection still checks the actual cloth surface.
    static bool FitsClothSlot(string section, BodyPartTemplate part) => section switch
    {
        "head" => part.Type == BodyPartType.Head,
        "legs" or "boots" => part.LimbSlot is 2 or 3,
        "armour" or "shirt" => part.LimbSlot is 0 or 1 || ChestHolePolicy.RegionOf(part.StringId) >= 0,
        _ => false,
    };

    static float EffectiveReduction(float reduction, float penetration) => penetration == 0
        ? Math.Clamp(reduction, 0, 1) : Math.Clamp(reduction * (1 - penetration), 0, .9f);
}
