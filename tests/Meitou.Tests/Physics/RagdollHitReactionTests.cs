using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ogre;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using static Meitou.Tests.Physics.RagdollCutTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollHitReactionTests
{
    sealed class Flat : IRagdollEnvironment, IWalkability
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public float GroundHeight(float x, float z) => 0;
        public bool IsWalkable(float x, float z) => true;
        public PathResult FindPath(Vector3 from, Vector3 to) => new([from, to]);
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    sealed class Scene : IDisposable
    {
        public SimWorld World { get; }
        public RagdollSystem Physics { get; }
        public CombatSystem Combat { get; }
        public CharacterCold Cold { get; }
        public CharacterId Id { get; }
        public BodyOptions Options { get; }
        public CharacterPoseSetup Setup { get; }
        public Scene(bool advanced = true, bool combat = false, bool knockout = true, bool fatal = false, bool robot = false)
        {
            var skeleton = new OgreSkeleton();
            skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = new(0, 11, 0) });
            skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 Head", Position = new(0, 5, 0) });
            skeleton.Bones.Add(new() { Handle = 2, Parent = 0, Name = "Bip01 L UpperArm", Position = new(3, -1.5f, 0) });
            skeleton.Bones.Add(new() { Handle = 3, Parent = 2, Name = "Bip01 L Forearm", Position = new(0, -3, 0) });
            skeleton.Bones.Add(new() { Handle = 4, Parent = 3, Name = "Bip01 L Hand", Position = new(0, -2, 0) });
            Setup = new() { Rig = new(skeleton) };
            var definition = Definition(
            [
                Box("Bip01", 0, new(0, 11, 0), new(3, 5, 2)),
                Box("Bip01 Head", 1, new(0, 16, 0), new(2, 2, 2)),
                Box("Bip01 L UpperArm", 2, new(3, 9.5f, 0), new(1, 3, 1)),
                Box("Bip01 L Forearm", 3, new(3, 6.5f, 0), new(1, 3, 1)),
            ], [(0, 1, new(0, 14, 0)), (0, 2, new(2, 11, 0)), (2, 3, new(3, 8, 0))]);
            definition = new() { Bodies = definition.Bodies, Joints = definition.Joints, Root = 0, Joined = definition.Joined, BodyOfBone = [0, 1, 2, 3, -1] };
            var database = new GameDatabase();
            var file = new FcsFile();
            file.Records.Add(new() { StringId = "hit-race", Name = "Hit race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
            file.Records[0].Bools["is robot"] = robot;
            database.Apply(file, "hit-tests.mod");
            Options = BodyOptions.Default with { Gore = true, AdvancedRagdolls = advanced };
            Options.ForcedHitPart = fatal ? (Head.StringId, Head.Name) : (Chest.StringId, Chest.Name);
            Physics = new(new(database, _ => null, _ => null), new Flat(), Options, _ => Setup, (_, _, _) => definition);
            Combat = new([Cut("down", direction: 1)], Constants, BaseGame, Options,
                new AnimationLengths().Add("down", 0.9f), new CombatOptions { TickMedical = false, RecordLog = true });
            World = new(new WorldSettings { Seed = 31, TickSeconds = 1f / 30 }, new Flat(),
                combat ? [Combat, Senses(Combat), Physics] : [Physics]);
            Cold = new Combatant { Defence = 0, Toughness = 1 }.MakeCold();
            Cold.Name = "Unconscious victim";
            Cold.Faction = 1;
            Cold.Appearance = new() { Race = database.Find("hit-race")! };
            var race = new RaceData { IsRobot = robot, Anatomy = [.. Human.Anatomy.Select(e => e with { Part = e.Part with { BoneName = e.Part.Type switch { BodyPartType.Head => "Bip01 Head", BodyPartType.Arm => "Bip01 L Forearm", _ => "Bip01" } } })] };
            Cold.Race = race;
            Cold.Medical = Healthy(race);
            if (knockout) Cold.Medical.KnockOut(10000);
            Id = World.Characters.Spawn(new() { Position = new(1000, 15, 1000), Health = 100 }, Cold, 0);
        }
        public SavedRagdoll State => Assert.IsType<SavedRagdoll>(Physics.SaveState(Id));
        public void Settle() { World.RunTicks(245); Assert.True(State.Frozen); }
        public bool Hit(BodyPartTemplate? part = null, Vector3? push = null) => Physics.QueueHit(World, Id, part ?? Cold.Medical!.Parts[0].Template, push ?? new(40, 8, 0));
        public CharacterId Attack(bool fatal = false, bool allow = true, string? part = null, bool picked = true, float distance = 12)
        {
            var attacker = new Combatant { Attack = 100, WeaponSkill = fatal ? 100 : 10, Dexterity = fatal ? 100 : 10, Strength = fatal ? 100 : 10, Quality = fatal ? 1 : 0 }.MakeCold();
            attacker.IsPlayer = true;
            attacker.Faction = 0;
            var at = World.Characters.Previous[Id.Slot].Position;
            var id = World.Characters.Spawn(new() { Position = at - new Vector3(distance, 0, 0), Yaw = MathF.PI / 2, Health = 100 }, attacker, World.Tick);
            World.Commands.Enqueue(new AttackOrder([id], Id) { Picked = picked, AllowIncapacitated = allow, TargetBodyPart = part, Tick = World.Tick });
            return id;
        }
        public void ToHit()
        {
            for (int i = 0; i < 600 && Combat.Log.All(h => h.Outcome != BlowOutcome.Hit); i++) World.RunTick();
            Assert.Contains(Combat.Log, h => h.Outcome == BlowOutcome.Hit && h.Defender == Id.Slot);
        }
        public void Dispose() { World.Dispose(); Physics.Dispose(); }
    }

    [Fact]
    public void Hitting_a_frozen_unconscious_body_wakes_physics_from_its_current_pose_once()
    {
        using var scene = new Scene();
        scene.Settle();
        var before = scene.State;
        float ko = scene.Cold.Medical!.KoTimer;
        ulong hash = scene.World.StateHash();
        Assert.True(scene.Hit());
        Assert.NotEqual(hash, scene.World.StateHash());
        scene.World.RunTick();
        var after = scene.State;
        Assert.False(after.Frozen);
        Assert.InRange(after.Age, 0.02f, 0.04f);
        Assert.True(scene.Cold.Medical.Unconscious);
        Assert.False(scene.Cold.Medical.Dead);
        Assert.Equal(ko, scene.Cold.Medical.KoTimer);
        Assert.Null(scene.Cold.Blow);
        Assert.Null(scene.Cold.Animation);
        for (int i = 0; i < before.Pose.Length; i++)
            Assert.InRange(Vector3.Distance(before.Pose[i].Position, after.Pose[i].Position), 0, 3);
        scene.World.RunTicks(5);
        Assert.InRange(scene.State.Age, 0.19f, 0.21f);
        Assert.True(scene.State.Pose.Average(p => p.Position.X) > before.Pose.Average(p => p.Position.X) + 0.1f);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Later_hits_reset_settling_and_move_the_body_in_the_new_blow_direction(int direction)
    {
        using var scene = new Scene();
        scene.Settle();
        Assert.True(scene.Hit(push: new(0, 5, 20)));
        scene.World.RunTicks(20);
        var before = scene.State;
        Assert.True(scene.Hit(push: new(direction * 40, 8, 0)));
        scene.World.RunTicks(8);
        Assert.InRange(scene.State.Age, 0.26f, 0.28f);
        float delta = scene.State.Pose.Average(p => p.Position.X) - before.Pose.Average(p => p.Position.X);
        Assert.True(direction * delta > 0.1f, $"Expected {direction} direction, got {delta}");
        Assert.True(scene.Cold.Medical!.Unconscious);
    }

    [Fact]
    public void A_hit_on_an_unphysical_hand_uses_the_nearest_articulated_arm()
    {
        using var scene = new Scene();
        scene.World.RunTick();
        var before = scene.State;
        Assert.True(scene.Hit(LeftArm with { BoneName = "Bip01 L Hand" }, new(0, 0, 40)));
        scene.World.RunTick();
        var after = scene.State;
        float arm = after.Pose[3].Position.Z - before.Pose[3].Position.Z;
        float torso = after.Pose[0].Position.Z - before.Pose[0].Position.Z;
        Assert.True(arm > 0.1f);
        Assert.True(arm > torso, $"Hand strike should first move forearm: arm={arm}, torso={torso}");
    }

    [Fact]
    public void Queued_hits_cannot_reach_a_new_character_reusing_the_old_slot()
    {
        using var scene = new Scene();
        scene.Settle();
        Assert.True(scene.Hit());
        scene.World.Characters.Remove(scene.Id);
        var replacement = new CharacterCold { Name = "Replacement", Appearance = scene.Cold.Appearance, Medical = Healthy(), Race = Human };
        replacement.Medical.KnockOut(10000);
        var id = scene.World.Characters.Spawn(new() { Position = new(100, 20, 100), Health = 100 }, replacement, scene.World.Tick);
        Assert.Equal(scene.Id.Slot, id.Slot);
        Assert.NotEqual(scene.Id, id);
        scene.World.RunTick();
        var pose = Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(id));
        Assert.InRange(pose.Pose[0].Position.X, 99.99f, 100.01f);
    }

    [Fact]
    public void Advanced_reactions_do_not_change_awake_or_legacy_bodies()
    {
        using var awake = new Scene(knockout: false);
        awake.World.RunTick();
        Assert.False(awake.Hit());
        Assert.Null(awake.Physics.SaveState(awake.Id));
        using var legacy = new Scene(advanced: false);
        legacy.Settle();
        var before = legacy.State;
        Assert.False(legacy.Hit());
        legacy.World.RunTicks(5);
        Assert.True(legacy.State.Frozen);
        Assert.Equal(before.Pose, legacy.State.Pose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_landed_ground_strikes_reactivate_a_settled_body_without_a_second_launch(bool fatal)
    {
        using var scene = new Scene(combat: true, fatal: fatal);
        scene.Settle();
        var before = scene.State;
        if (fatal) scene.Cold.Medical!.Parts[HeadIx].Flesh = -90;
        scene.Attack(fatal);
        scene.ToHit();
        Assert.False(scene.State.Frozen);
        Assert.InRange(scene.State.Age, 0.02f, 0.04f);
        Assert.True(scene.Cold.Medical!.Incapacitated);
        Assert.Null(scene.Cold.Blow);
        if (fatal)
        {
            Assert.True(scene.Cold.Medical.Dead);
            if (scene.Cold.Medical.Decapitated)
                Assert.Contains(scene.Physics.PublishDetached(scene.Id), p => p.Part == -1);
        }
        else Assert.False(scene.Cold.Medical.Dead);
        scene.World.RunTicks(8);
        // Ground tessellation changes contact friction; require forward recoil rather than a fixed sliding distance.
        Assert.True(scene.State.Pose.Average(p => p.Position.X) > before.Pose.Average(p => p.Position.X) + 0.01f);
    }

    [Fact]
    public void An_automatic_order_leaves_a_frozen_unconscious_body_at_rest()
    {
        using var scene = new Scene(combat: true);
        scene.Settle();
        var before = scene.State;
        scene.Attack(allow: false);
        scene.World.RunTicks(120);
        Assert.Empty(scene.Combat.Log);
        Assert.True(scene.State.Frozen);
        Assert.Equal(before.Pose, scene.State.Pose);
    }

    [Theory]
    [InlineData("32-t")]
    [InlineData("101-t")]
    [InlineData("100-t")]
    [InlineData("28-t")]
    [InlineData("29-t")]
    [InlineData("30-t")]
    [InlineData("31-t")]
    public void An_explicit_part_selection_applies_real_damage_only_to_that_part_even_with_a_different_debug_override(string id)
    {
        using var scene = new Scene(combat: true);
        scene.Settle();
        scene.Options.ForcedHitPart = id == Head.StringId ? (Chest.StringId, Chest.Name) : (Head.StringId, Head.Name);
        var before = scene.Cold.Medical!.Parts.Select(p => p.Flesh).ToArray();
        var attacker = scene.Attack(part: id, distance: 35);
        scene.ToHit();
        int chosen = scene.Cold.Medical.Parts.ToList().FindIndex(p => p.Template.StringId == id);
        Assert.Equal(chosen, Assert.Single(scene.Combat.Log, h => h.Outcome == BlowOutcome.Hit).Part);
        Assert.Equal(id, scene.Combat.StateOf(attacker).TargetBodyPart);
        for (int i = 0; i < before.Length; i++)
            if (i == chosen) Assert.True(scene.Cold.Medical.Parts[i].Flesh < before[i]);
            else Assert.Equal(before[i], scene.Cold.Medical.Parts[i].Flesh);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Aimed_orders_refuse_missing_parts_awake_or_invalid_targets_and_nonexplicit_permissions(int invalid)
    {
        using var scene = new Scene(combat: true, knockout: invalid != 2);
        scene.World.RunTick();
        string part = invalid == 0 ? "unknown-part" : invalid == 1 ? LeftArm.StringId : Head.StringId;
        if (invalid == 1) scene.Cold.Medical!.Sever(LArm, Context(scene.Cold.Race));
        if (invalid == 3) scene.Cold.Medical!.KillFromBisection();
        if (invalid == 4) scene.Cold.Faction = 0;
        if (invalid == 5) scene.Cold.CarriedBy = new(222, 1);
        if (invalid == 6) scene.Cold.InSomething = global::Meitou.Simulation.Locks.HeldState.Imprisoned;
        var attacker = scene.Attack(part: part, allow: invalid != 7, picked: invalid != 8);
        scene.World.RunTicks(160);
        Assert.Empty(scene.Combat.Log);
        Assert.Null(scene.Combat.StateOf(attacker).TargetBodyPart);
    }

    [Fact]
    public void A_selected_part_order_stops_if_the_enemy_wakes_during_the_windup()
    {
        using var scene = new Scene(combat: true, knockout: false);
        scene.Cold.InSomething = global::Meitou.Simulation.Locks.HeldState.Bed;
        scene.World.Characters.Place(scene.Id.Slot, new(20, 0, 20), 0);
        scene.World.RunTick();
        var attacker = scene.Attack(part: Head.StringId);
        for (int i = 0; i < 180 && scene.Combat.StateOf(attacker).AttackTech < 0; i++) scene.World.RunTick();
        Assert.True(scene.Combat.StateOf(attacker).AttackTech >= 0);
        scene.Cold.InSomething = global::Meitou.Simulation.Locks.HeldState.None;
        Assert.False(scene.Cold.Medical!.Incapacitated);
        scene.World.RunTicks(120);
        Assert.Empty(scene.Combat.Log);
        Assert.Null(scene.Combat.StateOf(attacker).TargetBodyPart);
    }

    [Fact]
    public void A_selected_head_tracks_its_actual_fallen_pose_and_changes_with_the_physics()
    {
        using var scene = new Scene(combat: true);
        scene.Settle();
        var head = scene.Cold.Medical!.Parts[HeadIx].Template;
        Assert.True(scene.Physics.TryPartPosition(scene.Id, head, out var before));
        var attacker = scene.Attack(part: Head.StringId, distance: 70);
        scene.World.RunTick();
        var state = scene.Combat.StateOf(attacker);
        Assert.InRange(Vector3.Distance(before, scene.Combat.TargetPoint(scene.World, state, new(-999))), 0, 0.0001f);
        Assert.True(scene.Hit(head, new(0, 5, 40)));
        scene.World.RunTicks(8);
        Assert.True(scene.Physics.TryPartPosition(scene.Id, head, out var moved));
        Assert.True(Vector3.Distance(before, moved) > 0.1f);
        state = scene.Combat.StateOf(attacker);
        Assert.InRange(Vector3.Distance(moved, scene.Combat.TargetPoint(scene.World, state, new(-999))), 0, 0.0001f);
        scene.ToHit();
        Assert.Equal(HeadIx, Assert.Single(scene.Combat.Log, h => h.Outcome == BlowOutcome.Hit).Part);
    }

    [Fact]
    public void A_head_severed_by_the_hit_inherits_that_one_impulse_from_the_old_body()
    {
        using var scene = new Scene();
        scene.Settle();
        var before = scene.State.Pose[1].Position;
        scene.Cold.Medical!.Decapitate();
        Assert.True(scene.Hit(scene.Cold.Medical.Parts[HeadIx].Template, new(30, 0, 0)));
        scene.World.RunTick();
        var head = Assert.Single(scene.Physics.PublishDetached(scene.Id), p => p.Part == -1);
        var shown = Assert.Single(scene.World.Snapshot.Characters, c => c.Id == scene.Id);
        var placement = CharacterRigs.Placement(scene.Setup, shown.Position, shown.Yaw, 1);
        var after = Vector3.Transform(head.Bones.Position[1], placement);
        Assert.InRange(after.X - before.X, 0.85f, 1.15f); // 30 units/s for one 1/30 second step, once.
        Assert.Null(scene.Cold.Blow);
    }

    [Fact]
    public void The_selected_body_part_survives_native_order_write_read_restore_and_still_hits_that_part()
    {
        using var original = new Scene(combat: true);
        original.Settle();
        var attacker = original.Attack(part: LeftArm.StringId, distance: 70);
        original.World.RunTick();
        var ai = new FcsRecord { StringId = "aimed-ai", RecordType = FcsRecordType.GAMESTATE_AI };
        var hand = new Hand((int)FcsRecordType.CHARACTER, 44, 1, 2, 3);
        OrderSave.Write(ai, original.World.Characters.Cold(attacker.Slot)!, original.World, new Dictionary<string, FcsRecord>(), (record, key, _) => hand.Write(record, key));
        Assert.Equal(LeftArm.StringId, ai.Strings["meitou tjobD0"]);
        Assert.Equal(1, ai.Ints["meitou tjobK0"]);
        var file = new SaveFile();
        file.Records.Add(ai);
        using var bytes = new MemoryStream();
        file.Write(bytes);
        var persisted = Assert.Single(SaveFile.Read(bytes.ToArray()).Records);

        using var loaded = new Scene(combat: true);
        loaded.Settle();
        var restoredAttacker = loaded.Attack(distance: 70);
        loaded.World.Commands.TakeDue(long.MaxValue); // Spawn only; the saved queue supplies the actual order.
        var targetSource = new SaveCharacter(new SavePlatoon(new FcsRecord { StringId = "aim-platoon", RecordType = FcsRecordType.PLATOON }, null), new FcsInstance())
        {
            GameState = new FcsRecord { StringId = "aim-target", RecordType = FcsRecordType.GAMESTATE_CHARACTER },
        };
        targetSource.Handle = hand;
        loaded.Cold.Save = new() { Source = targetSource, PlatoonName = "aim-platoon", Slot = 2 };
        var quick = new SaveFile();
        quick.Records.Add(new() { StringId = "aim-camera", RecordType = FcsRecordType.CAMERA });
        var saved = new SaveGame(quick);
        var cold = loaded.World.Characters.Cold(restoredAttacker.Slot)!;
        OrderSave.Read(persisted, cold, saved.BuildHandles(), new Dictionary<FcsRecord, string>());
        Assert.Equal(LeftArm.StringId, Assert.Single(cold.SavedOrders!).DetailText);
        OrderSave.Restore(loaded.World, restoredAttacker.Slot, cold);
        var order = Assert.Single(cold.OrderQueue);
        Assert.Equal(loaded.Id, order.Target);
        Assert.Equal(LeftArm.StringId, order.Owner.SaveText(order));
        order.Owner.Start(loaded.World, restoredAttacker.Slot, order);
        loaded.ToHit();
        Assert.Equal(LArm, Assert.Single(loaded.Combat.Log, h => h.Outcome == BlowOutcome.Hit).Part);
        Assert.Equal(LeftArm.StringId, loaded.Combat.StateOf(restoredAttacker).TargetBodyPart);
    }
}