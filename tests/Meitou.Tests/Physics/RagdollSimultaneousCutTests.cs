using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Physics.RagdollCutTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public class RagdollSimultaneousCutTests
{
    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 500;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    [Fact]
    [Slow]
    public void Twenty_installed_samurai_with_varied_oblique_cuts_remain_finite_and_settle_together()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var database = InstallData.FullLoadOrder!;
        var samurai = database.OfType(FcsRecordType.CHARACTER).FirstOrDefault(record => record.Name == "Samurai");
        Assert.SkipWhen(samurai is null, "Installed Samurai character not found");
        var assets = new AssetLocator(install);
        var rigs = new CharacterRigs(database,
            path => assets.Configured.Find(Path.GetFileName(path.Replace('\\', '/'))) ?? assets.Find(path), path => assets.Find(path));
        var appearances = Enumerable.Range(0, 20).Select(index =>
        {
            ulong key = Rng.Mix(SeedSalts.PlayerStart ^ Rng.StableHash(samurai.StringId) ^ (index == 0 ? 2UL : 1UL) ^ ((ulong)index << 8));
            int seed = (int)(Rng.Hash(0, key, RngPurpose.Spawn, SeedSalts.PlayerLook) & 0x7FFFFFFF);
            return CharacterAppearance.Build(database, install.Root, samurai.StringId,
                new CharacterOptions { Seed = seed, Naked = true, Faction = index == 0 ? "United Cities" : "Nameless" });
        }).ToArray();
        Assert.All(appearances, appearance => Assert.NotNull(rigs.For(appearance)));

        CheckPile(Vector2.Zero, appearances, rigs, database);
    }

    [Fact]
    public void A_broad_thin_plate_keeps_collision_but_a_microscopic_corner_is_omitted_without_mutating_the_source()
    {
        var body = Box("Bip01 Spine1", 0, Vector3.Zero, new(4, 4, 4));
        var source = Definition([body]);
        var broadSlice = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -1.99f), 1, fullBody: true));
        Assert.Single(broadSlice.Bodies);

        var normal = Vector3.Normalize(Vector3.One);
        var plane = new Vector4(normal, -Vector3.Dot(normal, new(2, 2, 2)) + 0.01f);
        Assert.Null(RagdollCut.Build(source, plane, 1, fullBody: true));
        var substantial = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, -1, fullBody: true));
        Assert.Single(substantial.Bodies);
        Assert.Same(body, Assert.Single(source.Bodies));
        Assert.Null(body.Hull);
        Assert.Equal(new Vector3(4, 4, 4), body.Size);
    }

    static void CheckPile(Vector2 origin, CharacterAppearance[] appearances, CharacterRigs rigs, Meitou.Data.GameDatabase database)
    {
        var setups = appearances.ToDictionary(appearance => appearance, appearance =>
        {
            var setup = rigs.For(appearance)!;
            var idle = setup.Rig.Layer(database, setup.Idle, setup.Animal);
            return new CharacterPoseSetup { Rig = setup.Rig, Shape = setup.Shape, GroundOffset = setup.GroundOffset,
                Idle = setup.Idle, Animal = setup.Animal, Fixed = idle is null ? setup.Fixed : [.. setup.Fixed, (idle, 0.5f)] };
        });
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true },
            appearance => setups[appearance], null!);
        using var world = new SimWorld(new WorldSettings { Seed = 0, Threads = 4, TickSeconds = 1f / 30 },
            new OpenGroundWalkability((_, _) => 500), [physics]);
        var ids = new List<CharacterId>();
        for (int i = 0; i < appearances.Length; i++)
        {
            var appearance = appearances[i];
            var race = RaceData.From(appearance.Race, database);
            var position = new Vector3(origin.X + (i % 6 - 2.5f) * 2, 500, origin.Y + (i / 6 - 2.5f) * 2);
            // Packing the native stress scene teleports the bystanders but preserves their ring-facing yaw.
            int ring = i <= 16 ? 1 : 2, placed = i <= 16 ? i - 1 : i - 17;
            int perRing = (int)(2 * MathF.PI * (25 + 15 * ring) / 15);
            float yaw = i == 0 ? -MathF.PI / 2 : 2 * MathF.PI * placed / perRing + MathF.PI;
            var attacker = new Vector3(origin.X - 15, 500, origin.Y);
            var travel = new Vector2(position.X - attacker.X, position.Z - attacker.Z);
            int direction = HitOutcomes.Facing(2, HitOutcomes.Behind(attacker, position, yaw));
            var cut = TorsoBisectionPolicy.StateForDirection(direction, 3, travel, yaw, TorsoBisectionStyle.Oblique);
            var push = Vector3.Normalize(new(travel.X, 0, travel.Y)) * 50 + Vector3.UnitY * 10;
            var cold = new CharacterCold
            {
                Name = $"Samurai {i}", Appearance = appearance, Race = race, Medical = Healthy(race),
                TorsoBisection = cut, Blow = (push, push), Knockdown = push,
            };
            cold.Medical.KillFromBisection();
            ids.Add(world.Characters.Spawn(new() { Position = position, Yaw = yaw }, cold, 0));
        }

        for (int tick = 0; tick < 420; tick++)
        {
            world.RunTick();
            foreach (var shown in world.Snapshot.Characters)
            {
                Assert.True(Finite(shown.Position), $"origin {origin}, tick {tick}, {shown.Id}: owner position");
                var halves = Assert.IsType<TorsoRagdollPose>(shown.TorsoRagdoll);
                AssertPose(halves.Positive, origin, tick, shown.Id);
                AssertPose(halves.Negative, origin, tick, shown.Id);
            }
        }
        Assert.Equal(40, physics.Count);
        Assert.Equal(0, physics.Moving);
        foreach (var id in ids)
        {
            var state = Assert.IsType<Meitou.Simulation.Saving.SavedRagdoll>(physics.SaveState(id));
            Assert.NotNull(state.Torso);
            Assert.True(state.Torso.Positive.Frozen);
            Assert.True(state.Torso.Negative.Frozen);
        }
    }

    static void AssertPose(BonePose pose, Vector2 origin, int tick, CharacterId id)
    {
        Assert.True(pose.AbsolutePositions);
        for (int bone = 0; bone < pose.Set.Length; bone++)
        {
            if (!pose.Set[bone]) continue;
            Assert.True(Finite(pose.Position[bone]) && pose.Position[bone].LengthSquared() < 1000 * 1000,
                $"origin {origin}, tick {tick}, {id}, bone {bone}: fragment-relative position {pose.Position[bone]}");
            Assert.InRange(pose.Rotation[bone].LengthSquared(), 0.99f, 1.01f);
        }
    }

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
