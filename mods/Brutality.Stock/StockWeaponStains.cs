using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Finds the exact equipped item behind the stock fighter's weapon value.</summary>
internal static class StockWeaponItems
{
    internal static ItemInstance? InSlot(CharacterCold cold, bool back)
    {
        if (cold is not { Fighter: { IsAnimal: false } fighter, Inventory: { } inventory }
            || (back ? fighter.BackWeapon : fighter.HipWeapon) is not { Data.StringId: { } record }) return null;
        string section = back ? "back" : "hip";
        // CharacterAssembly.MakeFighter chooses the first valid WEAPON in this section. A record
        // matching the fighter necessarily passed the same native weapon lookup.
        foreach (var item in inventory.Items)
            if (item.Type == FcsRecordType.Weapon && item.Section == section && item.Record == record) return item;
        return null;
    }

    internal static ItemInstance? Swing(CharacterCold? cold, CombatSystem combat, in Effect effect)
    {
        int technique = (int)effect.Position.X;
        if (cold is not { Fighter: { IsAnimal: false, Unarmed: false, Weapon: not null } fighter }
            || (uint)technique >= (uint)combat.Techniques.Count || combat.Techniques[technique].LimbOf(effect.Arg) != 4) return null;
        return InSlot(cold, fighter.WeaponIsBack);
    }
}

internal static class StockWeaponStains
{
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    internal static bool Enabled { get; private set; }

    internal static void Install(ModContext context)
    {
        try
        {
            if (!StockWeaponPresentation.Install(context)) return;
            StockWeaponWetness.Install(context);
            CutEquipmentAdapter.Install(context.Harmony);
            context.AddSystem("weapon-blood", _ => new WeaponBloodState(context.RngPurpose), SystemAnchor.AfterPart(StandardParts.Combat));
            Enabled = true;
            context.Log("Brutality stock weapon stains initialized");
            StockWeaponGround.Install(context);
            StockFlatWeaponTint.Install(context);
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock weapon stains unavailable: {error.GetBaseException().Message}");
        }
    }

    internal static void Observe(World world, in CombatHit hit)
    {
        if (hit.Source != CombatHitSource.Melee || !hit.VictimWasAlive
            || !StockTrainingHook.TryGetWeapon(world, hit, out var item, out var race)
            || WeaponBloodState.For(world) is not { } state) return;

        // A club's crushing contact can stain it. For non-blunt weapons the delivered packet
        // determines the opening regardless of slash versus thrust.
        var mechanism = world.Characters.Cold(hit.Attacker.Slot)?.Fighter?.Weapon?.Data.SkillCategory == (int)WeaponCategory.Blunt
            ? CombatWoundMechanism.Crush : CombatWoundMechanism.Slash;
        if (state.Deposit(item, race, hit.Damage, aliveBeforeHit: true, organicBeforeHit: true, mechanism) && trace)
            Console.WriteLine(FormattableString.Invariant($"Brutality stock weapon stain actor={hit.Attacker} weapon={item.Record} victimRace={race.StringId} tick={hit.Tick} cut={hit.Damage.Cut:F1}"));
    }
}
