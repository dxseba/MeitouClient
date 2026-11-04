using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;
namespace Meitou.Brutality.Physics;

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
