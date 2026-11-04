using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    sealed class CutEquipmentStamp
    {
        public TorsoBisectionState? Torso;
        public IReadOnlyList<LimbCutState> Limbs = [];
        public uint Missing;
    }

    readonly ConditionalWeakTable<CharacterCold, CutEquipmentStamp> cutEquipment = new();

    void DropUnsupportedCutWeapons(World world, CharacterId id, CharacterCold cold)
    {
        if (options is not { AdvancedRagdolls: true } || cold is not { Appearance: { Animal: null } look, Inventory: { } inventory, Medical: { } medical }) return;
        uint missing = 0;
        foreach (var part in medical.PartSpan)
            if (part.Template.LimbSlot is >= 0 and <= 3 and var slot && part.Limb == LimbState.Stump) missing |= 1u << slot;
        if (cold.TorsoBisection is null && missing == 0) return;
        var stamp = cutEquipment.GetValue(cold, static _ => new());
        if (stamp.Torso == cold.TorsoBisection && stamp.Missing == missing
            && (ReferenceEquals(stamp.Limbs, cold.LimbSeverances) || stamp.Limbs.SequenceEqual(cold.LimbSeverances))) return;
        if (SetupOf(look) is not { Rig: { } rig }) return;
        stamp.Torso = cold.TorsoBisection; stamp.Missing = missing; stamp.Limbs = cold.LimbSeverances;
        bool changed = false;
        var surface = SkinSurfaceFor(look);
        foreach (var item in inventory.Items.ToArray())
        {
            if (item.Type is not (FcsRecordType.WEAPON or FcsRecordType.CROSSBOW) || item.Section is not ("hip" or "back")) continue;
            bool bowDrawn = cold.DrawnWeapon == HandHold.OfCategory((int)Meitou.Data.Gameplay.Combat.WeaponCategory.Bow).Weapon;
            bool inHand = cold.DrawnWeapon != WeaponKinds.None && (item.Type == FcsRecordType.WEAPON
                ? !bowDrawn && ReferenceEquals(cold.Fighter?.Weapon?.SourceItem, item)
                : bowDrawn && ReferenceEquals(item, inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "back")
                    ?? inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.CROSSBOW && i.Section == "hip")));
            string pointName = inHand ? "hands" : item.Section == "back" && item.Y > 0 ? "back2" : item.Section;
            if (!look.AttachmentPoints.TryGetValue(pointName, out var point) || rig.Handle(point.Bone) is not int bone) continue;
            var extra = look.Parts.FirstOrDefault(p => p.Record.StringId == item.Record && p.Point == pointName && p.Field != "sheath")?.PointOffset ?? default;
            var at = rig.BindPosition[bone] + Vector3.Transform((point.Position + extra) * rig.BindScale[bone].Y, Quaternion.Inverse(rig.BindRotationInverse[bone]));
            // Prop2 is animation-driven; the right hand is the physical material supporting a drawn weapon.
            if (inHand && rig.Handle("Bip01 R Hand") is int hand) { bone = hand; at = rig.BindPosition[hand]; }
            int group = CutWeaponDrops.Group(rig.ByHandle[bone]?.Name ?? "");
            var support = surface?.EquipmentSupport(rig, at, group);
            var mount = new CutWeaponMount(group, rig.BindPosition[bone], at, support?.A ?? at, support?.B ?? at, support?.C ?? at);
            Vector4? limbPlane = null;
            int limb = group - 1;
            if (limb is >= 0 and <= 3)
                foreach (var cut in cold.LimbSeverances)
                    if (cut.LimbSlot == limb && LimbCutGeometry.TryPlane(rig, cut, out var plane)) { limbPlane = plane; break; }
            if (CutWeaponDrops.Unsupported(mount, cold.TorsoBisection, limb is >= 0 and <= 3 && (missing & 1u << limb) != 0, limbPlane))
                changed |= CutWeaponDrops.Drop(world, id, cold, item, rig.ByHandle[bone]?.Name ?? point.Bone);
        }
        if (changed) world.Systems.OfType<PopulationSystem>().FirstOrDefault()?.RefreshCutEquipment(cold);
    }
}

internal readonly record struct CutWeaponMount(int Group, Vector3 Bone, Vector3 Position, Vector3 A, Vector3 B, Vector3 C);

internal static class CutWeaponDrops
{
    internal static int Group(string name)
    {
        bool left = name.Contains(" L ", StringComparison.Ordinal), right = name.Contains(" R ", StringComparison.Ordinal);
        if (!left && !right || name.Contains("Clavicle", StringComparison.Ordinal)) return 0;
        bool leg = name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal)
            || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal);
        return (leg ? 3 : 1) + (right ? 1 : 0);
    }

    internal static bool Unsupported(in CutWeaponMount mount, TorsoBisectionState? torso, bool limbLost, Vector4? limbPlane)
    {
        if (limbLost && (limbPlane is not { } plane || MathF.Min(Distance(plane, mount.A), MathF.Min(Distance(plane, mount.B), Distance(plane, mount.C))) <= .0001f)) return true;
        if (torso is not { IsValid: true } cut || !cut.FullBody && mount.Group != 0) return false;
        float a = Distance(cut.Plane, mount.A), b = Distance(cut.Plane, mount.B), c = Distance(cut.Plane, mount.C);
        // Cutting the supporting skin patch or the connection from its bone to its offset mount breaks that attachment.
        return MathF.Min(a, MathF.Min(b, c)) <= .0001f && MathF.Max(a, MathF.Max(b, c)) >= -.0001f
            || Distance(cut.Plane, mount.Bone) * Distance(cut.Plane, mount.Position) < 0;
    }

    static float Distance(Vector4 plane, Vector3 at) => Vector4.Dot(plane, new(at, 1));

    internal static bool Drop(World world, CharacterId id, CharacterCold cold, ItemInstance item, string bone)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || cold.Inventory is not { } inventory || !inventory.Items.Remove(item)) return false;
        ref readonly var hot = ref world.Characters.Next[slot];
        var at = hot.Position;
        at.Y = world.Walkability.GroundHeight(at.X, at.Z, at.Y);
        item.Section = "main";
        world.GroundItems.Drop(item, at, hot.Yaw, new FallSource(id, bone, world.Tick));
        return true;
    }
}
