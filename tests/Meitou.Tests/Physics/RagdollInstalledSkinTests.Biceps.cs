using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    sealed class BicepsAnimations(CharacterAnimation carrier) : ITickSystem, IAnimationSource
    {
        public string Gait = "";
        public float Time;
        public IReadOnlyList<AnimationLayer> Publish(CharacterAnimation animation, List<AnimationLayer> into)
        {
            into.Clear();
            if (ReferenceEquals(animation, carrier))
            {
                into.Add(new(Gait, Time, 1));
                into.Add(new("carrying left arm only", 0, 1));
            }
            // The original carried clip remains authoritative; fragment support may only translate its pose.
            else into.Add(new(CarrySystem.CarriedClip, 0, 1));
            return into;
        }
    }

    [Theory, Slow]
    [InlineData(false, TorsoBisectionStyle.Transverse, 2)]
    [InlineData(true, TorsoBisectionStyle.Transverse, 2)]
    [InlineData(false, TorsoBisectionStyle.Oblique, 2)]
    [InlineData(true, TorsoBisectionStyle.Vertical, 1)]
    public void Installed_halves_rest_at_their_middle_on_the_upper_arm_without_changing_the_carried_pose(bool female, TorsoBisectionStyle style, int direction)
    {
        var (rig, definition, model, setup, rigs, look) = Installed(female);
        var gaits = new[] { "walk", "jog" }.Select(prefix => Assert.IsType<OgreAnimation>(rig.Skeleton.Animations.FirstOrDefault(animation =>
            animation.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !animation.Name.Contains("carrying", StringComparison.OrdinalIgnoreCase)))).ToArray();
        var carrierAnimation = new CharacterAnimation();
        var animation = new BicepsAnimations(carrierAnimation) { Gait = gaits[0].Name };
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [animation, physics]);
        var cut = TorsoBisectionPolicy.StateForDirection(direction, 1, Vector2.UnitX, -MathF.PI / 2, style);
        var cold = new CharacterCold { Name = "Biceps QA corpse", Race = Human, Medical = Healthy(), Appearance = look, TorsoBisection = cut };
        cold.Medical.KillFromBisection();
        var id = world.Characters.Spawn(new() { Position = new(1000, 0, 1000) }, cold, 0);
        world.RunTicks(240);
        cold.Animation = new();
        var holders = new Dictionary<int, CharacterId>();
        foreach (int side in new[] { -1, 1 })
        {
            var carrier = new CharacterCold { Name = "Biceps QA carrier", Race = Human, Medical = Healthy(), Appearance = look,
                Animation = carrierAnimation, Carrying = id, CarryingTorsoSide = side > 0 ? 1 : 0 };
            holders[side] = world.Characters.Spawn(new() { Position = new(side > 0 ? 1200 : 1000, 0, 1000), Health = 100 }, carrier, world.Tick);
        }
        cold.CarriedBy = holders[-1];
        cold.TorsoPositiveCarriedBy = holders[1];
        var source = model.Parts.SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray();
        var groups = new uint[8];
        foreach (var bone in rig.Skeleton.Bones)
            if (bone.Handle < 64) groups[bone.Handle / 8] |= (uint)(bone.Name == "Bip01 Jaw" ? 6 : WoundMasks.BodyGroup(bone.Name)) << (bone.Handle % 8 * 4);
        var originalSkin = RagdollSkinSurface.Read(OgreMeshReader.ReadFile(rigs.FindMesh(look.BodyMesh)!));
        var roots = new Dictionary<int, int>();
        var fragmentDefinitions = new Dictionary<int, RagdollDefinition>();
        var witnesses = new Dictionary<int, List<(int Triangle, Vector3 Bary)>>();
        foreach (int side in new[] { -1, 1 })
        {
            var fragment = Assert.IsType<RagdollDefinition>(RagdollCut.Build(definition, cut.Plane, side, cut.FullBody,
                cut.FullBody ? originalSkin.Bridges(rig, definition, cut.Plane, side) : null));
            roots[side] = fragment.Bodies[fragment.Root].Bone;
            fragmentDefinitions[side] = fragment;
        }
        int shoulderBone = Assert.IsType<int>(rig.Handle("Bip01 L UpperArm"));
        int elbowBone = Assert.IsType<int>(rig.Handle("Bip01 L Forearm"));
        var scratch = rig.Rent();
        try
        {
            foreach (var gait in gaits)
                for (int phase = 0; phase < 3; phase++)
                {
                    animation.Gait = gait.Name;
                    animation.Time = gait.Length * phase / 3;
                    float yaw = phase * 0.9f - 0.6f;
                    foreach (int side in new[] { -1, 1 }) world.Characters.Place(holders[side].Slot, new(side > 0 ? 1200 : 1000, 0, 1000 + phase * 4), yaw);
                    world.Characters.Place(id.Slot, new(1000, 0, 1000 + phase * 4), yaw);
                    world.RunTick();
                    var carrierLayers = animation.Publish(carrierAnimation, []).Select(layer =>
                        (Layer: rig.Layer(rigs.Database, layer.Name), layer.Time, layer.Weight)).Where(layer => layer.Layer is not null)
                        .Select(layer => new ActiveLayer(layer.Layer!, layer.Time, layer.Weight)).ToList();
                    foreach (var (layer, time) in setup.Fixed) carrierLayers.Add(new(layer, time, 1));
                    scratch.PoseDerived(setup.Shape, carrierLayers.ToArray());
                    var localShoulder = scratch.DerivedPosition[shoulderBone];
                    var localElbow = scratch.DerivedPosition[elbowBone];
                    var shown = Assert.Single(world.Snapshot.Characters, character => character.Id == id);
                    var halves = Assert.IsType<TorsoRagdollPose>(shown.TorsoRagdoll);
                    var held = (shown.Carry ?? CarryPose.Fallback).World(shown.Position, shown.Yaw);
                    var placement = CharacterRigs.Placement(setup, held.Position, held.Rotation, 1);
                    foreach (int side in new[] { -1, 1 })
                    {
                        var holder = Assert.Single(world.Snapshot.Characters, character => character.Id == holders[side]);
                        var carrierWorld = CharacterRigs.Placement(setup, holder.Position, holder.Yaw, 1);
                        var shoulder = Vector3.Transform(localShoulder, carrierWorld);
                        var elbow = Vector3.Transform(localElbow, carrierWorld);
                        var arm = elbow - shoulder;
                        float armLength = arm.Length();
                        var pose = side > 0 ? halves.Positive : halves.Negative;
                        Assert.NotNull(pose.Components);
                        int root = pose.Components[roots[side]];
                        var palette = new Matrix4x4[rig.BoneCount];
                        scratch.Pose(setup.Shape, [], palette, pose);
                        var faces = BicepsVisibleRootSkin(source, groups, new(cut.Plane, side, cut.FullBody), palette, placement, pose.Components, root);
                        Assert.True(faces.Count > 100, "The independent witness must measure substantial retained root-connected skin");
                        var attachment = side > 0 ? shown.TorsoPositiveCarry : shown.Carry;
                        var oldPlacement = (attachment ?? CarryPose.Fallback).World(holder.Position, holder.Yaw);
                        var oldWorld = CharacterRigs.Placement(setup, oldPlacement.Position, oldPlacement.Rotation, 1);
                        Assert.True(Matrix4x4.Invert(oldWorld, out var referenceInverse));
                        // Reconstruct the unchanged original carried target, then measure its uniform root translation.
                        var carriedLayers = new List<ActiveLayer>();
                        if (rig.Layer(rigs.Database, CarrySystem.CarriedClip) is { } carried) carriedLayers.Add(new(carried, 0, 1));
                        foreach (var (layer, time) in setup.Fixed) carriedLayers.Add(new(layer, time, 1));
                        scratch.PoseDerived(setup.Shape, carriedLayers.ToArray());
                        var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(oldWorld with { M41 = 0, M42 = 0, M43 = 0 }));
                        var both = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(physics.SaveState(id)).Torso);
                        var saved = side > 0 ? both.Positive : both.Negative;
                        var fragment = fragmentDefinitions[side];
                        int rootBone = fragment.Bodies[fragment.Root].Bone;
                        var oldRoot = Vector3.Transform(scratch.DerivedPosition[rootBone], oldWorld);
                        var actualRoot = fragment.Bodies[fragment.Root].BoneInBody.Then(saved.Pose[fragment.Root]).Position;
                        var shift = actualRoot - oldRoot;
                        var support = CarryPose.BicepsSupport(shoulder, elbow);
                        string sample = $"female{female}/{style}/side{side}/{gait.Name}/phase{phase}";
                        float RootShare(int triangle, Vector3 bary)
                        {
                            float share = 0;
                            for (int point = 0; point < 3; point++)
                                for (int influence = 0; influence < 4; influence++)
                                {
                                    var vertex = source[triangle + point];
                                    int bone = (int)(vertex.Bones >> (8 * influence) & 255);
                                    if (bone < pose.Components.Length && pose.Components[bone] == root) share += bary[point] * vertex.Weights[influence];
                                }
                            return share;
                        }
                        Vector3 Material((int Triangle, Vector3 Bary) witness)
                        {
                            var point = Vector3.Zero;
                            for (int vertex = 0; vertex < 3; vertex++)
                                point += Vector3.Transform(Skin(source[witness.Triangle + vertex], palette), placement) * witness.Bary[vertex];
                            return point;
                        }
                        if (!witnesses.TryGetValue(side, out var candidates))
                        {
                            // Skin with loose-chain weights moves by RootShare * shift. Undo that on original triangle barycentrics.
                            var before = faces.Select(face => face with
                            {
                                A = face.A - shift * RootShare(face.Triangle, face.BaryA),
                                B = face.B - shift * RootShare(face.Triangle, face.BaryB),
                                C = face.C - shift * RootShare(face.Triangle, face.BaryC),
                            }).ToArray();
                            var points = before.SelectMany(face => new[] { face.A, face.B, face.C })
                                .Select(point => Vector3.Transform(point, referenceInverse)).ToArray();
                            var minimum = points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
                            var maximum = points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
                            var centre = Vector3.Transform((minimum + maximum) * 0.5f, oldWorld);
                            float bestCentreDistance = before.Min(face =>
                            {
                                var bary = RagdollSkinSurface.ClosestBarycentric(centre, face.A, face.B, face.C);
                                return Vector3.Distance(centre, face.A * bary.X + face.B * bary.Y + face.C * bary.Z);
                            });
                            candidates = [];
                            foreach (var face in faces)
                            {
                                var bary = RagdollSkinSurface.ClosestBarycentric(support, face.A, face.B, face.C);
                                var original = face.BaryA * bary.X + face.BaryB * bary.Y + face.BaryC * bary.Z;
                                var material = Material((face.Triangle, original));
                                if (Vector3.Distance(material, support) > 0.005f) continue;
                                var originalPoint = material - shift * RootShare(face.Triangle, original);
                                if (Vector3.Distance(originalPoint, centre) <= bestCentreDistance + 0.02f) candidates.Add((face.Triangle, original));
                            }
                            Assert.True(candidates.Count > 0, sample + $": no original contact material lies at the pickup3D middle (nearest skin {bestCentreDistance:F4})");
                            witnesses[side] = candidates;
                        }
                        // Folded skin may have coincident triangles at pickup. Only prune that original set; never select new material.
                        candidates.RemoveAll(witness => Vector3.Distance(Material(witness), support) > 0.005f);
                        Assert.True(candidates.Count > 0, sample + ": the original midpoint material slipped away from its upper-arm support");
                        var contact = candidates.Select(Material).MinBy(point => Vector3.DistanceSquared(point, support));
                        float projection = Vector3.Dot(contact - shoulder, arm) / arm.LengthSquared();
                        float radial = Vector3.Distance(contact, shoulder + arm * Math.Clamp(projection, 0, 1));
                        Assert.InRange(projection, 0.25f, 0.75f);
                        Assert.True(radial < armLength * 0.20f, sample + $": visible fragment misses upper-arm support by {radial:F3}, upper-arm length {armLength:F3}");
                        Assert.InRange(Vector3.Distance(contact, support), 0, 0.005f);
                        output.WriteLine($"{sample}: original midpoint witnesses {candidates.Count}, upper-arm projection {projection:F3}, radial gap {radial:F4}");

                        for (int body = 0; body < fragment.Bodies.Length; body++)
                        {
                            var actor = fragment.Bodies[body];
                            if (pose.Components[actor.Bone] != root) continue;
                            var actual = actor.BoneInBody.Then(saved.Pose[body]);
                            var expectedRotation = Quaternion.Normalize(turn * scratch.DerivedRotation[actor.Bone]);
                            Assert.True(MathF.Abs(Quaternion.Dot(expectedRotation, actual.Rotation)) >= 0.99999f,
                                sample + $": repositioning changed {actor.Name}'s original carried rotation");
                            var oldOffset = Vector3.Transform(scratch.DerivedPosition[actor.Bone], oldWorld) - oldRoot;
                            var actualOffset = actual.Position - actualRoot;
                            Assert.InRange(Vector3.Distance(oldOffset, actualOffset), 0, 0.005f);
                        }
                    }
                }
        }
        finally { rig.Return(scratch); }
    }

    readonly record struct BicepsFace(Vector3 A, Vector3 B, Vector3 C, int Triangle, Vector3 BaryA, Vector3 BaryB, Vector3 BaryC);

    static List<BicepsFace> BicepsVisibleRootSkin(Vertex[] source, uint[] groups, CharacterTorsoCut cut, Matrix4x4[] palette,
        Matrix4x4 placement, int[] components, int root)
    {
        var result = new List<BicepsFace>();
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var vertices = source[triangle..(triangle + 3)];
            var posed = vertices.Select(vertex => Vector3.Transform(Skin(vertex, palette), placement)).ToArray();
            var bands = vertices.Select(vertex => TorsoCutSurface.Band(vertex.Position, TorsoCutSurface.Group(vertex, groups), cut)).ToArray();
            var shares = vertices.Select(vertex =>
            {
                float share = 0;
                for (int influence = 0; influence < 4; influence++)
                {
                    int bone = (int)(vertex.Bones >> (8 * influence) & 255);
                    if (bone < components.Length && components[bone] == root) share += vertex.Weights[influence];
                }
                return share;
            }).ToArray();
            var polygon = new List<Vector3> { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            Clip(new(bands[0], bands[1], bands[2]), 0);
            Clip(new(shares[0], shares[1], shares[2]), 0.5f);
            Vector3 At(Vector3 bary) => posed[0] * bary.X + posed[1] * bary.Y + posed[2] * bary.Z;
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                var face = new BicepsFace(At(polygon[0]), At(polygon[index]), At(polygon[index + 1]), triangle, polygon[0], polygon[index], polygon[index + 1]);
                if (Vector3.Cross(face.B - face.A, face.C - face.A).LengthSquared() > 1e-10f) result.Add(face);
            }
            void Clip(Vector3 field, float threshold)
            {
                var clipped = new List<Vector3>();
                for (int index = 0; index < polygon.Count; index++)
                {
                    var a = polygon[index]; var b = polygon[(index + 1) % polygon.Count];
                    float da = Vector3.Dot(field, a) - threshold, db = Vector3.Dot(field, b) - threshold;
                    if (da >= 0) clipped.Add(a);
                    if ((da < 0) != (db < 0)) clipped.Add(Vector3.Lerp(a, b, da / (da - db)));
                }
                polygon = clipped;
            }
        }
        return result;
    }

}
