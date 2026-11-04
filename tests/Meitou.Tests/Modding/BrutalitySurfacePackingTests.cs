using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Brutality;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Simulation.Combat;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalitySurfacePackingTests
{
    static BrutalityActorView State(TorsoBisectionState? torso = null, params LimbCutState[] limbs) => new(new(2, 7), 0, 0, 0,
        new(4, SevereHeadWoundKind.BrokenSkull, 4, Mechanism: CombatWoundMechanism.Crush), null, torso, limbs);
    static CharacterAppearance Look(string name = "Human")
    {
        var database = new GameDatabase(); var file = new FcsFile();
        file.Records.Add(new FcsRecord { Name = name, StringId = "packing-race", RecordType = FcsRecordType.RACE });
        database.Apply(file, "packing.mod");
        return new() { Race = database.Find("packing-race")! };
    }
    static SkeletonRig Rig()
    {
        var skeleton = new OgreSkeleton();
        void Bone(string name, ushort? parent, Vector3 position) => skeleton.Bones.Add(new() { Handle = (ushort)skeleton.Bones.Count, Name = name, Parent = parent, Position = position });
        Bone("Bip01", null, new(0, 10, 0)); Bone("Bip01 Head", 0, new(0, 8, 0)); Bone("Bip01 Jaw", 1, new(0, -1, 1));
        Bone("Bip01 L UpperArm", 0, new(-3, 5, 0)); Bone("Bip01 L Forearm", 3, new(0, -3, 0)); Bone("Bip01 L Hand", 4, new(0, -3, 0));
        Bone("Bip01 R UpperArm", 0, new(3, 5, 0)); Bone("Bip01 R Forearm", 6, new(0, -3, 0)); Bone("Bip01 R Hand", 7, new(0, -3, 0));
        Bone("Bip01 L Thigh", 0, new(-1, -2, 0)); Bone("Bip01 L Calf", 9, new(0, -4, 0)); Bone("Bip01 L Foot", 10, new(0, -4, 0));
        Bone("Bip01 R Thigh", 0, new(1, -2, 0)); Bone("Bip01 R Calf", 12, new(0, -4, 0)); Bone("Bip01 R Foot", 13, new(0, -4, 0));
        Bone("Bip01 L Clavicle", 0, new(-1, 5, 0));
        while (skeleton.Bones.Count < 63) Bone("unused" + skeleton.Bones.Count, 0, Vector3.Zero);
        Bone("Bip01 L Finger", 5, new(0, -1, 0)); Bone("Bip01 R Finger", 8, new(0, -1, 0));
        return new(skeleton);
    }

    [Fact]
    public void Palette_offsets_preserve_the_native_header_and_truncate_only_after_64_wound_entries()
    {
        var spheres = Enumerable.Range(0, 65).Select(i => new Vector4(i + 1, i + 101, i + 201, i + 301)).ToArray();
        var cuts = Enumerable.Range(0, 65).Select(i => new Vector4(-i - 1, -i - 101, -i - 201, -i - 301)).ToArray();
        var head = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(7, 8, 9);
        var packed = NativeSurfaceBridge.Pack(Rig(), State(), 0, 0xFEDCBA98u, spheres, cuts, head);
        Assert.Equal(36, packed.Length);
        var words = MemoryMarshal.Cast<Matrix4x4, uint>(packed.AsSpan());
        Assert.Equal(0xFEDCBA98u, words[16]);
        Assert.Equal(State().SevereHead.RendererFlags, words[17]);
        Assert.Equal(0u, words[18]); Assert.Equal(0u, words[19]); Assert.Equal(0u, words[28]);
        Assert.Equal(head, packed[2]); Assert.Equal(default, packed[3]);
        var vectors = MemoryMarshal.Cast<Matrix4x4, Vector4>(packed.AsSpan());
        for (int entry = 0; entry < 64; entry++) { Assert.Equal(spheres[entry], vectors[16 + entry]); Assert.Equal(cuts[entry], vectors[80 + entry]); }
        spheres[0] = cuts[0] = new(999);
        Assert.Equal(new Vector4(1, 101, 201, 301), vectors[16]); Assert.Equal(new Vector4(-1, -101, -201, -301), vectors[80]);
        uint Group(int bone) => MemoryMarshal.Cast<Matrix4x4, uint>(packed.AsSpan())[20 + bone / 8] >> (bone % 8 * 4) & 15;
        Assert.Equal(0u, Group(0)); Assert.Equal(6u, Group(2)); Assert.Equal(1u, Group(3)); Assert.Equal(2u, Group(6));
        Assert.Equal(3u, Group(9)); Assert.Equal(4u, Group(12)); Assert.Equal(0u, Group(15)); Assert.Equal(1u, Group(63));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Each_detached_limb_uses_its_own_exact_cut_plane_and_distal_side(int slot)
    {
        var rig = Rig(); var cut = new LimbCutState(slot, .4f, new(.3f, -.2f));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var expected));
        var packed = NativeSurfaceBridge.Pack(rig, State(null, cut), 10 + slot, 0, null, null, default);
        var words = MemoryMarshal.Cast<Matrix4x4, uint>(packed.AsSpan());
        Assert.Equal((1u << slot), words[28]); Assert.NotEqual(0u, words[17] & 1u << (slot + 1));
        Assert.Equal(((uint)slot + 1) << 8 | 255u, words[19]);
        Assert.Equal(expected, MemoryMarshal.Cast<Matrix4x4, Vector4>(packed.AsSpan(0, 1))[slot]);
        // Shader PartOnly chooses the negative/distal half of this shared proximal-positive physical plane.
        var prefix = slot < 2 ? "Arm" : "Leg";
        string side = slot % 2 == 0 ? "L" : "R";
        int proximal = rig.Handle($"Bip01 {side} {(prefix == "Arm" ? "UpperArm" : "Thigh")}")!.Value;
        int distal = rig.Handle($"Bip01 {side} {(prefix == "Arm" ? "Hand" : "Foot")}")!.Value;
        Assert.True(Vector4.Dot(expected, new(rig.BindPosition[proximal], 1)) > 0);
        Assert.True(Vector4.Dot(expected, new(rig.BindPosition[distal], 1)) < 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Only_the_two_torso_halves_carry_the_owner_torso_plane(bool fullBody)
    {
        var cut = new TorsoBisectionState(new(Vector3.Normalize(new(.3f, .9f, .1f)), -12), fullBody);
        var state = State(cut); var rig = Rig();
        foreach (int piece in new[] { 0, 1 })
        {
            var frame = NativeSurfaceBridge.Pack(rig, state, piece, 0, null, null, default)[3];
            Assert.Equal(new Vector4(cut.Plane.X, cut.Plane.Y, cut.Plane.Z, cut.Plane.W), new(frame.M11, frame.M12, frame.M13, frame.M14));
            Assert.Equal(piece == 0 ? 1 : -1, frame.M21); Assert.Equal(2.8f, frame.M22); Assert.Equal(fullBody ? 1 : 0, frame.M23);
        }
        foreach (int piece in new[] { 2, 3, 10, 11, 12, 13 })
            Assert.Equal(default, NativeSurfaceBridge.Pack(rig, state, piece, 0, null, null, default)[3]);
    }

    [Theory]
    [InlineData(HeadCutMode.AttachedSplit, false, 0, true)]
    [InlineData(HeadCutMode.AttachedSplit, true, 0, false)]
    [InlineData(HeadCutMode.AttachedSplit, true, 1, false)]
    [InlineData(HeadCutMode.NeckDetach, false, 0, false)]
    [InlineData(HeadCutMode.DetachedSplit, false, 0, false)]
    [InlineData(HeadCutMode.NeckDetach, false, 2, true)]
    [InlineData(HeadCutMode.DetachedSplit, true, 2, true)]
    [InlineData(HeadCutMode.DetachedSplit, true, 3, true)]
    [InlineData(HeadCutMode.AttachedSplit, false, 10, false)]
    [InlineData(HeadCutMode.DetachedSplit, true, 13, false)]
    public void Head_sections_belong_to_head_fragments_or_the_still_attached_half_without_replacing_a_torso_section(HeadCutMode mode, bool torso, int piece, bool expected)
    {
        var state = State(torso ? new TorsoBisectionState(new(0, 1, 0, -10)) : null) with { Head = new HeadCutState(Vector4.UnitX, mode) };
        Assert.Equal(expected, NativeSurfaceBridge.AppliesHeadPlane(state, piece));
        Assert.False(NativeSurfaceBridge.AppliesHeadPlane(state with { Head = null }, piece));
    }

    [Fact]
    public void Wound_projection_preserves_source_region_order_then_newest_limbs_and_race_size()
    {
        static Wound Mark(int segment, int age, int category, long born = 0) => new(segment, .3f, Vector3.UnitZ, new(1, 0, .2f, .8f, WoundKind.Slash, category), 12, 1, age, 2, born);
        // Pinned WoundMarks emits chest/abdomen/back in index order, then independent segment marks newest first.
        var state = new BodyWoundState([new(0, Mark(-1, 1, 0)), new(1, Mark(-1, 12, 1)), new(2, Mark(-1, 4, 2)),
            new(-1, Mark(0, 3, 3)), new(-1, Mark(8, 8, 8)), new(-1, Mark(9, 9, 9, 99))]);
        var marks = NativeSurfaceBridge.Marks(state, Look(), 100, 1);
        Assert.Equal(new[] { 0, 1, 2, 9, 8, 3 }, marks.Select(mark => mark.WeaponCategory));
        Assert.Equal("Bip01 Pelvis", marks[0].BoneA); Assert.True(marks[2].Side.Z < 0);
        var chest = marks.Single(mark => mark.WeaponCategory == 9);
        Assert.Equal("Bip01 Pelvis", chest.BoneA); Assert.Equal("Bip01 Neck", chest.BoneB); Assert.Equal(.3f, chest.T); Assert.Equal(.25f, chest.Flow);
        var head = marks.Single(mark => mark.WeaponCategory == 8);
        Assert.Equal("Bip01 Neck", head.BoneA); Assert.Equal("Bip01 Head", head.BoneB); Assert.Equal(2.5f, head.T); Assert.True(head.Side.X > 0);
        var arm = marks.Single(mark => mark.WeaponCategory == 3);
        Assert.Equal("Bip01 L UpperArm", arm.BoneA); Assert.Equal("Bip01 L Forearm", arm.BoneB);
        Assert.All(marks, mark => Assert.Equal(1, mark.Cosine * mark.Cosine + mark.Sine * mark.Sine, .0001f));
        var hive = NativeSurfaceBridge.Marks(state, Look("Hive prince"), 100, 1);
        for (int mark = 0; mark < marks.Count; mark++) Assert.Equal(marks[mark].Size * .55f, hive[mark].Size);
    }
}
