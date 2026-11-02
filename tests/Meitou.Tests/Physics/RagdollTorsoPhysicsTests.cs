using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Physics.RagdollCutTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public class RagdollTorsoPhysicsTests
{
    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    internal sealed class Scene : IDisposable
    {
        public SimWorld World { get; }
        public RagdollSystem Physics { get; }
        public CharacterId Id { get; }
        public CharacterCold Cold { get; }
        public CharacterPoseSetup Setup { get; }
        public BodyOptions Options { get; }
        public RagdollDefinition SourceDefinition { get; }
        public Scene(bool advanced = true, SavedRagdoll? restore = null, bool torso = true, TorsoBisectionState? cut = null)
        {
            var skeleton = new OgreSkeleton();
            skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = new(0, 11, 0) });
            skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 L UpperArm", Position = new(3, -1.5f, 0) });
            skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 L Forearm", Position = new(0, -3, 0) });
            skeleton.Bones.Add(new() { Handle = 3, Parent = 2, Name = "Bip01 L Hand", Position = new(0, -2, 0) });
            var rig = new SkeletonRig(skeleton);
            Setup = new() { Rig = rig };
            var definition = Definition(
            [
                Box("Bip01", 0, new(0, 11, 0), new(3, 5, 2)),
                Box("Bip01 L UpperArm", 1, new(3, 9.5f, 0), new(1, 3, 1)),
                Box("Bip01 L Forearm", 2, new(3, 6.5f, 0), new(1, 3, 1)),
            ], [(0, 1, new Vector3(2, 11, 0)), (1, 2, new Vector3(3, 8, 0))]);
            // The non-physical hand still needs the full skeleton handle range.
            definition = new() { Bodies = definition.Bodies, Joints = definition.Joints, Root = 0, Joined = definition.Joined, BodyOfBone = [0, 1, 2, -1] };
            SourceDefinition = definition;
            var database = new GameDatabase();
            var file = new FcsFile();
            file.Records.Add(new() { StringId = "fragment-race", Name = "Fragment race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
            database.Apply(file, "fragment-tests.mod");
            var rigs = new CharacterRigs(database, _ => null, _ => null);
            Options = BodyOptions.Default with { AdvancedRagdolls = advanced };
            Physics = new(rigs, new Flat(), Options, _ => Setup, (_, _, _) => definition);
            World = new(new WorldSettings { Seed = 41, TickSeconds = 1f / 30 }, new OpenGroundWalkability((_, _) => 0), [Physics]);
            var race = new RaceData { Anatomy = [.. Human.Anatomy.Select(entry => entry.Part.LimbSlot == 0 ? entry with { Part = entry.Part with { BoneName2 = "Bip01 L UpperArm" } } : entry)] };
            Cold = new()
            {
                Name = "Cut corpse", Appearance = new() { Race = database.Find("fragment-race")! }, Race = race,
                Medical = Healthy(race), TorsoBisection = torso ? cut ?? new(new(0, 1, 0, -10), true) : null,
            };
            Cold.Medical.KillFromBisection();
            if (restore is not null)
                Cold.Save = new()
                {
                    Source = new(new SavePlatoon(new FcsRecord { StringId = "fragment-platoon", RecordType = FcsRecordType.PLATOON }, null), new FcsInstance()),
                    PlatoonName = "Fragment tests", Slot = 0, Ragdoll = restore,
                };
            Id = World.Characters.Spawn(new() { Position = restore?.Pose[0].Position ?? new(20, 15, 20), Health = 0 }, Cold, 0);
        }
        public CharacterSnapshot Snapshot => Assert.Single(World.Snapshot.Characters, c => c.Id == Id);
        public Vector3 BoneWorld(BonePose pose, int bone)
        {
            var shown = Snapshot;
            var placement = CharacterRigs.Placement(Setup, shown.Position, shown.Yaw, 1);
            if (!shown.CarriedBy.IsNone)
            {
                var (at, rotation) = (shown.Carry ?? CarryPose.Fallback).World(shown.Position, shown.Yaw);
                placement = CharacterRigs.Placement(Setup, at, rotation, 1);
            }
            return Vector3.Transform(pose.Position[bone], placement);
        }
        public void Dispose() { World.Dispose(); Physics.Dispose(); }
    }

    [Fact]
    public void A_disconnected_arm_falls_and_its_hand_follows_it_instead_of_floating_at_the_shoulder()
    {
        using var scene = new Scene();
        scene.World.RunTick();
        var initial = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        Assert.True(initial.Positive.AbsolutePositions);
        Assert.True(initial.Negative.AbsolutePositions);
        Assert.All(initial.Negative.Set, value => Assert.True(value));
        Assert.NotNull(initial.Negative.Components);
        Assert.NotEqual(initial.Negative.Components[0], initial.Negative.Components[2]);
        Assert.Equal(initial.Negative.Components[1], initial.Negative.Components[2]);
        Assert.Equal(initial.Negative.Components[2], initial.Negative.Components[3]);
        float before = scene.BoneWorld(initial.Negative, 2).Y;
        scene.World.RunTicks(240);
        var settled = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        var forearm = scene.BoneWorld(settled.Negative, 2);
        var hand = scene.BoneWorld(settled.Negative, 3);
        Assert.True(before > 15);
        Assert.InRange(forearm.Y, 0.1f, 3.5f);
        Assert.InRange(hand.Y, -0.5f, 4.5f);
        Assert.InRange(Vector3.Distance(forearm, hand), 1.9f, 2.1f);
    }

    [Fact]
    public void Publishing_the_next_tick_preserves_both_poses_in_the_shown_snapshot_pair()
    {
        using var scene = new Scene();
        scene.World.RunTick();
        var before = scene.Snapshot;
        var poses = Assert.IsType<TorsoRagdollPose>(before.TorsoRagdoll);
        var positive = (Vector3[])poses.Positive.Position.Clone();
        var negative = (Vector3[])poses.Negative.Position.Clone();
        scene.World.RunTick();
        Assert.Same(before, Assert.Single(scene.World.PreviousSnapshot.Characters, c => c.Id == scene.Id));
        Assert.Same(poses, before.TorsoRagdoll);
        Assert.Equal(positive, poses.Positive.Position);
        Assert.Equal(negative, poses.Negative.Position);
        var after = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        Assert.NotSame(poses, after);
        Assert.NotSame(poses.Positive, after.Positive);
        Assert.NotSame(poses.Negative, after.Negative);
    }

    [Fact]
    public void The_advanced_switch_selects_fragment_physics_and_can_return_to_the_legacy_fallback()
    {
        using var scene = new Scene(false);
        scene.World.RunTick();
        Assert.Null(scene.Snapshot.TorsoRagdoll);
        scene.Options.AdvancedRagdolls = true;
        scene.World.RunTick();
        Assert.NotNull(scene.Snapshot.TorsoRagdoll);
        scene.Options.AdvancedRagdolls = false;
        scene.World.RunTick();
        Assert.Null(scene.Snapshot.TorsoRagdoll);
    }

    [Fact]
    public void A_settled_cut_restores_both_physical_sides_without_replaying_the_fall()
    {
        using var scene = new Scene();
        scene.World.RunTicks(240);
        var saved = Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id));
        var halves = Assert.IsType<SavedTorsoRagdoll>(saved.Torso);
        Assert.True(halves.Positive.Frozen);
        Assert.True(halves.Negative.Frozen);
        var record = new FcsRecord();
        RagdollSave.Write(record, saved);
        var restored = Assert.IsType<SavedRagdoll>(RagdollSave.Read(record));
        using var loaded = new Scene(restore: restored);
        loaded.World.RunTick();
        var after = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(loaded.Physics.SaveState(loaded.Id)).Torso);
        Assert.Equal(halves.Cut, after.Cut);
        AssertPose(halves.Positive.Pose, after.Positive.Pose);
        AssertPose(halves.Negative.Pose, after.Negative.Pose);
        Assert.True(after.Positive.Frozen);
        Assert.True(after.Negative.Frozen);
        Assert.NotNull(loaded.Snapshot.TorsoRagdoll);
    }

    [Fact]
    public void Saved_fragment_physics_restores_the_cut_before_a_late_torso_sidecar_is_available()
    {
        using var scene = new Scene();
        scene.World.RunTicks(240);
        var original = Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id));
        var halves = Assert.IsType<SavedTorsoRagdoll>(original.Torso);
        var record = new FcsRecord();
        RagdollSave.Write(record, original);
        var saved = Assert.IsType<SavedRagdoll>(RagdollSave.Read(record));
        using var loaded = new Scene(restore: saved, torso: false);
        Assert.Null(loaded.Cold.TorsoBisection);
        loaded.World.RunTick();
        Assert.Equal(halves.Cut, loaded.Cold.TorsoBisection);
        Assert.NotNull(loaded.Snapshot.TorsoRagdoll);
        var restored = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(loaded.Physics.SaveState(loaded.Id)).Torso);
        AssertPose(halves.Positive.Pose, restored.Positive.Pose);
        AssertPose(halves.Negative.Pose, restored.Negative.Pose);
        Assert.Null(loaded.Cold.Save!.Ragdoll);
    }

    [Fact]
    public void Carrying_the_corpse_does_not_pick_a_disconnected_arm_back_up_with_it()
    {
        using var scene = new Scene();
        scene.World.RunTicks(240);
        var carrierCold = new CharacterCold { Name = "Carrier", Appearance = scene.Cold.Appearance, Race = Human, Medical = Healthy() };
        var carrier = scene.World.Characters.Spawn(new() { Position = new(20, 0, 20), Health = 100 }, carrierCold, scene.World.Tick);
        carrierCold.Carrying = scene.Id;
        scene.Cold.CarriedBy = carrier;
        scene.World.RunTick();
        var carried = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        RagdollTorsoGripTests.AssertPalmContact(scene,-1,carrier);
        Assert.InRange(scene.BoneWorld(carried.Negative, 2).Y, 0.1f, 3.5f);
        carrierCold.Carrying = CharacterId.None;
        scene.Cold.CarriedBy = CharacterId.None;
        scene.World.RunTicks(120);
        var dropped = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        Assert.InRange(scene.BoneWorld(dropped.Negative, 2).Y, 0.1f, 3.5f);
    }

    [Fact]
    public void The_positive_half_can_be_carried_without_lifting_the_negative_half()
    {
        using var scene = new Scene();
        scene.World.RunTicks(240);
        var before = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        var ground = scene.BoneWorld(before.Negative, 0);
        var carrierCold = new CharacterCold { Name = "Positive carrier", Appearance = scene.Cold.Appearance, Race = Human, Medical = Healthy() };
        var carrier = scene.World.Characters.Spawn(new() { Position = new(100, 0, 20), Health = 100 }, carrierCold, scene.World.Tick);
        carrierCold.Carrying = scene.Id;
        carrierCold.CarryingTorsoSide = 1;
        scene.Cold.TorsoPositiveCarriedBy = carrier;
        scene.World.RunTick();
        var held = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        var lifted = scene.BoneWorld(held.Positive, 0);
        RagdollTorsoGripTests.AssertPalmContact(scene,1,carrier);
        Assert.True(lifted.X > 80);
        Assert.InRange(Vector3.Distance(ground, scene.BoneWorld(held.Negative, 0)), 0, 0.1f);
        Assert.Equal(carrier, scene.Snapshot.TorsoPositiveCarriedBy);
        Assert.Equal(CharacterId.None, scene.Snapshot.CarriedBy);
        Assert.Equal(1, Assert.Single(scene.World.Snapshot.Characters, c => c.Id == carrier).CarryingTorsoSide);
        Assert.NotNull(scene.Snapshot.TorsoPositivePosition);
    }

    [Fact]
    public void Separate_carriers_can_hold_both_halves_and_dropping_one_does_not_release_the_other()
    {
        using var scene = new Scene();
        scene.World.RunTicks(240);
        var negativeCold = new CharacterCold { Name = "Negative carrier", Appearance = scene.Cold.Appearance, Race = Human, Medical = Healthy() };
        var negativeCarrier = scene.World.Characters.Spawn(new() { Position = new(20, 0, 20), Health = 100 }, negativeCold, scene.World.Tick);
        var positiveCold = new CharacterCold { Name = "Positive carrier", Appearance = scene.Cold.Appearance, Race = Human, Medical = Healthy() };
        var positiveCarrier = scene.World.Characters.Spawn(new() { Position = new(100, 0, 20), Health = 100 }, positiveCold, scene.World.Tick);
        negativeCold.Carrying = positiveCold.Carrying = scene.Id;
        positiveCold.CarryingTorsoSide = 1;
        scene.Cold.CarriedBy = negativeCarrier;
        scene.Cold.TorsoPositiveCarriedBy = positiveCarrier;
        scene.World.RunTick();
        var held = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        var negativeAt = scene.BoneWorld(held.Negative, 0);
        var positiveAt = scene.BoneWorld(held.Positive, 0);
        RagdollTorsoGripTests.AssertPalmContact(scene,-1,negativeCarrier);
        RagdollTorsoGripTests.AssertPalmContact(scene,1,positiveCarrier);
        Assert.True(Vector3.Distance(negativeAt, positiveAt) > 60);
        Assert.InRange(scene.BoneWorld(held.Negative, 2).Y, 0.1f, 3.5f);
        positiveCold.Carrying = CharacterId.None;
        positiveCold.CarryingTorsoSide = 0;
        scene.Cold.TorsoPositiveCarriedBy = CharacterId.None;
        scene.Cold.TorsoPositiveCarryAttach = null;
        scene.Cold.TorsoPositiveReleaseAt = new(120, 0, 20);
        scene.World.RunTicks(120);
        var dropped = Assert.IsType<TorsoRagdollPose>(scene.Snapshot.TorsoRagdoll);
        Assert.InRange(Vector3.Distance(negativeAt, scene.BoneWorld(dropped.Negative, 0)), 0, 0.1f);
        Assert.Equal(negativeCarrier, scene.Snapshot.CarriedBy);
        Assert.Equal(CharacterId.None, scene.Snapshot.TorsoPositiveCarriedBy);
        Assert.Null(scene.Cold.TorsoPositiveReleaseAt);
        Assert.InRange(scene.BoneWorld(dropped.Positive, 0).X, 115, 125);
        Assert.True(scene.BoneWorld(dropped.Positive, 0).Y < 8);
    }

    [Fact]
    public void Saved_fragment_poses_can_remain_far_from_the_logical_owner_and_each_other()
    {
        using var scene = new Scene();
        scene.World.RunTicks(500);
        var saved = Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id));
        var halves = Assert.IsType<SavedTorsoRagdoll>(saved.Torso);
        var farPositive = halves.Positive.Pose.Select(p => p with { Position = p.Position + new Vector3(2000, 0, 0) }).ToArray();
        var farNegative = halves.Negative.Pose.Select((p, i) => i == 0 ? p : p with { Position = p.Position + new Vector3(-2000, 0, 0) }).ToArray();
        saved = saved with { Pose = farNegative, Torso = halves with { Positive = halves.Positive with { Pose = farPositive }, Negative = halves.Negative with { Pose = farNegative } } };
        using var loaded = new Scene(restore: saved);
        loaded.World.RunTick();
        var restored = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(loaded.Physics.SaveState(loaded.Id)).Torso);
        AssertPose(farPositive, restored.Positive.Pose);
        AssertPose(farNegative, restored.Negative.Pose);
    }
    [Fact]
    public void An_ordinary_severed_arm_falls_with_its_loot_item_and_disappears_when_picked_up()
    {
        using var scene = new Scene(torso: false);
        scene.World.RunTick();
        scene.Cold.Medical!.Sever(LArm, Context(scene.Cold.Race), false);
        var item = new ItemInstance { Record = "severed-arm-test", Type = FcsRecordType.ITEM };
        scene.World.GroundItems.Drop(item, scene.Snapshot.Position, 0, new(scene.Id, "Bip01 L UpperArm", scene.World.Tick));
        int itemId = Assert.Single(scene.World.GroundItems.All).Id;
        scene.World.RunTick();
        Assert.Equal(itemId, Assert.Single(scene.Snapshot.DetachedRagdolls, p => p.Part == 0).GroundItemId);
        scene.World.RunTicks(240);
        var arm = Assert.Single(scene.Snapshot.DetachedRagdolls, p => p.Part == 0);
        Assert.InRange(scene.BoneWorld(arm.Bones, 2).Y, 0.1f, 3.5f);
        var loot = Assert.IsType<GroundItem>(scene.World.GroundItems.Find(itemId));
        Assert.Same(item, loot.Item);
        Assert.InRange(loot.Position.Y, 0.1f, 5);
        Assert.Same(item, Assert.IsType<GroundItem>(scene.World.GroundItems.Take(itemId)).Item);
        scene.World.RunTicks(2);
        Assert.DoesNotContain(scene.Snapshot.DetachedRagdolls, p => p.Part == 0);
    }

    static void AssertPose(RigidTransform[] expected, RigidTransform[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int b = 0; b < expected.Length; b++)
        {
            Assert.InRange(Vector3.Distance(expected[b].Position, actual[b].Position), 0, 0.0001f);
            Assert.True(MathF.Abs(Quaternion.Dot(expected[b].Rotation, actual[b].Rotation)) > 0.99999f);
        }
    }

    [Fact]
    public void Removing_the_owner_removes_its_fragment_publication_and_cannot_attach_it_to_a_reused_slot()
    {
        using var scene = new Scene();
        scene.World.RunTick();
        Assert.NotNull(scene.Snapshot.TorsoRagdoll);
        scene.World.Characters.Remove(scene.Id);
        scene.World.RunTick();
        Assert.DoesNotContain(scene.World.Snapshot.Characters, c => c.Id == scene.Id);
        Assert.Null(scene.Physics.PublishTorso(scene.Id));
        var replacement = scene.World.Characters.Spawn(new() { Position = new(20, 0, 20), Health = 100 }, new() { Name = "Replacement", Appearance = scene.Cold.Appearance }, scene.World.Tick);
        scene.World.RunTick();
        Assert.Equal(scene.Id.Slot, replacement.Slot);
        Assert.NotEqual(scene.Id, replacement);
        Assert.Null(Assert.Single(scene.World.Snapshot.Characters, c => c.Id == replacement).TorsoRagdoll);
    }
}
