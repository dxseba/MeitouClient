using System.Numerics;
using System.Text;
using Meitou.Brutality;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Physics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;
using BowelScene = Meitou.Brutality.Physics.BowelRagdollScene;

namespace Meitou.Tests.Modding;

public sealed class BrutalityBowelTests
{
    sealed class Ground : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }
    static readonly Ground Floor = new();
    static BowelPresentation Ready(AnatomyLibrary library) => new(library, new BowelAssetPreparation(
        scale => BowelPresentation.BuildBowelAssets(library, scale), start: work => Task.FromResult(work())));

    [Fact]
    public void A_pending_first_plan_retries_current_cut_contacts_and_keeps_the_original_cut_age()
    {
        using var data = new AnatomyFixture();
        Func<BowelAssetSet>? build = null;
        var pending = new TaskCompletionSource<BowelAssetSet>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var preparation = new BowelAssetPreparation(scale => BowelPresentation.BuildBowelAssets(data.Library, scale),
            start: work => { build = work; return pending.Task; });
        using var bowel = new BowelPresentation(data.Library, preparation);
        var owner = Owner(keep: false); float x = 0;
        CutFluidContact? Contact(int half) => new(new(x + half * 4, 8, 0), Vector3.UnitY);
        Assert.False(bowel.Begin(owner, data.Appearance, data.Rig, 0, 0, Contact, Vector2.UnitX, 0x800000, Floor));
        Assert.False(bowel.HasSpilled(owner)); Assert.Equal(0, bowel.Count);
        x = 20; pending.SetResult(Assert.IsType<Func<BowelAssetSet>>(build)());
        Assert.True(bowel.Begin(owner, data.Appearance, data.Rig, 0, 4, Contact, Vector2.UnitX, 0x800000, Floor));
        bowel.Advance(4, (_, half) => Contact(half), _ => true, Floor, 120);
        Assert.Equal(2, bowel.Count); Assert.Equal(0, bowel.Attached); Assert.Equal(0, bowel.Moving);
        Assert.All(bowel.Rendered, piece => Assert.True(piece.Centre.X > 15));
    }

    [Fact]
    public void Actual_organ_five_triangles_start_inside_both_cut_faces_then_feed_out_and_publish_distinct_labels()
    {
        using var data = new AnatomyFixture();
        using var bowel = Ready(data.Library);
        var owner = Owner(keep: true);
        CutFluidContact? Contact(int half) => new(new(half * 4, 8, 0), half == 0 ? -Vector3.UnitY : Vector3.UnitY);
        Assert.True(bowel.Begin(owner, data.Appearance, data.Rig, 0, 0, Contact, Vector2.UnitX, 0x800000, Floor));
        Assert.False(bowel.Begin(owner, data.Appearance, data.Rig, 0, 0, Contact, Vector2.UnitX, 0x800000, Floor));
        bowel.Advance(0, (_, half) => Contact(half), _ => true, Floor, 0);
        Assert.Equal(2, bowel.Count); Assert.Equal(2, bowel.Attached); Assert.Equal(2, bowel.Rendered.Count);
        for (int half = 0; half < 2; half++)
        {
            var piece = bowel.Rendered[half]; var contact = Contact(half)!.Value;
            Assert.NotEmpty(piece.Vertices);
            Assert.All(piece.Vertices, vertex =>
            {
                var world = piece.Centre + vertex.Position;
                Assert.True(Vector3.Dot(world - contact.Position, contact.Normal) < -.09f);
                Assert.True(Vector3.Distance(world, contact.Position) < 5);
                Assert.True(float.IsFinite(vertex.Normal.LengthSquared()));
            });
        }
        bowel.Advance(.25, (_, half) => Contact(half), _ => true, Floor, 8);
        bowel.Advance(.5, (_, half) => Contact(half), _ => true, Floor, 15);
        Assert.Equal(2, bowel.Attached);
        Assert.Equal(new[] { 100, 101 }, bowel.Labels.Select(p => p.Piece).Order().ToArray());
        Assert.All(bowel.Labels, p => Assert.Equal("Intestines", p.Part));
        Assert.Contains(bowel.Rendered.SelectMany((piece, half) => piece.Vertices.Select(v => Vector3.Dot(piece.Centre + v.Position - Contact(half)!.Value.Position, Contact(half)!.Value.Normal))), distance => distance > 0);
        var held = bowel.Rendered.Select(p => (p.Centre, p.Version)).ToArray();
        bowel.Advance(.5, (_, half) => Contact(half), _ => true, Floor, 15);
        Assert.Equal(held, bowel.Rendered.Select(p => (p.Centre, p.Version)).ToArray());
    }

    [Fact]
    public void A_late_picture_keeps_original_cut_age_and_already_torn_tissue_rests_on_the_floor_without_replaying_emergence()
    {
        using var data = new AnatomyFixture();
        using var bowel = Ready(data.Library);
        CutFluidContact? Contact(int half) => new(new(half * 4, 8, 0), Vector3.UnitY);
        Assert.True(bowel.Begin(Owner(keep: false), data.Appearance, data.Rig, 0, 4, Contact, Vector2.UnitX, 0x800000, Floor));
        bowel.Advance(4, (_, half) => Contact(half), _ => true, Floor, 120);
        Assert.Equal(2, bowel.Count); Assert.Equal(0, bowel.Attached); Assert.Equal(0, bowel.Moving);
        Assert.All(bowel.Rendered, piece => Assert.All(piece.Vertices, vertex => Assert.InRange(piece.Centre.Y + vertex.Position.Y, -.01f, 1)));
    }

    [Fact]
    public void Retained_physical_tissue_follows_a_pickup_as_a_whole_sleeps_when_still_and_releases_a_teleport_without_a_tether()
    {
        using var scene = new BowelScene();
        var geometry = new BowelRigGeometry([.. from row in Enumerable.Range(0, 3)
            from column in Enumerable.Range(0, 3) select new Vector3((column - 1) * 1.2f, (row - 1) * 1.2f, 0)], .3f);
        Assert.True(scene.Add(1, geometry, new(0, 8, 0), Quaternion.Identity, Vector3.Zero, pinNode: 0, preserveAttachment: true));
        var first = scene.PoseOf(1)!;
        var points = Enumerable.Range(0, 9).Select(i => Node(first, i)).ToArray();
        var shift = new Vector3(12, 4, -2);
        Assert.True(scene.MoveAttachment(1, points[0] + shift));
        for (int i = 0; i < 9; i++) Assert.True(Vector3.Distance(points[i] + shift, Node(scene.PoseOf(1)!, i)) < .00001f);
        for (int tick = 0; tick < 60 * 13; tick++) scene.Step(1f / 60, Floor, tick);
        Assert.True(scene.IsAttached(1)); Assert.Equal(0, scene.Moving);
        var anchor = Node(scene.PoseOf(1)!, 0); var held = scene.PoseOf(1)!;
        Assert.True(scene.MoveAttachment(1, anchor)); scene.Step(0, Floor, 800); Assert.Same(held, scene.PoseOf(1));
        Assert.True(scene.MoveAttachment(1, anchor + new Vector3(.2f, .1f, 0))); Assert.Equal(1, scene.Moving);
        var before = Enumerable.Range(0, 9).Select(i => Node(scene.PoseOf(1)!, i)).ToArray();
        Assert.False(scene.MoveAttachment(1, Node(scene.PoseOf(1)!, 0) + Vector3.UnitX * 50)); Assert.False(scene.IsAttached(1));
        for (int i = 0; i < 9; i++) Assert.Equal(before[i], Node(scene.PoseOf(1)!, i));
        scene.Step(1f / 30, Floor, 801);
        Assert.All(scene.PoseOf(1)!.Transforms, matrix => Assert.True(float.IsFinite(matrix.Translation.LengthSquared())));
    }

    [Fact]
    public void A_crowd_is_limited_to_thirty_two_physical_pieces_and_alt_labels_are_immutable_and_expire_with_the_snapshot()
    {
        using var data = new AnatomyFixture();
        using var bowel = Ready(data.Library);
        for (int i = 0; i < 24; i++)
        {
            int index = i;
            bowel.Begin(new(i + 1, 4), data.Appearance, data.Rig, 0, 0,
                half => new(new(index * 5 + half, 8, 0), Vector3.UnitY), Vector2.UnitX, 0x800000, Floor);
        }
        bowel.Advance(0, (id, half) => new(new((id.Slot - 1) * 5 + half, 8, 0), Vector3.UnitY), _ => true, Floor, 0);
        Assert.Equal(BowelScene.Capacity, bowel.Count); Assert.Equal(BowelScene.Capacity, bowel.Rendered.Count);
        var labels = new BowelLabelSystem(); labels.Publish(12, bowel.Labels);
        Assert.Equal(BowelScene.Capacity, labels.LabelsAt(12).Count); Assert.Empty(labels.LabelsAt(11)); Assert.Empty(labels.LabelsAt(15));
        bowel.Clear(); Assert.Equal(0, bowel.Count); Assert.Empty(bowel.Rendered); Assert.Equal(BowelScene.Capacity, labels.LabelsAt(12).Count);
    }

    [Fact]
    public void Missing_bowel_anatomy_or_missing_cut_contacts_never_spawn_a_placeholder_organ_in_midair()
    {
        using var data = new AnatomyFixture(includeOrgan: false);
        using var bowel = Ready(data.Library);
        Assert.False(bowel.Begin(new(1, 0), data.Appearance, data.Rig, 0, 0, _ => new(new(0, 8, 0), Vector3.UnitY), Vector2.UnitX, 0x800000, Floor));
        Assert.Equal(0, bowel.Count);
        using var supplied = new AnatomyFixture();
        using var hidden = Ready(supplied.Library);
        Assert.False(hidden.Begin(new(1, 0), supplied.Appearance, supplied.Rig, 0, 0, _ => null, Vector2.UnitX, 0x800000, Floor));
        Assert.Equal(0, hidden.Count);
    }

    [Fact]
    public void An_ordinary_deep_abdomen_spills_one_complete_imported_organ_at_the_posed_skin_and_never_replays_it()
    {
        using var data = new AnatomyFixture(raceName: "Greenlander");
        using var bowel = Ready(data.Library);
        var contact = new CutFluidContact(new(12, 8, -4), Vector3.UnitZ);
        var owner = Owner(keep: true);
        Assert.True(bowel.BeginWound(owner, data.Appearance, data.Rig, 2, 2, contact, 0x800000, Floor));
        bowel.Advance(2, (_, _) => throw new InvalidOperationException("Loose ordinary spills have no attachment."), _ => true, Floor, 60);
        var piece = Assert.Single(bowel.Rendered);
        Assert.Equal(36, piece.Vertices.Length); Assert.Equal(0, bowel.Attached);
        Assert.InRange(Vector3.Distance(piece.Centre, contact.Position), 0, .11f);
        Assert.InRange(piece.Vertices.Max(v => v.Position.X) - piece.Vertices.Min(v => v.Position.X), 2.59f, 2.61f);
        Assert.False(bowel.BeginWound(owner, data.Appearance, data.Rig, 2, 2, contact, 0x800000, Floor));
        Assert.False(bowel.Begin(owner, data.Appearance, data.Rig, 2, 2, _ => contact, Vector2.UnitX, 0x800000, Floor));
        Assert.Equal(100, Assert.Single(bowel.Labels).Piece);
        using var delayed = Ready(data.Library);
        Assert.True(delayed.BeginWound(new(50, 2), data.Appearance, data.Rig, 2, 4, contact, 0x800000, Floor));
        delayed.Advance(4, (_, _) => null, _ => true, Floor, 120);
        Assert.Equal(0, delayed.Moving);
        Assert.All(Assert.Single(delayed.Rendered).Vertices, vertex => Assert.InRange(vertex.Position.Y + delayed.Rendered[0].Centre.Y, -.001f, .8f));
    }

    [Fact]
    public void The_deep_abdomen_trigger_accepts_saved_old_wounds_and_does_not_treat_chest_or_crush_marks_as_spills()
    {
        var abdomen = new Wound(9, .3f, Vector3.UnitZ, new(1, 0, .2f, .5f, WoundKind.Slash), 1, 3, 7, 1.8f, 50);
        var chest = abdomen with { T = .8f, Age = 8, Born = 60 };
        var state = new BrutalityActorView(new(1, 0), 0, 0, 0, default, null, null, [])
            { Wounds = new(Array.AsReadOnly(new[] { new BodyWoundEntry(-1, chest), new BodyWoundEntry(-1, abdomen) }), 10) };
        Assert.True(EffectsBridge.HasDeepAbdomen(state));
        Assert.False(EffectsBridge.HasDeepAbdomen(state with { Wounds = state.Wounds with { BellyHits = 9 } }));
        Assert.False(EffectsBridge.HasDeepAbdomen(state with { Wounds = new([new(-1, chest)], 10) }));
        Assert.False(EffectsBridge.HasDeepAbdomen(state with { Wounds = new([new(-1, abdomen with { Shape = abdomen.Shape with { Kind = WoundKind.Crush } })], 10) }));
        using var absent = new AnatomyFixture(includeOrgan: false, raceName: "Greenlander"); using var bowel = Ready(absent.Library);
        Assert.False(bowel.BeginWound(new(1, 0), absent.Appearance, absent.Rig, 0, 0, new(new(0, 8, 0), Vector3.UnitZ), 0x800000, Floor));
        using var otherRace = new AnatomyFixture(raceName: "Hive Prince"); using var hive = Ready(otherRace.Library);
        Assert.False(hive.BeginWound(new(1, 0), otherRace.Appearance, otherRace.Rig, 0, 0, new(new(0, 8, 0), Vector3.UnitZ), 0x800000, Floor));
    }

    static Vector3 Node(BowelPose pose, int index) => pose.Centre + Vector3.Transform(pose.RestNodes[index], pose.Transforms[index]);
    static CharacterId Owner(bool keep) => Enumerable.Range(1, 100).Select(slot => new CharacterId(slot, 3)).First(id => GutEmergence.KeepAttached(id) == keep);

    sealed class AnatomyFixture : IDisposable
    {
        readonly string folder = Directory.CreateTempSubdirectory("meitou-brutality-bowel-").FullName;
        public readonly AnatomyLibrary Library;
        public readonly CharacterAppearance Appearance;
        public readonly SkeletonRig Rig;
        public AnatomyFixture(bool includeOrgan = true, string raceName = "race")
        {
            if (includeOrgan)
            {
                string path = Path.Combine(folder, "assets", "anatomy", "Ecorche.anatomy"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var intestine = Box(new(-1.3f, -.8f, -.3f), new(1.3f, .8f, .3f), 55);
                var unrelated = Box(new(100, 100, 100), new(102, 102, 102), 15);
                using var writer = new BinaryWriter(File.Create(path)); writer.Write(Encoding.ASCII.GetBytes("ECR1")); writer.Write(1);
                var bone = Encoding.ASCII.GetBytes("Bip01 Spine1"); writer.Write(bone.Length); writer.Write(4); writer.Write(intestine.Length + unrelated.Length); writer.Write(bone);
                foreach (var v in intestine.Concat(unrelated))
                {
                    writer.Write(v.Position.X); writer.Write(v.Position.Y); writer.Write(v.Position.Z);
                    writer.Write(v.Normal.X); writer.Write(v.Normal.Y); writer.Write(v.Normal.Z); writer.Write(v.Uv.X); writer.Write(v.Uv.Y);
                }
            }
            Library = new(new AnatomyAssets(folder));
            var file = new FcsFile(); file.Records.Add(new() { StringId = "race", Name = raceName, Flags = 0x10, RecordType = FcsRecordType.RACE });
            var db = new GameDatabase(); db.Apply(file, "test.mod"); Appearance = new() { Race = db.Find("race")! };
            var skeleton = new OgreSkeleton(); skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01 Pelvis", Position = new(0, 10, 0) });
            skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 Neck", Position = new(0, 6.1649f, 0) }); Rig = new(skeleton);
        }
        public void Dispose() => Directory.Delete(folder, recursive: true);
    }
    static AnatomyVertex[] Box(Vector3 min, Vector3 max, float organ)
    {
        var result = new List<AnatomyVertex>();
        for (int axis = 0; axis < 3; axis++)
            foreach (int side in new[] { 0, 1 })
            {
                int x = (axis + 1) % 3, y = (axis + 2) % 3;
                Vector3 P(bool a, bool b) { var p = min; p[axis] = side == 0 ? min[axis] : max[axis]; p[x] = a ? max[x] : min[x]; p[y] = b ? max[y] : min[y]; return p; }
                var a = P(false, false); var b = P(true, false); var c = P(true, true); var d = P(false, true); if (side == 0) (b, d) = (d, b);
                var normal = Vector3.Zero; normal[axis] = side == 0 ? -1 : 1;
                foreach (var p in new[] { a, b, c, a, c, d }) result.Add(new(p, normal, new(organ, 0)));
            }
        return result.ToArray();
    }
}
