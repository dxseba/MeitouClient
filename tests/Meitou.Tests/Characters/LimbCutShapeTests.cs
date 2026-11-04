using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Ragdolls;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Physics.RagdollCutTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Characters;

public class LimbCutShapeTests
{
    [Fact]
    public void Only_explicitly_cut_limbs_regain_natural_sizes_while_other_stumps_sliders_and_live_build_are_preserved()
    {
        var (cache, look, rig) = Inputs();
        var source = Assert.IsType<CharacterShapeInput>(look.Shape);
        var build = new BodyBuild(.45f, .7f, .3f);
        var restored = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 1 | 4, build));
        Assert.True(restored.BoneSize[rig.Handle("Bip01 L Forearm")!.Value].Length() > 1);
        Assert.True(restored.BoneSize[rig.Handle("Bip01 L Hand")!.Value].Length() > 1);
        Assert.Equal(Vector3.Zero, restored.BoneSize[rig.Handle("Bip01 R Forearm")!.Value]);
        Assert.Equal(Vector3.Zero, restored.BoneSize[rig.Handle("Bip01 R UpperArm")!.Value]);
        Assert.True(restored.BoneSize[rig.Handle("Bip01 L Calf")!.Value].Length() > 1);
        Assert.Equal(Vector3.Zero, restored.BoneSize[rig.Handle("Bip01 R Calf")!.Value]);
        var expected = Compute(rig, source with
        {
            Missing = new HashSet<Limb> { Limb.RightArm, Limb.RightLeg }, HideStump = HideStump.RightArm,
            NarrowLeftThigh = false, Muscle = build.Muscle, Starvation = build.Starvation,
        });
        Assert.Equal(expected.BoneSize, restored.BoneSize);
        Assert.Equal(expected.PositionalSize, restored.PositionalSize);
        Assert.Equal(expected.MovementScale, restored.MovementScale);
        Assert.Equal(4, source.Missing.Count);
        Assert.Equal(HideStump.BothArms, source.HideStump);
        Assert.True(source.NarrowLeftThigh);
        var unchanged = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 0));
        Assert.Equal(Compute(rig, source).BoneSize, unchanged.BoneSize);
    }

    [Fact]
    public void Shape_cache_tracks_mutable_inputs_and_keeps_source_and_current_build_versions_without_frame_allocations()
    {
        var (cache, look, rig) = Inputs();
        var source = Assert.IsType<CharacterShapeInput>(look.Shape);
        var build = new BodyBuild(.3f, .2f, .1f);
        var original = cache.ShapeForLimbCuts(look, rig, 1);
        var built = cache.ShapeForLimbCuts(look, rig, 1, build);
        for (int warm = 0; warm < 20; warm++)
        {
            Assert.Same(original, cache.ShapeForLimbCuts(look, rig, 1));
            Assert.Same(built, cache.ShapeForLimbCuts(look, rig, 1, build));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 1000; frame++)
        {
            cache.ShapeForLimbCuts(look, rig, 1);
            cache.ShapeForLimbCuts(look, rig, 1, build);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var changedBuild = cache.ShapeForLimbCuts(look, rig, 1, build with { Muscle = .8f });
        Assert.NotSame(built, changedBuild);
        ((Dictionary<string, float>)source.Sliders)["Height"] = 130;
        var taller = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 1));
        Assert.NotSame(original, taller);
        Assert.InRange(taller.MovementScale, 1.299f, 1.301f);
        ((HashSet<Limb>)source.Missing).Remove(Limb.RightArm);
        var withRightArm = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 1));
        Assert.True(withRightArm.BoneSize[rig.Handle("Bip01 R Forearm")!.Value].Length() > 1);
        look.Shape = source with { Sliders = new Dictionary<string, float> { ["Height"] = 80 } };
        Assert.InRange(cache.ShapeForLimbCuts(look, rig, 1)!.MovementScale, .799f, .801f);
    }

    [Fact]
    public void Loadout_derived_shapes_restore_only_the_selected_arm_and_notice_a_changed_limb_state()
    {
        var (cache, _, rig) = Inputs();
        var loadout = new Loadout
        {
            Seed = 1, Race = cache.Database.Find("cut-shape-race")!, Female = false,
            Appearance = new() { Record = new() { RecordType = FcsRecordType.CHARACTER_APPEARANCE } },
        };
        loadout.Limbs[0] = new(1);
        loadout.Limbs[1] = new(1);
        var look = CharacterAppearance.Build(cache.Database, Path.GetTempPath(), "cut-shape-race", new() { Loadout = loadout, Naked = true });
        Assert.Null(look.Shape);
        var first = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 1));
        Assert.True(first.BoneSize[rig.Handle("Bip01 L Forearm")!.Value].Length() > 1);
        Assert.Equal(Vector3.Zero, first.BoneSize[rig.Handle("Bip01 R Forearm")!.Value]);
        loadout.Limbs[1] = null;
        var changed = Assert.IsType<BodyShapeData>(cache.ShapeForLimbCuts(look, rig, 1));
        Assert.NotSame(first, changed);
        Assert.True(changed.BoneSize[rig.Handle("Bip01 R Forearm")!.Value].Length() > 1);
        Assert.Null(look.Shape);
    }

    [Fact]
    public void A_partial_amputees_missing_hand_keeps_its_natural_local_offset_when_the_owner_becomes_unconscious()
    {
        var (cache, look, rig) = Inputs();
        look.Shape = new() { Missing = new HashSet<Limb> { Limb.LeftArm } };
        var setup = new CharacterPoseSetup { Rig = rig, Shape = Compute(rig, look.Shape) };
        int arm = rig.Handle("Bip01 L UpperArm")!.Value, forearm = rig.Handle("Bip01 L Forearm")!.Value, hand = rig.Handle("Bip01 L Hand")!.Value;
        var source = Definition([Box("Bip01", 0, rig.BindPosition[0], new(3, 5, 2)),
            Box("Bip01 L UpperArm", arm, rig.BindPosition[arm], new(1, 4, 1)),
            Box("Bip01 L Forearm", forearm, rig.BindPosition[forearm], new(1, 4, 1)),
            Box("Bip01 L Hand", hand, rig.BindPosition[hand], new(1, 4, 1))],
            [(0, 1, rig.BindPosition[arm]), (1, 2, rig.BindPosition[forearm] + Vector3.UnitY * 2), (2, 3, rig.BindPosition[hand] + Vector3.UnitY * 2)]);
        var map = Enumerable.Repeat(-1, rig.BoneCount).ToArray();
        map[0] = 0; map[arm] = 1; map[forearm] = 2; map[hand] = 3;
        source = new() { Bodies = source.Bodies, Joints = source.Joints, Joined = source.Joined, Root = 0, BodyOfBone = map };
        var cold = new CharacterCold { Race = Human, Medical = Healthy(), Appearance = look,
            LimbSeverances = [new(0, .7f, new(.2f, -.1f))] };
        cold.Medical.Sever(LArm, Context());
        using var physics = new RagdollSystem(cache, new Flat(), BodyOptions.Default with { Gore = true, AdvancedRagdolls = true }, _ => setup, (_, _, _) => source);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability((_, _) => 0), [physics]);
        var id = world.Characters.Spawn(new() { Position = new(20, 25, 20) }, cold, 0);
        world.RunTicks(2);
        cold.Medical.KnockOut(10000);
        world.RunTick();
        var snapshot = Assert.Single(world.Snapshot.Characters, character => character.Id == id);
        var pose = Assert.IsType<BonePose>(snapshot.Ragdoll);
        Assert.True(pose.AbsolutePositions);
        Assert.InRange(Vector3.Distance(pose.Position[forearm], pose.Position[hand]), 3.99f, 4.01f);
        Assert.Single(snapshot.DetachedRagdolls, piece => piece.Part == 0);
    }

    static (CharacterRigs Cache, CharacterAppearance Look, SkeletonRig Rig) Inputs()
    {
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "cut-shape-race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        var database = new GameDatabase();
        database.Apply(file, "cut-shape-tests.mod");
        var look = new CharacterAppearance { Race = database.Find("cut-shape-race")!, Shape = new()
        {
            Sliders = new Dictionary<string, float> { ["Height"] = 120, ["Hands"] = 85 }, Muscle = .2f, Starvation = .1f,
            Missing = new HashSet<Limb> { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, HideStump = HideStump.BothArms,
            NarrowLeftThigh = true, NarrowRightThigh = true, LegRatio = .8f,
        } };
        string[] names = ["Bip01", "Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head", "Bip01 Jaw",
            "Bip01 L Clavicle", "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand", "Bip01 L Finger0",
            "Bip01 R Clavicle", "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand", "Bip01 R Finger0",
            "Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot", "Bip01 L Toe0", "Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot", "Bip01 R Toe0",
            "L Boob", "R Boob", "Bip01 L Finger1", "Bip01 R Finger1"];
        var skeleton = new OgreSkeleton();
        for (ushort bone = 0; bone < names.Length; bone++)
        {
            bool lower = names[bone].Contains("Forearm", StringComparison.Ordinal) || names[bone].Contains("Hand", StringComparison.Ordinal);
            skeleton.Bones.Add(new() { Handle = bone, Name = names[bone], Parent = bone == 0 ? null : lower ? (ushort)(bone - 1) : (ushort)0,
                Position = bone == 0 ? new(0, 11, 0) : lower ? new(0, -4, 0) : names[bone].Contains("UpperArm", StringComparison.Ordinal) ? new(3, 3, 0) : Vector3.Zero });
        }
        return (new(database, _ => null, _ => null), look, new(skeleton));
    }

    static BodyShapeData Compute(SkeletonRig rig, CharacterShapeInput input)
    {
        var shape = CharacterShape.Compute(input, name => rig.Handle(name) is not null);
        return rig.Shape(shape.BoneSize, shape.PositionalSize, shape.MovementScale);
    }

    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }
}
