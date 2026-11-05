using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Ogre;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Drops the original inventory item when its hand or its mounted skin patch no longer supports it.</summary>
public sealed partial class CutEquipmentAdapter(CharacterRigs? rigs, Action<CharacterCold> refresh, bool advancedRagdolls = true) : ITickSystem
{
    readonly record struct Stamp(TorsoBisectionState? Torso, int Missing, string Limbs);
    readonly Dictionary<CharacterCold, Stamp> checkedCuts = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<CharacterCold, (Meitou.Simulation.Items.ItemInstance? Item, bool Bow)> heldBefore = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, RagdollSkinSurface> skins = new(StringComparer.OrdinalIgnoreCase);
    public static void Install(ModContext context, CharacterRigs? rigs = null)
    {
        InstallPersistence(context.Harmony);
        var type = typeof(World).Assembly.GetType("Meitou.Simulation.CharacterAssembly", throwOnError: true)!;
        var assembly = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [context.Population], null)!;
        var method = AccessTools.Method(type, "RefreshEquipment", [typeof(CharacterCold)]);
        context.AddSystem("weapon-blood", _ => new WeaponBloodState(context.RngPurpose), SystemAnchor.AfterPart(StandardParts.Combat));
        context.AddSystem("cut-equipment", _ => new CutEquipmentAdapter(rigs, cold => method.Invoke(assembly, [cold]), context.Population.BodyOptions.AdvancedRagdolls), SystemAnchor.AfterPart(StandardParts.Animation));
    }
    public void Schedule(World world)
    {
        heldBefore.Clear();
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Cold(slot) is { Inventory: { } inventory, DrawnWeapon: not WeaponKinds.None } cold)
            {
                bool bow = cold.DrawnWeapon == HandHold.OfCategory((int)Meitou.Data.Gameplay.Combat.WeaponCategory.Bow).Weapon;
                var item = bow ? inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "back")
                    ?? inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "hip") : cold.Fighter?.Weapon?.SourceItem;
                heldBefore[cold] = (item, bow);
            }
    }
    public void SlowWorld(World world)
    {
        if (!advancedRagdolls || BrutalityRuntime.For(world) is not { } gameplay) return;
        var states = gameplay.ActorViews.ToDictionary(state => state.Character);
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
        {
            if (!world.Characters.Next[slot].Alive || world.Characters.Cold(slot) is not { Inventory: { } inventory, Medical: { } medical } cold) continue;
            var id = world.Characters.IdOf(slot); states.TryGetValue(id, out var state);
            int missing = 0;
            for (int limb = 0; limb < 4; limb++) if (medical.LimbPart(limb) is { Limb: LimbState.Stump }) missing |= 1 << limb;
            if (state?.Torso is null && missing == 0) continue;
            var stamp = new Stamp(state?.Torso, missing, state is null ? "" : string.Join(';', state.Limbs));
            if (checkedCuts.TryGetValue(cold, out var before) && before == stamp) continue;
            checkedCuts[cold] = stamp;
            bool changed = false;
            var look = cold.Appearance;
            var setup = rigs is not null && look is { Animal: null } ? rigs.For(look) : null;
            RagdollSkinSurface? skin = null;
            if (setup is not null && rigs!.FindMesh(look!.BodyMesh) is { } bodyPath)
            {
                if (!skins.TryGetValue(bodyPath, out skin)) skins[bodyPath] = skin = RagdollSkinSurface.Read(OgreMeshReader.ReadFile(bodyPath));
            }
            bool bowDrawn = cold.DrawnWeapon == HandHold.OfCategory((int)Meitou.Data.Gameplay.Combat.WeaponCategory.Bow).Weapon;
            foreach (var item in inventory.Items.ToArray())
            {
                if (item.Type is not (FcsRecordType.WEAPON or FcsRecordType.CROSSBOW) || item.Section is not ("hip" or "back")) continue;
                bool inHand = heldBefore.TryGetValue(cold, out var held) && ReferenceEquals(held.Item, item)
                    || cold.DrawnWeapon != WeaponKinds.None && (item.Type == FcsRecordType.WEAPON
                    ? !bowDrawn && ReferenceEquals(cold.Fighter?.Weapon?.SourceItem, item)
                    : bowDrawn && ReferenceEquals(inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "back")
                        ?? inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "hip"), item));
                // The real right hand is also an unambiguous support when art/rig files are not available.
                if (setup is null || look is null)
                {
                    if (inHand && (missing & 2) != 0) changed |= CutWeaponDrops.Drop(world, id, cold, item, "Bip01 R Hand");
                    continue;
                }
                string pointName = inHand ? "hands" : item.Section == "back" && item.Y > 0 ? "back2" : item.Section;
                if (!look.AttachmentPoints.TryGetValue(pointName, out var point) || setup.Rig.Handle(point.Bone) is not int bone) continue;
                var rig = setup.Rig;
                var extra = look.Parts.FirstOrDefault(part => part.Record.StringId == item.Record && part.Point == pointName && part.Field != "sheath")?.PointOffset ?? default;
                var at = rig.BindPosition[bone] + Vector3.Transform((point.Position + extra) * rig.BindScale[bone].Y, Quaternion.Inverse(rig.BindRotationInverse[bone]));
                string supportName = point.Bone;
                if (inHand && point.Bone == "Bip01 Prop2" && rig.Handle("Bip01 R Hand") is int hand) { bone = hand; supportName = "Bip01 R Hand"; at = rig.BindPosition[bone]; }
                int group = CutWeaponDrops.Group(supportName);
                var triangle = skin?.EquipmentSupport(rig, at, group);
                var mount = new CutWeaponMount(group, rig.BindPosition[bone], at, triangle?.A ?? at, triangle?.B ?? at, triangle?.C ?? at);
                int limb = group - 1;
                Vector4? plane = null;
                if (state?.Limbs.FirstOrDefault(cut => cut.LimbSlot == limb) is { IsValid: true } cut && LimbCutGeometry.TryPlane(rig, cut, out var value)) plane = value;
                if (CutWeaponDrops.Unsupported(mount, state?.Torso, limb >= 0 && (missing & (1 << limb)) != 0, plane))
                    changed |= CutWeaponDrops.Drop(world, id, cold, item, supportName);
            }
            if (changed) refresh(cold);
        }
    }
}
