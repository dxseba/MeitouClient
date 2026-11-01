using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;

namespace Meitou.Tests.Physics;

public class RagdollLimbAppearanceTests
{
    [Fact]
    public void A_detached_limb_keeps_natural_forearm_and_hand_size_even_when_its_owners_loadout_has_stumps()
    {
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "natural-limb-race", Name = "Natural limb race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        var armour = new FcsRecord { StringId = "hide-stump-armour", Name = "Hide stump armour", RecordType = FcsRecordType.ARMOUR, Flags = 0x10 };
        armour.Ints["hide stump"] = (int)HideStump.BothArms;
        file.Records.Add(armour);
        var db = new GameDatabase();
        db.Apply(file, "natural-limb-test.mod");
        var loadout = new Loadout
        {
            Seed = 1, Race = db.Find("natural-limb-race")!, Female = false,
            Appearance = new() { Record = new() { RecordType = FcsRecordType.CHARACTER_APPEARANCE } },
        };
        loadout.Limbs[0] = new(1);
        loadout.Limbs[2] = new(1);
        var amputee = CharacterAppearance.Build(db, Path.GetTempPath(), "natural-limb-race", new() { Loadout = loadout, Naked = true });
        amputee.Parts.Add(new() { Record = db.Find("hide-stump-armour")!, Mesh = "synthetic-armour.mesh", Field = "mesh", Mode = AttachMode.SharedSkeleton, Slot = AttachSlot.Body });
        Assert.Null(amputee.Shape); // Exercise the real Loadout -> InputFor path, not a manually supplied shape override.
        var missing = CharacterShape.InputFor(db, amputee);
        Assert.Contains(Limb.LeftArm, missing.Missing);
        Assert.Contains(Limb.LeftLeg, missing.Missing);
        var detached = amputee.SeveredLimb(db);
        var natural = Assert.IsType<CharacterShapeInput>(detached.Shape);
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01" });
        skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 L Forearm", Position = new(0, 1, 0) });
        skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 L Hand", Position = new(0, -2, 0) });
        var rig = new SkeletonRig(skeleton);
        BodyShapeData Shape(CharacterShapeInput input)
        {
            var result = CharacterShape.Compute(input, name => rig.Handle(name) is not null);
            return rig.Shape(result.BoneSize, result.PositionalSize, result.MovementScale);
        }
        var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(Shape(missing), []);
            Assert.Equal(Vector3.Zero, scratch.DerivedScale[1]);
            Assert.Equal(Vector3.Zero, scratch.DerivedScale[2]);
            scratch.PoseDerived(Shape(natural), []);
            Assert.True(scratch.DerivedScale[1].Length() > 1);
            Assert.True(scratch.DerivedScale[2].Length() > 1);
            Assert.InRange(Vector3.Distance(scratch.DerivedPosition[1], scratch.DerivedPosition[2]), 1.9f, 2.1f);
        }
        finally { rig.Return(scratch); }
        Assert.Empty(natural.Missing);
        Assert.Equal(HideStump.None, natural.HideStump);
        Assert.False(natural.NarrowLeftThigh);
        Assert.False(natural.NarrowRightThigh);
        Assert.Same(loadout, amputee.Loadout);
        Assert.Equal(1, loadout.Limbs[0]!.State);
        Assert.Contains(Limb.LeftArm, CharacterShape.InputFor(db, amputee).Missing);
    }
}
