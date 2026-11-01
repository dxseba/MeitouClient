using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests(ITestOutputHelper output)
{
    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    readonly record struct EdgePoint(Vector3 Bind, Vector3 Before, Vector3 After);

    [Theory]
    [InlineData(TorsoBisectionStyle.Oblique, 2)]
    [InlineData(TorsoBisectionStyle.Vertical, 1)]
    [Slow]
    public void Installed_cut_skin_does_not_stretch_into_straps_between_free_components(TorsoBisectionStyle style, int direction)
    {
        var (rig, definition, model, setup, rigs, look) = Installed();
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        const float yaw = -MathF.PI / 2;
        var cut = TorsoBisectionPolicy.StateForDirection(direction, 1, Vector2.UnitX, yaw, style);
        var push = new Vector3(50, 10, 0);
        var cold = new CharacterCold
        {
            Name = "Skin test corpse", Race = Human, Medical = Healthy(),
            Appearance = look, TorsoBisection = cut,
            Blow = (push, push), Knockdown = push,
        };
        cold.Medical.KillFromBisection();
        var id = world.Characters.Spawn(new() { Position = new(20, 0, 20) }, cold, 0);
        CheckCutSkin(rig, model, setup, world, id, cut, style, [1, 30, 90, 150, 240]);
    }

    [Fact]
    [Slow]
    public void Installed_worker_vertical_cut_skin_and_caps_do_not_form_long_spikes_after_settling()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var database = InstallData.FullLoadOrder!;
        const string worker = "42194-rebirth.mod";
        Assert.SkipWhen(database.Find(worker) is null, "Installed Worker Drone character not found");
        var look = CharacterAppearance.Build(database, install.Root, worker, new() { Female = false, Naked = true });
        var assets = new AssetLocator(install);
        var rigs = new CharacterRigs(database, path => assets.Configured.Find(Path.GetFileName(path.Replace('\\', '/'))) ?? assets.Find(path), path => assets.Find(path));
        var originalSetup = rigs.For(look);
        Assert.SkipWhen(originalSetup is null, "Installed Worker Drone skeleton not found");
        var rig = originalSetup.Rig;
        var idle = rig.Layer(database, originalSetup.Idle);
        Assert.NotNull(idle);
        // Hold the real linked idle over the Worker's body-file shape and posture sliders at the instant of the cut.
        var setup = new CharacterPoseSetup
        {
            Rig = rig, Shape = originalSetup.Shape, GroundOffset = originalSetup.GroundOffset, Idle = originalSetup.Idle,
            Fixed = [.. originalSetup.Fixed, (idle, 0.5f)],
        };
        var bodyPath = rigs.FindMesh(look.BodyMesh);
        Assert.NotNull(bodyPath);
        var model = Model.Build(OgreMeshReader.ReadFile(bodyPath), rig.BoneCount, gameBinormals: true);
        var ragdollPath = rigs.Find(look.Race.GetPath("male ragdoll"));
        Assert.NotNull(ragdollPath);
        Vector3[]? Hull(string name) => assets.Find(name) is { } path ? CookedMeshReader.ReadConvex(File.ReadAllBytes(path)).Vertices : null;
        var definition = Assert.IsType<RagdollDefinition>(RagdollDefinition.Build(PhysicsFile.ReadFile(ragdollPath), rig, 1, Hull));
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 1 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        const float yaw = -MathF.PI / 2;
        var cut = TorsoBisectionPolicy.StateForDirection(1, 1, -Vector2.UnitY, yaw, TorsoBisectionStyle.Vertical);
        var push = new Vector3(0, 10, -50);
        var race = RaceData.From(look.Race, database);
        var cold = new CharacterCold
        {
            Name = "Worker skin test corpse", RecordId = worker, Race = race, Medical = Healthy(race),
            Appearance = look, TorsoBisection = cut, Blow = (push, push), Knockdown = push,
        };
        cold.Medical.KillFromBisection();
        var id = world.Characters.Spawn(new() { Position = new(20, 0, 20), Yaw = yaw }, cold, 0);
        output.WriteLine($"Worker body {look.BodyMesh}, idle {idle.Label}, shaped {setup.Shape is not null}");
        CheckCutSkin(rig, model, setup, world, id, cut, TorsoBisectionStyle.Vertical, [1, 30, 90, 150, 240, 320]);
        var settled = Assert.IsType<TorsoRagdollPose>(Assert.Single(world.Snapshot.Characters, character => character.Id == id).TorsoRagdoll);
        Assert.NotNull(settled.Positive.Components);
        int trunk = ComponentOf(rig, settled.Positive.Components, "Bip01 Pelvis");
        foreach (string bone in new[] { "Bip01 L Thigh", "Bip01 L Calf", "Bip01 R Thigh", "Bip01 R Calf" })
            Assert.True(ComponentOf(rig, settled.Positive.Components, bone) == trunk, $"The Worker's retained front skin must keep {bone} joined to the trunk");
    }

    [Fact]
    [Slow]
    public void An_installed_amputees_retained_calves_and_feet_follow_their_physics_positions_above_the_floor()
    {
        var (rig, definition, model, setup, rigs, look) = Installed();
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        var cold = new CharacterCold
        {
            Name = "Amputee skin test", Race = Human, Medical = Healthy(),
            Appearance = look,
            Blow = (new Vector3(50, 10, 0), new Vector3(50, 10, 0)),
        };
        cold.Medical.KillFromBisection();
        cold.Medical.Sever(LArm, Context(), false);
        cold.Medical.Sever(LArm + 1, Context(), false);
        var id = world.Characters.Spawn(new() { Position = new(20, 0, 20) }, cold, 0);
        world.RunTicks(240);
        var shown = Assert.Single(world.Snapshot.Characters, c => c.Id == id);
        var pose = Assert.IsType<BonePose>(shown.Ragdoll);
        var matrices = new Matrix4x4[rig.BoneCount];
        var scratch = rig.Rent();
        try { scratch.Pose(setup.Shape, [], matrices, pose); }
        finally { rig.Return(scratch); }
        var placement = CharacterRigs.Placement(setup, shown.Position, shown.Yaw, 1);
        var lowerLegs = model.Parts.SelectMany(part => part.Vertices).Where(vertex =>
        {
            int strongest = 0;
            for (int influence = 1; influence < 4; influence++) if (vertex.Weights[influence] > vertex.Weights[strongest]) strongest = influence;
            int bone = (int)((vertex.Bones >> (8 * strongest)) & 255);
            string name = rig.ByHandle[bone]?.Name ?? "";
            return name.Contains("Calf", StringComparison.Ordinal) || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal);
        }).Select(vertex => Vector3.Transform(Skin(vertex, matrices), placement)).ToArray();
        Assert.True(lowerLegs.Length > 100);
        float lowest = lowerLegs.Min(point => point.Y);
        Assert.True(lowest >= -1, $"Retained calf/foot skin goes {lowest:F3} below the physical floor");
        Assert.True(pose.AbsolutePositions, "The remnant must publish independently positioned physics bones");
    }
    (SkeletonRig Rig, RagdollDefinition Definition, Model Model, CharacterPoseSetup Setup, CharacterRigs Rigs, CharacterAppearance Look) Installed()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var skeletonPath = GameFiles.Resolve(Path.Combine(install.DataDirectory, "character", "meshes", "male_skeleton", "male_skeleton.skeleton"));
        var meshPath = GameFiles.Resolve(Path.Combine(install.DataDirectory, "character", "meshes", "human", "human_male.mesh"));
        var ragdollPath = GameFiles.Resolve(Path.Combine(install.DataDirectory, "ragdoll", "hu_m_ragdoll_5.phs"));
        Assert.SkipWhen(!File.Exists(skeletonPath) || !File.Exists(meshPath) || !File.Exists(ragdollPath), "Installed human mesh, skeleton or ragdoll not found");
        var rig = new SkeletonRig(OgreSkeletonReader.ReadFile(skeletonPath));
        var assets = new AssetLocator(install);
        Vector3[]? Hull(string name) => assets.Find(name) is { } path ? CookedMeshReader.ReadConvex(File.ReadAllBytes(path)).Vertices : null;
        var definition = Assert.IsType<RagdollDefinition>(RagdollDefinition.Build(PhysicsFile.ReadFile(ragdollPath), rig, 1, Hull));
        var model = Model.Build(OgreMeshReader.ReadFile(meshPath), rig.BoneCount);
        var idle = rig.Layer(null, "idle_stand_normal") ?? rig.Layer(null, "idle_stand_relax");
        var setup = new CharacterPoseSetup { Rig = rig, Fixed = idle is null ? [] : [(idle, 0.5f)] };
        output.WriteLine($"Initial posture: {idle?.Label ?? "bind"}");
        var database = new GameDatabase();
        var records = new FcsFile();
        var race = new FcsRecord { StringId = "skin-test-race", RecordType = FcsRecordType.RACE, Name = "Skin test race", Flags = 0x10 };
        race.Filenames["male mesh"] = meshPath;
        records.Records.Add(race);
        database.Apply(records, "skin-test.mod");
        var look = CharacterAppearance.Build(database, install.Root, "skin-test-race", new() { Female = false, Naked = true });
        var rigs = new CharacterRigs(database, path => assets.Find(path), path => assets.Find(path));
        return (rig, definition, model, setup, rigs, look);
    }

    void CheckCutSkin(SkeletonRig rig, Model model, CharacterPoseSetup setup, SimWorld world, CharacterId id, TorsoBisectionState cut, TorsoBisectionStyle style, int[] ticks)
    {
        var beforeSkin = new Matrix4x4[rig.BoneCount];
        var afterSkin = new Matrix4x4[rig.BoneCount];
        var scratch = rig.Rent();
        try
        {
            scratch.Pose(setup.Shape, setup.Fixed.Select(layer => new ActiveLayer(layer.Def, layer.Time, 1)).ToArray(), beforeSkin);
            var baseline = model.Parts.Select(part => part.Vertices.Select(v => Skin(v, beforeSkin)).ToArray()).ToArray();
            var source = model.Parts.SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray();
            var groups = new uint[8];
            foreach (var bone in rig.Skeleton.Bones)
                if (bone.Handle < 64) groups[bone.Handle / 8] |= (uint)(bone.Name == "Bip01 Jaw" ? 6 : WoundMasks.BodyGroup(bone.Name)) << (bone.Handle % 8 * 4);
            var caps = new Dictionary<int, (Vertex[] Vertices, Vector3[] Baseline)>();

            foreach (int tick in ticks)
            {
                while (world.Tick < tick) world.RunTick();
                var shown = Assert.Single(world.Snapshot.Characters, c => c.Id == id);
                var halves = Assert.IsType<TorsoRagdollPose>(shown.TorsoRagdoll);
                foreach (int side in new[] { -1, 1 })
                {
                    var pose = side > 0 ? halves.Positive : halves.Negative;
                    Assert.True(pose.AbsolutePositions);
                    Assert.NotNull(pose.Components);
                    if (style == TorsoBisectionStyle.Oblique && side < 0)
                    {
                        int trunk = ComponentOf(rig, pose.Components, "Bip01");
                        foreach (string bone in new[] { "Bip01 L Forearm", "Bip01 R Forearm" })
                            Assert.True(ComponentOf(rig, pose.Components, bone) != trunk, $"Oblique tick{tick}: the cut {bone} must remain disconnected from the trunk");
                    }
                    if (!caps.ContainsKey(side))
                    {
                        var cap = TorsoCutSurface.Build(source, groups, new(cut.Plane, side, true), components: pose.Components);
                        Assert.NotNull(cap);
                        AssertCapComponents(cap, pose.Components);
                        caps[side] = (cap, cap.Select(vertex => Skin(vertex, beforeSkin)).ToArray());
                    }
                    scratch.Pose(setup.Shape, [], afterSkin, pose);
                    CheckEdges(model, baseline, afterSkin, cut.Plane, side, tick, style);
                    CheckCapEdges(caps[side].Vertices, caps[side].Baseline, afterSkin, side, tick, style);
                }
            }
        }
        finally { rig.Return(scratch); }
    }

    static int ComponentOf(SkeletonRig rig, int[] components, string bone)
    {
        int handle = Assert.IsType<int>(rig.Handle(bone));
        Assert.InRange(handle, 0, components.Length - 1);
        Assert.True(components[handle] >= 0, $"{bone} must name a physical component");
        return components[handle];
    }

    void CheckEdges(Model model, Vector3[][] baseline, Matrix4x4[] skin, Vector4 plane, int side, int tick, TorsoBisectionStyle style)
    {
        Span<EdgePoint> clipped = stackalloc EdgePoint[4];
        float worst = 0;
        string message = "";
        int checkedEdges = 0;
        for (int partIndex = 0; partIndex < model.Parts.Count; partIndex++)
        {
            var part = model.Parts[partIndex];
            var posed = part.Vertices.Select(vertex => Skin(vertex, skin)).ToArray();
            EdgePoint Point(uint index) => new(part.Vertices[index].Position, baseline[partIndex][index], posed[index]);
            for (int triangle = 0; triangle < part.Indices.Length; triangle += 3)
            {
                int count = Clip(Point(part.Indices[triangle]), Point(part.Indices[triangle + 1]), Point(part.Indices[triangle + 2]), plane, side, clipped);
                for (int edge = 0; edge < count; edge++)
                {
                    var a = clipped[edge];
                    var b = clipped[(edge + 1) % count];
                    float length = Vector3.Distance(a.Before, b.Before);
                    if (length < 0.0001f) continue;
                    float now = Vector3.Distance(a.After, b.After);
                    Assert.True(float.IsFinite(now), $"{style} side{side} tick{tick}: a retained mesh edge is not finite");
                    // A generous envelope allows ordinary joint bends and skinning distortion, but catches metre-long elastic strips.
                    float allowed = MathF.Max(8 * length, 6);
                    float ratio = now / allowed;
                    if (ratio > worst)
                    {
                        worst = ratio;
                        message = $"{style} side{side} tick{tick} part{partIndex} triangle{triangle / 3}: retained edge {a.Bind}->{b.Bind} grew from {length:F3} to {now:F3} (limit {allowed:F3})";
                    }
                    checkedEdges++;
                }
            }
        }
        Assert.True(checkedEdges > 1000, "The retained installed mesh must provide substantial real skinning coverage");
        output.WriteLine(message);
        Assert.True(worst <= 1, message);
    }

    static void AssertCapComponents(Vertex[] cap, int[] components)
    {
        for (int triangle = 0; triangle < cap.Length; triangle += 3)
        {
            int component = -1;
            for (int vertex = triangle; vertex < triangle + 3; vertex++)
                for (int influence = 0; influence < 4; influence++)
                {
                    if (cap[vertex].Weights[influence] <= 0.00001f) continue;
                    int bone = (int)((cap[vertex].Bones >> (8 * influence)) & 255);
                    Assert.InRange(bone, 0, components.Length - 1);
                    int next = components[bone];
                    Assert.True(next >= 0, "Every cap influence must name a physical component");
                    if (component < 0) component = next;
                    Assert.True(component == next, $"Cap triangle{triangle / 3} joins disconnected physical components{component}/{next}");
                }
        }
    }

    void CheckCapEdges(Vertex[] cap, Vector3[] baseline, Matrix4x4[] skin, int side, int tick, TorsoBisectionStyle style)
    {
        var posed = cap.Select(vertex => Skin(vertex, skin)).ToArray();
        float worst = 0;
        string message = "";
        for (int triangle = 0; triangle < cap.Length; triangle += 3)
            for (int edge = 0; edge < 3; edge++)
            {
                int a = triangle + edge, b = triangle + (edge + 1) % 3;
                float before = Vector3.Distance(baseline[a], baseline[b]);
                if (before < 0.0001f) continue;
                float after = Vector3.Distance(posed[a], posed[b]);
                Assert.True(float.IsFinite(after));
                float limit = MathF.Max(8 * before, 6);
                float ratio = after / limit;
                if (ratio <= worst) continue;
                worst = ratio;
                message = $"{style} side{side} tick{tick} cap contour{cap[a].Uv.Y} triangle{triangle / 3}: rim edge {cap[a].Position}->{cap[b].Position} grew from {before:F3} to {after:F3} (limit {limit:F3})";
            }
        output.WriteLine(message);
        Assert.True(worst <= 1, message);
    }

    static int Clip(EdgePoint a, EdgePoint b, EdgePoint c, Vector4 plane, int side, Span<EdgePoint> result)
    {
        Span<EdgePoint> source = stackalloc EdgePoint[3] { a, b, c };
        float Distance(EdgePoint point) => side * (Vector3.Dot(new(plane.X, plane.Y, plane.Z), point.Bind) + plane.W);
        int count = 0;
        for (int edge = 0; edge < 3; edge++)
        {
            var previous = source[(edge + 2) % 3];
            var current = source[edge];
            float previousDistance = Distance(previous), currentDistance = Distance(current);
            bool previousInside = previousDistance >= 0, currentInside = currentDistance >= 0;
            if (previousInside != currentInside)
            {
                float amount = previousDistance / (previousDistance - currentDistance);
                result[count++] = new(Vector3.Lerp(previous.Bind, current.Bind, amount), Vector3.Lerp(previous.Before, current.Before, amount), Vector3.Lerp(previous.After, current.After, amount));
            }
            if (currentInside) result[count++] = current;
        }
        return count;
    }

    static Vector3 Skin(Vertex vertex, Matrix4x4[] matrices)
    {
        var result = Vector3.Zero;
        for (int influence = 0; influence < 4; influence++)
        {
            int bone = (int)((vertex.Bones >> (8 * influence)) & 255);
            float weight = vertex.Weights[influence];
            if (weight > 0 && bone < matrices.Length) result += Vector3.Transform(vertex.Position, matrices[bone]) * weight;
        }
        return result;
    }
}
