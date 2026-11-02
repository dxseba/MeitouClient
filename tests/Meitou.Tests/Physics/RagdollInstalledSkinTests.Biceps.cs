using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    sealed class WholeCarryQaAnimations(CharacterAnimation carrier) : ITickSystem, IAnimationSource
    {
        public string Gait = "";
        public float Time;
        public IReadOnlyList<AnimationLayer> Publish(CharacterAnimation animation, List<AnimationLayer> into)
        {
            into.Clear();
            if (ReferenceEquals(animation, carrier)) { into.Add(new(Gait, Time, 1)); into.Add(new("carrying left arm only", 0, 1)); }
            else into.Add(new(CarrySystem.CarriedClip, 0, 1));
            return into;
        }
    }

    [Theory, Slow]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void Installed_halves_preserve_actual_whole_carry_seats_and_rendered_descendants(bool female, bool missingLeg)
    {
        var (rig, definition, model, setup, rigs, look) = Installed(female);
        var walk = Assert.Single(rig.Skeleton.Animations.Where(a => a.Name.StartsWith("walk", StringComparison.OrdinalIgnoreCase)
            && !a.Name.Contains("carrying", StringComparison.OrdinalIgnoreCase)).Take(1));
        var jog = Assert.Single(rig.Skeleton.Animations.Where(a => a.Name.StartsWith("jog", StringComparison.OrdinalIgnoreCase)
            && !a.Name.Contains("carrying", StringComparison.OrdinalIgnoreCase)).Take(1));
        var carrierAnimation = new CharacterAnimation();
        var animation = new WholeCarryQaAnimations(carrierAnimation) { Gait = walk.Name };
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [animation, physics]);
        var cut = new TorsoBisectionState(new(0, 1, 0, -12));
        CharacterCold Corpse(bool bisected)
        {
            var cold = new CharacterCold { Name = bisected ? "Cut QA" : "Whole QA", Race = Human, Medical = Healthy(), Appearance = look,
                TorsoBisection = bisected ? cut : null };
            if (missingLeg) cold.Medical.Sever(LArm + 2, Context(), false);
            cold.Medical.KillFromBisection();
            return cold;
        }
        var wholeCold = Corpse(false); var halfCold = Corpse(true);
        var wholeId = world.Characters.Spawn(new() { Position = new(1000, 0, 1000) }, wholeCold, 0);
        var halfId = world.Characters.Spawn(new() { Position = new(1400, 0, 1000) }, halfCold, 0);
        world.RunTicks(240);
        wholeCold.Animation = new();
        halfCold.Animation = new();
        CharacterId Holder(CharacterId body, float x, int side)
        {
            var cold = new CharacterCold { Name = "QA carrier", Appearance = look, Race = Human, Medical = Healthy(), Animation = carrierAnimation,
                Carrying = body, CarryingTorsoSide = side };
            return world.Characters.Spawn(new() { Position = new(x, 0, 1000), Health = 100 }, cold, world.Tick);
        }
        var controlHolder = Holder(wholeId, 1000, 0);
        var holders = new Dictionary<int, CharacterId> { [-1] = Holder(halfId, 1400, 0), [1] = Holder(halfId, 1800, 1) };
        wholeCold.CarriedBy = controlHolder;
        halfCold.CarriedBy = holders[-1]; halfCold.TorsoPositiveCarriedBy = holders[1];
        var remaining = missingLeg ? Assert.IsType<RagdollDefinition>(RagdollSeverance.Split(definition, 2, SeveredLevels.Height(SeveredLevels.Of(halfId, 2), 2)).Remaining) : definition;
        var fragments = new Dictionary<int, RagdollDefinition>();
        foreach (int side in new[] { -1, 1 }) fragments[side] = Assert.IsType<RagdollDefinition>(RagdollCut.Build(remaining, cut.Plane, side, false));
        var source = model.Parts.SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray();
        var vertices = model.Parts.SelectMany(part => part.Vertices).Where((_, index) => index % 2 == 0).ToArray();
        var groups = new uint[8];
        foreach (var bone in rig.Skeleton.Bones)
            if (bone.Handle < 64) groups[bone.Handle / 8] |= (uint)(bone.Name == "Bip01 Jaw" ? 6 : WoundMasks.BodyGroup(bone.Name)) << (bone.Handle % 8 * 4);
        int Actor(int bone) { while (bone >= 0 && definition.BodyOfBone[bone] < 0) bone = rig.Parent[bone]; return bone; }
        Matrix4x4 Bind(int bone) => Matrix4x4.CreateScale(rig.BindScale[bone]) * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rig.BindRotationInverse[bone]))
            * Matrix4x4.CreateTranslation(rig.BindPosition[bone]);
        var descendantSamples = vertices.Select(vertex =>
        {
            var influences = Enumerable.Range(0, 4).Where(k => vertex.Weights[k] > 0.00001f).Select(k => (int)(vertex.Bones >> (8 * k) & 255)).ToArray();
            var actors = influences.Select(Actor).Distinct().ToArray();
            return (Vertex: vertex, Actor: actors.Length == 1 && influences.Any(h => definition.BodyOfBone[h] < 0) ? actors[0] : -1);
        }).Where(sample => sample.Actor >= 0).ToArray();
        var scratch = rig.Rent();
        (int Triangle, Vector3 Bary)? originalSeat = null;
        int pinnedChecks = 0, childChecks = 0;
        float wholeDynamic = 0, halfDynamic = 0;
        try
        {
            foreach (var gait in new[] { walk, jog }) for (int phase = 0; phase < 3; phase++)
            {
                animation.Gait = gait.Name; animation.Time = gait.Length * phase / 3;
                float yaw = phase * 0.9f - 0.6f;
                world.Characters.Place(controlHolder.Slot, new(1000, 0, 1000 + phase * 4), yaw);
                world.Characters.Place(wholeId.Slot, new(1000, 0, 1000 + phase * 4), yaw);
                foreach (int side in new[] { -1, 1 }) world.Characters.Place(holders[side].Slot, new(side > 0 ? 1800 : 1400, 0, 1000 + phase * 4), yaw);
                world.Characters.Place(halfId.Slot, new(1400, 0, 1000 + phase * 4), yaw);
                world.RunTicks(8);
                var layers = new List<ActiveLayer>();
                if (rig.Layer(rigs.Database, CarrySystem.CarriedClip) is { } carry) layers.Add(new(carry, 0, 1));
                foreach (var (layer, time) in setup.Fixed) layers.Add(new(layer, time, 1));
                var whole = Assert.Single(world.Snapshot.Characters, c => c.Id == wholeId);
                var cutBody = Assert.Single(world.Snapshot.Characters, c => c.Id == halfId);
                var wholePose = Assert.IsType<BonePose>(whole.Ragdoll);
                var halves = Assert.IsType<TorsoRagdollPose>(cutBody.TorsoRagdoll);
                var wholePalette = new Matrix4x4[rig.BoneCount];
                var animated = new Matrix4x4[rig.BoneCount];
                scratch.Pose(setup.Shape, layers.ToArray(), wholePalette, wholePose);
                scratch.Pose(setup.Shape, layers.ToArray(), animated);
                var carrierLayers = animation.Publish(carrierAnimation, []).Select(l => (Def: rig.Layer(rigs.Database, l.Name), l.Time, l.Weight))
                    .Where(l => l.Def is not null).Select(l => new ActiveLayer(l.Def!, l.Time, l.Weight)).Concat(setup.Fixed.Select(l => new ActiveLayer(l.Def, l.Time, 1))).ToArray();
                scratch.PoseDerived(setup.Shape, carrierLayers);
                int hand = Assert.IsType<int>(rig.Handle("Bip01 L Hand"));
                Vector3? Point(string name) => rig.Handle(name) is { } h ? scratch.DerivedPosition[h] : null;
                var palm = CarryPose.PalmPoint(scratch.DerivedPosition[hand], Point("Bip01 L Finger0") ?? Point("Bip01 L Finger1"), Point("Bip01 L Forearm"));
                var control = Assert.Single(world.Snapshot.Characters, c => c.Id == controlHolder);
                var controlWorld = CharacterRigs.Placement(setup, control.Position, control.Yaw, 1);
                var wholeAt = (whole.Carry ?? CarryPose.Fallback).World(whole.Position, whole.Yaw);
                var wholeWorld = CharacterRigs.Placement(setup, wholeAt.Position, wholeAt.Rotation, 1);
                Assert.True(Matrix4x4.Invert(wholeWorld, out var wholeInverse));
                palm = Vector3.Transform(palm, controlWorld * wholeInverse);
                if (originalSeat is null) originalSeat = WholeQaClosestSeat(source, groups, wholePalette, rig, palm);
                var seat = originalSeat.Value;
                Vector3 Material(Matrix4x4[] palette) => Skin(source[seat.Triangle], palette) * seat.Bary.X
                    + Skin(source[seat.Triangle + 1], palette) * seat.Bary.Y + Skin(source[seat.Triangle + 2], palette) * seat.Bary.Z;
                var referenceSeat = Material(wholePalette);
                var halfAt = (cutBody.Carry ?? CarryPose.Fallback).World(cutBody.Position, cutBody.Yaw);
                var halfWorld = CharacterRigs.Placement(setup, halfAt.Position, halfAt.Rotation, 1);
                foreach (int side in new[] { -1, 1 })
                {
                    var pose = side > 0 ? halves.Positive : halves.Negative;
                    Assert.NotNull(pose.Components);
                    var fragment = fragments[side];
                    int rootBone = fragment.Bodies[fragment.Root].Bone;
                    int root = pose.Components[rootBone];
                    var holder = Assert.Single(world.Snapshot.Characters, c => c.Id == holders[side]);
                    var attachment = side > 0 ? cutBody.TorsoPositiveCarry : cutBody.Carry;
                    var ownAt = (attachment ?? CarryPose.Fallback).World(holder.Position, holder.Yaw);
                    Assert.True(Matrix4x4.Invert(CharacterRigs.Placement(setup, ownAt.Position, ownAt.Rotation, 1), out var ownInverse));
                    var palette = new Matrix4x4[rig.BoneCount];
                    scratch.Pose(setup.Shape, layers.ToArray(), palette, pose);
                    for (int bone = 0; bone < palette.Length; bone++) palette[bone] *= halfWorld * ownInverse;
                    Vector3 Origin(int bone, Matrix4x4[] skin) => Vector3.Transform(rig.BindPosition[bone], skin[bone]);
                    var shift = Origin(rootBone, palette) - Origin(rootBone, wholePalette);
                    string sample = $"female{female}/missingLeg{missingLeg}/side{side}/{gait.Name}/{phase}";
                    if (side < 0) Assert.True(shift.Length() < 0.005f, sample + $": original retained hip seat moved by {shift}");
                    else Assert.True(shift.Length() > 0.05f, sample + ": the missing original hip seat must be relocated to retained trunk skin");
                    foreach (var actor in fragment.Bodies)
                    {
                        int bone = actor.Bone;
                        if (pose.Components[bone] != root) continue;
                        var actualFrame = Bind(bone) * palette[bone];
                        var wholeFrame = Bind(bone) * wholePalette[bone];
                        Assert.True(Matrix4x4.Decompose(actualFrame, out _, out var actualRotation, out _));
                        Assert.True(Matrix4x4.Decompose(wholeFrame, out _, out var wholeRotation, out _));
                        if (!wholePose.Set[bone])
                        {
                            Assert.InRange(Vector3.Distance(Origin(bone, palette), Origin(bone, wholePalette) + shift), 0, 0.005f);
                            Assert.True(MathF.Abs(Quaternion.Dot(actualRotation, wholeRotation)) > 0.99999f, sample + $": pinned {actor.Name} differs from actual whole carry");
                            pinnedChecks++;
                        }
                        else
                        {
                            Assert.True(Matrix4x4.Decompose(Bind(bone) * animated[bone], out _, out var animatedRotation, out _));
                            wholeDynamic = MathF.Max(wholeDynamic, 1 - MathF.Abs(Quaternion.Dot(wholeRotation, animatedRotation)));
                            halfDynamic = MathF.Max(halfDynamic, 1 - MathF.Abs(Quaternion.Dot(actualRotation, animatedRotation)));
                        }
                    }
                    float seatGap = WholeQaSeatGap(source, groups, palette, rig, referenceSeat, cut, side, pose.Components, root, wholePose);
                    Assert.True(seatGap < 0.02f, sample + $": remaining anatomical seat misses actual whole support by {seatGap:F5}");
                    var frames = new Dictionary<int, (Matrix4x4 Whole, Matrix4x4 Half)>();
                    foreach (var vertex in descendantSamples)
                    {
                        if (fragment.BodyOfBone[vertex.Actor] < 0 || pose.Components[vertex.Actor] != root
                            || TorsoCutSurface.Band(vertex.Vertex.Position, TorsoCutSurface.Group(vertex.Vertex, groups), new(cut.Plane, side)) < 0
                            || missingLeg && TorsoCutSurface.Group(vertex.Vertex, groups) == 3) continue;
                        if (!frames.TryGetValue(vertex.Actor, out var inverse))
                        {
                            Assert.True(Matrix4x4.Invert(Bind(vertex.Actor) * wholePalette[vertex.Actor], out var wi));
                            Assert.True(Matrix4x4.Invert(Bind(vertex.Actor) * palette[vertex.Actor], out var hi));
                            frames[vertex.Actor] = inverse = (wi, hi);
                        }
                        var expected = Vector3.Transform(Skin(vertex.Vertex, wholePalette), inverse.Whole);
                        var actual = Vector3.Transform(Skin(vertex.Vertex, palette), inverse.Half);
                        Assert.True(Vector3.Distance(expected, actual) < 0.005f, sample + $": rendered descendant skin at {vertex.Vertex.Position} differs from actual whole carry by {Vector3.Distance(expected, actual):F5}");
                        childChecks++;
                    }
                }
            }
            Assert.True(pinnedChecks > 20 && childChecks > 100, "The paired control must exercise retained actors and substantial descendant skin");
            Assert.True(wholeDynamic > 0.00001f, "The ordinary control must exercise actual CarryMode dynamics");
            Assert.True(halfDynamic > 0.00001f, "Retained dynamic head/forearms must not be frozen into the carry animation");
        }
        finally { rig.Return(scratch); }
    }

    readonly record struct WholeQaFace(Vector3 A, Vector3 B, Vector3 C, int Triangle, Vector3 BA, Vector3 BB, Vector3 BC);

    static (int Triangle, Vector3 Bary) WholeQaClosestSeat(Vertex[] source, uint[] groups, Matrix4x4[] palette, SkeletonRig rig, Vector3 palm)
    {
        var best = WholeQaFaces(source, groups, palette, rig).Select(face =>
        {
            var bary = RagdollSkinSurface.ClosestBarycentric(palm, face.A, face.B, face.C);
            var point = face.A * bary.X + face.B * bary.Y + face.C * bary.Z;
            return (Face: face, Bary: bary, Distance: Vector3.DistanceSquared(point, palm));
        }).MinBy(candidate => candidate.Distance);
        return (best.Face.Triangle, best.Face.BA * best.Bary.X + best.Face.BB * best.Bary.Y + best.Face.BC * best.Bary.Z);
    }

    static float WholeQaSeatGap(Vertex[] source, uint[] groups, Matrix4x4[] palette, SkeletonRig rig, Vector3 reference,
        TorsoBisectionState cut, int side, int[] components, int root, BonePose wholeOverrides) => MathF.Sqrt(WholeQaFaces(source, groups, palette, rig, cut, side, components, root, wholeOverrides)
            .Min(face => FoliageLodBuilder.DistanceSquared(reference, face.A, face.B, face.C)));

    static List<WholeQaFace> WholeQaFaces(Vertex[] source, uint[] groups, Matrix4x4[] palette, SkeletonRig rig,
        TorsoBisectionState? cut = null, int side = 1, int[]? components = null, int root = 0, BonePose? dynamic = null)
    {
        bool Axial(int bone)
        {
            for (int h = bone; h >= 0; h = rig.Parent[h]) if (rig.ByHandle[h]?.Name is "Bip01 Head" or "Bip01 Jaw") return false;
            return WoundMasks.BodyGroup(rig.ByHandle[bone]?.Name ?? "") == 0;
        }
        var faces = new List<WholeQaFace>();
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var vertices = source[triangle..(triangle + 3)];
            var posed = vertices.Select(v => Skin(v, palette)).ToArray();
            var polygon = new List<Vector3> { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            var axial = vertices.Select(v => Enumerable.Range(0, 4).Where(k => v.Weights[k] > 0 && Axial((int)(v.Bones >> (8 * k) & 255))).Sum(k => v.Weights[k])).ToArray();
            Clip(new(axial[0], axial[1], axial[2]), 0.5f);
            if (cut is { } c)
            {
                var bands = vertices.Select(v => TorsoCutSurface.Band(v.Position, TorsoCutSurface.Group(v, groups), new(c.Plane, side, c.FullBody))).ToArray();
                Clip(new(bands[0], bands[1], bands[2]), 0);
            }
            if (components is not null)
            {
                var shares = vertices.Select(v => Enumerable.Range(0, 4).Where(k =>
                {
                    int bone = (int)(v.Bones >> (8 * k) & 255);
                    return v.Weights[k] > 0 && bone < components.Length && components[bone] == root && dynamic?.Set[bone] != true;
                }).Sum(k => v.Weights[k])).ToArray();
                Clip(new(shares[0], shares[1], shares[2]), 0.5f);
            }
            Vector3 At(Vector3 bary) => posed[0] * bary.X + posed[1] * bary.Y + posed[2] * bary.Z;
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                var face = new WholeQaFace(At(polygon[0]), At(polygon[i]), At(polygon[i + 1]), triangle, polygon[0], polygon[i], polygon[i + 1]);
                if (Vector3.Cross(face.B - face.A, face.C - face.A).LengthSquared() > 1e-10f) faces.Add(face);
            }
            void Clip(Vector3 field, float threshold)
            {
                var clipped = new List<Vector3>();
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                    float da = Vector3.Dot(a, field) - threshold, db = Vector3.Dot(b, field) - threshold;
                    if (da >= 0) clipped.Add(a);
                    if ((da < 0) != (db < 0)) clipped.Add(Vector3.Lerp(a, b, da / (da - db)));
                }
                polygon = clipped;
            }
        }
        return faces;
    }
}
