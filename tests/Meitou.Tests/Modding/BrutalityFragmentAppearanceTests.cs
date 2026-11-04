using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Physics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;

namespace Meitou.Tests.Modding;

public sealed class BrutalityFragmentAppearanceTests
{
    [Fact]
    public void A_second_cut_refreshes_the_same_native_look_and_restores_its_new_hand_without_regrowing_an_unrelated_lost_leg()
    {
        var database = new GameDatabase(); var file = new FcsFile();
        file.Records.Add(new FcsRecord { StringId = "repeat-cut-race", Name = "human", RecordType = FcsRecordType.RACE }); database.Apply(file, "test.mod");
        var native = new CharacterAppearance { Race = database.Find("repeat-cut-race")!, Shape = new()
        { Missing = new HashSet<Limb> { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg }, HideStump = HideStump.BothArms } };
        typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.Headless))!.SetValue(native, true);
        var skeleton = new OgreSkeleton();
        void Bone(string name, ushort? parent, Vector3 position) => skeleton.Bones.Add(new() { Handle = (ushort)skeleton.Bones.Count, Name = name, Parent = parent, Position = position });
        Bone("Bip01", null, new(0, 10, 0));
        Bone("Bip01 L UpperArm", 0, new(-3, 5, 0)); Bone("Bip01 L Forearm", 1, new(0, -3, 0)); Bone("Bip01 L Hand", 2, new(0, -3, 0));
        Bone("Bip01 R UpperArm", 0, new(3, 5, 0)); Bone("Bip01 R Forearm", 4, new(0, -3, 0)); Bone("Bip01 R Hand", 5, new(0, -3, 0));
        Bone("Bip01 L Thigh", 0, new(-1, -2, 0)); Bone("Bip01 L Calf", 7, new(0, -4, 0)); Bone("Bip01 L Foot", 8, new(0, -4, 0));
        var rig = new SkeletonRig(skeleton);
        Vector3[] Pose(CharacterAppearance appearance)
        {
            var shape = CharacterShape.Compute(appearance.Shape!, name => rig.Handle(name) is not null);
            var scratch = rig.Rent();
            try { scratch.PoseDerived(rig.Shape(shape.BoneSize, shape.PositionalSize, shape.MovementScale), []); return scratch.DerivedPosition.ToArray(); }
            finally { rig.Return(scratch); }
        }
        var first = new BrutalityActorView(new(2, 0), 0, 0, 0, new(0), null, null, [new(0, .4f, Vector2.Zero)]);
        var cache = new FragmentAppearanceCache();
        var left = cache.Get(database, native, native.Parts, first); var before = Pose(left);
        Assert.True(Vector3.Distance(before[2], before[3]) > 2.5f);
        Assert.InRange(Vector3.Distance(before[5], before[6]), 0, .0001f);
        Assert.InRange(Vector3.Distance(before[8], before[9]), 0, .0001f);
        Assert.Same(left, cache.Get(database, native, native.Parts, first with { Experience = 200 }));
        var second = first with { Limbs = [new(0, .4f, Vector2.Zero), new(1, .6f, new(.2f, 0))] };
        var both = cache.Get(database, native, native.Parts, second); var after = Pose(both);
        Assert.NotSame(left, both);
        Assert.True(Vector3.Distance(after[2], after[3]) > 2.5f); Assert.True(Vector3.Distance(after[5], after[6]) > 2.5f);
        Assert.InRange(Vector3.Distance(after[8], after[9]), 0, .0001f);
        Assert.Contains(Limb.LeftLeg, both.Shape!.Missing); Assert.Equal(HideStump.None, both.Shape.HideStump);
        Assert.Contains(Limb.RightArm, left.Shape!.Missing); Assert.Equal(3, native.Shape!.Missing.Count);
        Assert.True(both.Headless);
        var headCut = second with { Head = new(Vector4.UnitX, HeadCutMode.AttachedSplit) };
        var split = cache.Get(database, native, native.Parts, headCut);
        Assert.False(split.Headless); Assert.Contains(Limb.LeftLeg, split.Shape!.Missing);
        Assert.True(cache.Get(database, native, native.Parts, second).Headless);
    }
}
