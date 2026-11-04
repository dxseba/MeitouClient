using System.Numerics;
using System.Text.Json;
using Meitou.Brutality;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;
using Environment = Meitou.Simulation.Ragdolls.IRagdollEnvironment;

namespace Meitou.Tests.Modding;

public sealed class BrutalityFragmentSaveTests
{
    sealed class Flat : Environment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }
    static RagdollDefinition Definition(float width = 2) => new()
    {
        Root = 0, BodyOfBone = [0, 1], Joined = [2, 1],
        Bodies = [new() { Name = "Bip01 Spine1", Bone = 0, Type = PhysicsShapeType.Box, Size = new(width, 2, 2), Mass = 10, Bind = new(new(0, 8, 0), Quaternion.Identity), BoneInBody = RigidTransform.Identity },
            new() { Name = "Bip01 Head", Bone = 1, Type = PhysicsShapeType.Box, Size = new(1, 1, 1), Mass = 3, Bind = new(new(0, 10, 0), Quaternion.Identity), BoneInBody = RigidTransform.Identity }],
        Joints = [new() { A = 0, B = 1, Type = PhysicsJointType.Spherical, OffsetA = Vector3.UnitY, OffsetB = -Vector3.UnitY, BasisA = Quaternion.Identity, BasisB = Quaternion.Identity }],
    };
    static AdvancedFragmentScene.Piece Make(AdvancedFragmentScene scene, RagdollDefinition definition, Vector3? offset = null) =>
        scene.Create(definition, bone => definition.Bodies[definition.BodyOfBone[bone]].Bind with
            { Position = definition.Bodies[definition.BodyOfBone[bone]].Bind.Position + (offset ?? Vector3.Zero) }, new(2, 3, 0), Vector3.UnitY, 7);
    const string Key = "hand:v1:25:90:12:4:200", Cut = "0123456789abcdef";
    static SavedFragmentOwner Owner(SavedFragmentPiece piece) => new(Key, "body.mesh", Cut, Vector3.Zero, [piece with { Stamp = Cut + ":" + piece.Key }]);
    static byte[] Bytes(params SavedFragmentOwner[] owners)
    {
        using var stream = new MemoryStream(); FragmentSaveCodec.Write(stream, owners, []); return stream.ToArray();
    }
    static FragmentSavePayload Decode(byte[] bytes) => FragmentSaveCodec.Read(new MemoryStream(bytes, writable: false));
    static void EqualPiece(SavedFragmentPiece expected, SavedFragmentPiece actual)
    {
        Assert.Equal(expected.Key, actual.Key); Assert.Equal(expected.Stamp, actual.Stamp); Assert.Equal(expected.Definition, actual.Definition);
        Assert.Equal(expected.Bones, actual.Bones); Assert.Equal(expected.Pose, actual.Pose); Assert.Equal(expected.Linear, actual.Linear);
        Assert.Equal(expected.Angular, actual.Angular); Assert.Equal(expected.Dynamic, actual.Dynamic); Assert.Equal(expected.Frozen, actual.Frozen);
        Assert.Equal(expected.Carried, actual.Carried); Assert.Equal(expected.Age, actual.Age); Assert.Equal(expected.Anchors, actual.Anchors); Assert.Equal(expected.Offsets, actual.Offsets);
    }
    static (BrutalityGameplaySystem Gameplay, FragmentPhysicsSystem Fragments, SimWorld World) Empty()
    {
        var gameplay = new BrutalityGameplaySystem(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        var data = SyntheticTown.Data(SyntheticTown.Database());
        var fragments = new FragmentPhysicsSystem(gameplay, new CharacterRigs(data.Db, _ => null, _ => null));
        var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [gameplay, fragments]);
        gameplay.BindWorld(world); fragments.BindSaveWorld(world);
        return (gameplay, fragments, world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fragment_pose_velocity_age_and_sleep_state_restore_exactly(bool frozen)
    {
        using var source = new AdvancedFragmentScene(); var definition = Definition(); var original = Make(source, definition);
        source.Step(1f / 30, new Flat(), 0);
        if (frozen) source.Freeze(original);
        var saved = source.Capture(original, 0, "test");
        using var restored = new AdvancedFragmentScene(); var target = Make(restored, definition, new(100, 0, 20));
        Assert.True(restored.Restore(target, saved));
        EqualPiece(saved, restored.Capture(target, 0, "test"));
        Assert.Equal(frozen ? 0 : 2, restored.AwakeBodies);
    }

    [Fact]
    public void Per_bone_anchors_and_carried_dynamic_masks_survive_the_physics_sidecar()
    {
        using var source = new AdvancedFragmentScene(); var definition = Definition(); var piece = Make(source, definition);
        source.SetCarried(piece, piece.Pose.ToArray(), [true, true], [true, false], entering: true);
        piece.Anchors = [0, 1]; piece.Offsets = [new(new(.25f, 0, 0), Quaternion.Identity), new(new(0, .5f, 0), Quaternion.Identity)];
        var saved = source.Capture(piece, 0, "test");
        var decoded = Assert.Single(Decode(Bytes(Owner(saved))).Owners).Pieces.Single();
        using var targetScene = new AdvancedFragmentScene(); var target = Make(targetScene, definition);
        Assert.True(targetScene.Restore(target, decoded));
        EqualPiece(decoded, targetScene.Capture(target, 0, decoded.Stamp));
        Assert.True(target.Carried); Assert.Equal(new[] { false, true }, target.Dynamic);
        targetScene.ReleaseCarried(target);
        Assert.False(target.Carried); Assert.All(target.Dynamic, dynamic => Assert.True(dynamic));
    }

    [Theory]
    [InlineData("position")]
    [InlineData("rotation")]
    [InlineData("velocity")]
    [InlineData("anchors")]
    [InlineData("offsets")]
    [InlineData("topology")]
    public void Corrupt_or_mismatched_fragment_data_does_not_change_the_current_piece(string corruption)
    {
        using var scene = new AdvancedFragmentScene(); var definition = Definition(); var piece = Make(scene, definition);
        var saved = scene.Capture(piece, 0, "test"); var before = piece.Pose.ToArray();
        switch (corruption)
        {
            case "position": var positions = saved.Pose.ToArray(); positions[0] = positions[0] with { Position = new(float.NaN, 0, 0) }; saved = saved with { Pose = positions }; break;
            case "rotation": var rotations = saved.Pose.ToArray(); rotations[0] = rotations[0] with { Rotation = default }; saved = saved with { Pose = rotations }; break;
            case "velocity": saved = saved with { Linear = [new(float.PositiveInfinity, 0, 0), Vector3.Zero] }; break;
            case "anchors": saved = saved with { Anchors = [0, 2], Offsets = [RigidTransform.Identity, RigidTransform.Identity] }; break;
            case "offsets": saved = saved with { Anchors = [0, 1], Offsets = [new(Vector3.Zero, default), RigidTransform.Identity] }; break;
            case "topology": saved = saved with { Definition = "ffffffffffffffff" }; break;
        }
        Assert.False(scene.Restore(piece, saved)); Assert.Equal(before, piece.Pose);
        var altered = Make(scene, Definition(width: 4));
        Assert.False(scene.Restore(altered, scene.Capture(piece, 0, "test")));
    }

    [Fact]
    public void Pending_actor_physics_is_written_again_before_any_model_or_NPC_materializes()
    {
        using var scene = new AdvancedFragmentScene(); var definition = Definition();
        var saved = Owner(scene.Capture(Make(scene, definition), 1, "test"));
        var state = Empty();
        using (state.World)
        using (state.Fragments)
        {
            ((IModSaveData)state.Fragments).Read(new MemoryStream(Bytes(saved)));
            using var written = new MemoryStream(); ((IModSaveData)state.Fragments).Write(written);
            var row = Assert.Single(Decode(written.ToArray()).Owners);
            Assert.Equal(saved.Key, row.Key); Assert.Equal(saved.Anchor, row.Anchor); EqualPiece(saved.Pieces[0], Assert.Single(row.Pieces));
            Assert.Equal(0, state.World.Characters.Count);
        }
    }

    [Fact]
    public void Native_rekey_preserves_a_pending_unmaterialized_fragment_without_slot_or_name_matching()
    {
        using var scene = new AdvancedFragmentScene(); var definition = Definition();
        var saved = Owner(scene.Capture(Make(scene, definition), 0, "test"));
        var builder = new SaveBuilder(); var platoon = builder.AddPlatoon("saved", "Testfolk", "20-t", "10-t", Vector3.Zero);
        var source = builder.AddCharacter(platoon, "2-t", "same name", "20-t", Vector3.Zero, Quaternion.Identity);
        source.Handle = new(25, 90, 12, 4, 200);
        var cold = new CharacterCold { Name = "same name", Save = new() { Source = source, PlatoonName = platoon.Name, Slot = source.Handle.I } };
        var state = Empty();
        using (state.World)
        using (state.Fragments)
        {
            ((IModSaveData)state.Fragments).Read(new MemoryStream(Bytes(saved)));
            var id = state.World.Characters.Spawn(new(), cold, 0);
            var replacement = source.Handle with { C = 91, S = 201 };
            state.Gameplay.CaptureSaveIdentity(state.World, cold, id, replacement);
            state.Fragments.CapturedSaveIdentity(cold);
            using var written = new MemoryStream(); ((IModSaveData)state.Fragments).Write(written);
            var row = Assert.Single(Decode(written.ToArray()).Owners);
            Assert.Equal(SaveIdentityAdapter.KeyFor(replacement), row.Key); Assert.NotEqual(Key, row.Key);
            EqualPiece(saved.Pieces[0], row.Pieces[0]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_composite_torso_poses_survive_a_save_before_their_NPC_is_loaded(bool withAnchors)
    {
        var builder = new SaveBuilder(); var platoon = builder.AddPlatoon("npc", "Testfolk", "20-t", "10-t", Vector3.Zero);
        var source = builder.AddCharacter(platoon, "2-t", "same name", "20-t", Vector3.Zero, Quaternion.Identity);
        source.GameState!.Strings["meitou ragdoll"] = "T2 0 1 0 -12 0 | 1 7 1 1 12 3 0 0 0 1 | 1 7 1 1 2 3 0 0 0 1";
        if (withAnchors) source.GameState.Strings["meitou torso anchors"] = "A1 | 2 0 .1 .2 .3 0 0 0 2 0 .4 .5 .6 0 0 0 1 | 2 0 .7 .8 .9 0 0 0 1 0 1 2 3 0 0 0 1";
        builder.Game.UpdateDerived();
        var original = Empty(); byte[] bytes;
        using (original.World)
        using (original.Fragments)
        {
            original.Fragments.ImportLegacySave(original.World, builder.Game);
            using var stream = new MemoryStream(); ((IModSaveData)original.Fragments).Write(stream); bytes = stream.ToArray();
        }
        var pending = Assert.Single(Decode(bytes).Legacy);
        Assert.Equal(new Vector3(1, 12, 3), Assert.Single(pending.Positive.Pose).Position);
        Assert.Equal(new Vector3(1, 2, 3), Assert.Single(pending.Negative.Pose).Position);
        if (withAnchors)
        {
            Assert.Equal(new[] { 0, 0 }, pending.Positive.Anchors);
            Assert.Equal(new Vector3(.1f, .2f, .3f), pending.Positive.Offsets![0].Position);
            Assert.Equal(Quaternion.Identity, pending.Positive.Offsets[0].Rotation);
            Assert.Equal(new Vector3(.7f, .8f, .9f), pending.Negative.Offsets![0].Position);
        }
        var reloaded = Empty();
        using (reloaded.World)
        using (reloaded.Fragments)
        {
            ((IModSaveData)reloaded.Fragments).Read(new MemoryStream(bytes));
            using var stream = new MemoryStream(); ((IModSaveData)reloaded.Fragments).Write(stream);
            Assert.Single(Decode(stream.ToArray()).Legacy);
        }
    }

    [Fact]
    public void Unknown_fragment_save_versions_are_rejected_and_the_original_sidecar_is_preserved()
    {
        var state = Empty();
        using (state.World)
        using (state.Fragments)
        {
            using var unsupported = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { Version = 99, Owners = Array.Empty<object>(), Legacy = Array.Empty<object>() }));
            Assert.Throws<InvalidDataException>(() => ((IModSaveData)state.Fragments).Read(unsupported));
            Assert.Throws<InvalidDataException>(() => ((IModSaveData)state.Fragments).Write(new MemoryStream()));
        }
    }
}
