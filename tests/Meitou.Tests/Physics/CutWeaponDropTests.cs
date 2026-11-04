using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;
using static Meitou.Tests.Bodies.BodyFixtures;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Physics;

public class CutWeaponDropTests
{
    [Fact]
    public void A_torso_cut_releases_a_crossed_mount_patch_but_not_safe_mounts_on_either_half()
    {
        var cut = new TorsoBisectionState(new(0, 1, 0, -10));
        var crossed = Mount(new(0, 10, 0), new(0, 9, 0), new(0, 11, 0), new(1, 10, 0));
        Assert.True(CutWeaponDrops.Unsupported(crossed, cut, false, null));
        foreach (float height in new[] { 5f, 15f })
        {
            var safe = Mount(new(0, height, 0), new(0, height - 1, 0), new(0, height + 1, 0), new(1, height, 0));
            Assert.False(CutWeaponDrops.Unsupported(safe, cut, false, null));
        }
        Assert.False(CutWeaponDrops.Unsupported(crossed, null, false, null));
    }

    [Fact]
    public void An_offset_mount_cannot_bridge_the_cut_from_a_bone_in_the_other_half()
    {
        var cut = new TorsoBisectionState(new(0, 1, 0, -10));
        var mount = Mount(new(0, 12, 0), new(0, 11, 0), new(0, 13, 0), new(1, 12, 0)) with { Bone = new(0, 8, 0) };
        Assert.True(CutWeaponDrops.Unsupported(mount, cut, false, null));
        Assert.False(CutWeaponDrops.Unsupported(mount with { Bone = new(0, 11, 0) }, cut, false, null));
    }

    [Fact]
    public void Only_the_lost_side_of_a_limb_cut_loses_its_weapon_support()
    {
        var limbPlane = new Vector4(0, 1, 0, -10);
        var lostHand = Mount(new(0, 7, 0), new(0, 6, 0), new(0, 8, 0), new(1, 7, 0)) with { Group = 2 };
        var retained = Mount(new(0, 13, 0), new(0, 12, 0), new(0, 14, 0), new(1, 13, 0)) with { Group = 2 };
        Assert.True(CutWeaponDrops.Unsupported(lostHand, null, true, limbPlane));
        Assert.False(CutWeaponDrops.Unsupported(retained, null, true, limbPlane));
        Assert.False(CutWeaponDrops.Unsupported(lostHand, null, false, limbPlane));
        Assert.True(CutWeaponDrops.Unsupported(lostHand, null, true, null));
        // A transverse torso loss does not cut an intact arm; a full-body plane may cut that same patch.
        var plane = new TorsoBisectionState(new(1, 0, 0, -.5f), true);
        Assert.True(CutWeaponDrops.Unsupported(lostHand, plane, false, null));
        Assert.False(CutWeaponDrops.Unsupported(lostHand, new(new(0, 1, 0, -7)), false, null));
    }

    [Fact]
    public void A_broken_mount_moves_the_exact_weapon_once_and_it_remains_recoverable_with_its_grade_blood_and_other_loot()
    {
        using var world = new Meitou.Simulation.World(new WorldSettings(), new OpenGroundWalkability((_, _) => 3), []);
        var sword = new ItemInstance { Record = "sword", Name = "Old blade", Type = FcsRecordType.WEAPON, Section = "hip",
            Level = 80, Quality = 73, CompanyId = "smith", MaterialId = "steel", WeaponBlood = new(.3f, .05f, .02f, .8f) };
        var loot = new ItemInstance { Record = "food" };
        var cold = new CharacterCold { Inventory = new() };
        cold.Inventory.Items.AddRange([sword, loot]);
        var id = world.Characters.Spawn(new() { Position = new(20, 15, 30), Yaw = .7f }, cold, 0);
        Assert.True(CutWeaponDrops.Drop(world, id, cold, sword, "Bip01 Pelvis"));
        Assert.False(CutWeaponDrops.Drop(world, id, cold, sword, "Bip01 Pelvis"));
        var ground = Assert.Single(world.GroundItems.All);
        Assert.Same(sword, ground.Item);
        Assert.Equal(new Vector3(20, 3, 30), ground.Position);
        Assert.True(ground.Dropped);
        Assert.Equal(id, ground.Fall!.Value.Character);
        Assert.Equal("Bip01 Pelvis", ground.Fall.Value.Bone);
        Assert.Equal(world.Tick, ground.Fall.Value.Tick);
        Assert.Equal(80, ground.Item.Level);
        Assert.Equal(73f, ground.Item.Quality);
        Assert.Equal("smith", ground.Item.CompanyId);
        Assert.Equal("steel", ground.Item.MaterialId);
        Assert.Equal(new(.3f, .05f, .02f, .8f), ground.Item.WeaponBlood);
        Assert.Same(loot, Assert.Single(cold.Inventory.Items));
        var recovered = world.GroundItems.Take(ground.Id);
        Assert.NotNull(recovered);
        Assert.Same(sword, recovered.Item);
        Assert.Null(world.GroundItems.Take(ground.Id));
    }

    [Theory]
    [InlineData(FcsRecordType.WEAPON)]
    [InlineData(FcsRecordType.CROSSBOW)]
    public void A_weapon_falling_from_the_severed_arms_bone_cannot_become_that_arms_loot_link(FcsRecordType type)
    {
        var (rig, definition) = LimbSeveranceGeometryTests.HumanRig();
        var setup = new CharacterPoseSetup { Rig = rig };
        var database = new GameDatabase();
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "drop-link-race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        database.Apply(file, "drop-link-tests.mod");
        const string bone = "Bip01 L Forearm";
        var race = new RaceData { Anatomy = Human.Anatomy.Select(entry => entry.Part == LeftArm
            ? entry with { Part = LeftArm with { BoneName2 = bone } } : entry).ToArray() };
        var cold = new CharacterCold { Race = race, Medical = Healthy(race), Inventory = new(),
            Appearance = new() { Race = database.Find("drop-link-race")! } };
        cold.Medical.Sever(LArm, Context(race));
        cold.Medical.KnockOut(10000);
        var weapon = new ItemInstance { Record = "dropped-weapon", Type = type, Section = "hip" };
        cold.Inventory.Items.Add(weapon);
        using var physics = new RagdollSystem(new(database, _ => null, _ => null), new Flat(), BodyOptions.Default with { Gore = true, AdvancedRagdolls = true },
            _ => setup, (_, _, _) => definition);
        using var world = new Meitou.Simulation.World(new WorldSettings(), new OpenGroundWalkability((_, _) => 0), [physics]);
        var id = world.Characters.Spawn(new() { Position = new(20, 25, 20) }, cold, 0);
        Assert.True(CutWeaponDrops.Drop(world, id, cold, weapon, bone));
        world.RunTick();
        var arm = Assert.Single(Assert.Single(world.Snapshot.Characters).DetachedRagdolls);
        Assert.Equal(0, arm.GroundItemId);
        var ground = Assert.Single(world.GroundItems.All);
        Assert.True(ground.Dropped);
        Assert.Equal(0f, ground.Position.Y);
        Assert.Same(weapon, world.GroundItems.Take(ground.Id)!.Item);
        world.RunTick();
        Assert.Single(Assert.Single(world.Snapshot.Characters).DetachedRagdolls);
    }

    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    static CutWeaponMount Mount(Vector3 at, Vector3 a, Vector3 b, Vector3 c) => new(0, at, at, a, b, c);
}
