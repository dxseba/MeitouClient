using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Data.Textures;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Tests.Combat;
using static Meitou.Tests.Combat.CombatFixtures;

namespace Meitou.Tests.Modding;

public sealed class BrutalityWeaponBloodGeometryTests
{
    static (CharacterAppearance Look, CharacterPart Blade) Look(WeaponShown shown = WeaponShown.Drawn)
    {
        var db = new GameDatabase(); var file = new FcsFile();
        var race = new FcsRecord { StringId = "race", RecordType = FcsRecordType.RACE, Flags = 0x10 };
        var weapon = new FcsRecord { StringId = "blade", RecordType = FcsRecordType.WEAPON, Flags = 0x10 };
        weapon.Filenames["bare sword"] = "blade.mesh"; file.Records.AddRange([race, weapon]); db.Apply(file, "test.mod");
        var appearance = new CharacterAppearance { Race = db.Find("race")! };
        var blade = new CharacterPart { Record = db.Find("blade")!, Mesh = "blade.mesh", Field = "bare sword", Mode = AttachMode.Bone, Point = "hands", Shown = shown };
        appearance.Parts.Add(blade); return (appearance, blade);
    }
    static ItemInstance Item(string section = "hip") => new() { Type = FcsRecordType.WEAPON, Record = "blade", Section = section };
    static OgreMesh Mesh()
    {
        Vertex Point(Vector3 at) => new() { Position = at, Normal = Vector3.UnitZ, Uv = new(at.X, at.Y / 100), Tangent = new(1, 0, 0, 1),
            Colour = Vector4.One, Weights = Vector4.UnitX, Bones = 3 };
        var mesh = MeshCut.FromTriangles([Point(new(0, 0, 0)), Point(new(0, 100, 0)), Point(new(1, 90, 0))], "rig.skeleton");
        var pose = new OgrePose { Name = "bend", Target = 1 }; pose.Vertices.Add(new(1, new(.2f, 0, 0), default)); mesh.Poses.Add(pose);
        return mesh;
    }
    static Vector4[] Colours(OgreMesh mesh)
    {
        var data = mesh.SubMeshes[0].VertexData!; var element = data.Find(OgreVertexSemantic.Diffuse)!; var values = data.ReadFloats(element);
        return Enumerable.Range(0, (int)data.VertexCount).Select(i => new Vector4(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3]) / 255).ToArray();
    }
    [Fact]
    public void Blade_tint_leaves_the_hilt_clean_and_preserves_uvs_weights_morphs_and_original_buffers()
    {
        var (_, blade) = Look(); var original = Mesh(); var source = original.SubMeshes[0].VertexData!;
        var oldBytes = source.Buffers[0].Data.ToArray(); var helper = new WeaponBloodGeometry();
        var painted = helper.Paint(original, new(Item(), blade, new(1, 0, 0, 1), 1)); var target = painted.SubMeshes[0].VertexData!;
        Assert.NotSame(original, painted); Assert.Equal(oldBytes, source.Buffers[0].Data);
        Assert.Equal(Vector4.One, Colours(painted)[0]); Assert.Equal(new(1, 0, 0, 1), Colours(painted)[1]);
        foreach (var semantic in new[] { OgreVertexSemantic.Position, OgreVertexSemantic.Normal, OgreVertexSemantic.TextureCoordinates, OgreVertexSemantic.Tangent })
            Assert.Equal(source.ReadFloats(source.Find(semantic)!), target.ReadFloats(target.Find(semantic)!));
        Assert.Equal(original.SubMeshes[0].BoneAssignments, painted.SubMeshes[0].BoneAssignments);
        Assert.Same(original.SubMeshes[0].Indices, painted.SubMeshes[0].Indices); Assert.Same(Assert.Single(original.Poses), Assert.Single(painted.Poses));
        Assert.Equal(original.SkeletonName, painted.SkeletonName); Assert.Equal(original.Bounds, painted.Bounds);
    }
    [Fact]
    public void Native_splatter_alpha_controls_coverage_and_identical_revision_reuses_the_mesh()
    {
        var (_, blade) = Look(); var original = Mesh(); var item = Item();
        var clear = new WeaponBloodGeometry(new RgbaImage(1, 1, [255, 255, 255, 0]));
        var blood = new WeaponStainAttachment(item, blade, new(0, 1, 0, 1), 11);
        Assert.All(Colours(clear.Paint(original, blood)), colour => Assert.Equal(Vector4.One, colour));
        var opaque = new WeaponBloodGeometry(new RgbaImage(1, 1, [255, 255, 255, 255]));
        var first = opaque.Paint(original, blood); Assert.Equal(new(0, 1, 0, 1), Colours(first)[1]);
        Assert.Same(first, opaque.Paint(original, blood));
        Assert.Same(first, opaque.Paint(first, blood)); // Calling the bridge twice cannot multiply the stain again.
    }
    [Fact]
    public void An_unwounded_character_gets_a_cached_geometry_variant_and_only_the_exact_part_is_painted()
    {
        var (appearance, blade) = Look(); var spare = blade with { Point = "back", Shown = WeaponShown.BackDrawn };
        appearance.Parts.Add(spare); var original = Mesh(); var helper = new WeaponBloodGeometry();
        IReadOnlyList<WeaponStainAttachment> stains = Array.AsReadOnly(new[] { new WeaponStainAttachment(Item(), blade, new(1, 0, 0, .8f), 7) });
        var geometry = helper.Apply(appearance, null, stains, _ => original)!;
        Assert.Null(geometry.BodyMesh); Assert.Equal(2, geometry.Parts!.Count);
        Assert.True(geometry.Parts[0].VertexColours); Assert.NotNull(geometry.Parts[0].Mesh);
        Assert.False(geometry.Parts[1].VertexColours); Assert.Null(geometry.Parts[1].Mesh);
        Assert.Same(geometry, helper.Apply(appearance, null, stains, _ => throw new Exception("cached mesh was loaded again")));
        Assert.Same(geometry, helper.Apply(appearance, geometry, stains, _ => original));
    }
    [Fact]
    public void Distinguishable_native_sets_keep_identical_items_separate_and_render_never_reads_live_inventory()
    {
        var (appearance, blade) = Look(); var backPart = blade with { Shown = WeaponShown.BackDrawn }; appearance.Parts.Add(backPart);
        var hip = Item(); var back = Item("back"); var cold = new Combatant().MakeCold(); cold.Appearance = appearance;
        cold.Inventory = new(); cold.Inventory.Items.AddRange([hip, back]);
        cold.Fighter = new(new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = hip }, [])
        { BackWeapon = new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = back } };
        var state = new WeaponBloodState();
        using var world = new Meitou.Simulation.World(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [state]);
        var id = world.Characters.Spawn(new(), cold, 0);
        state.Deposit(hip, new() { BloodColour = 0xFF0000 }, new(25, 0, 0, 1), true, true);
        state.Deposit(back, new() { BloodColour = 0x00FF00 }, new(50, 0, 0, 1), true, true);
        state.SlowWorld(world); var snapshot = state.FrameAt(1)!.Characters[id];
        Assert.Same(hip, snapshot.Parts[0].Item); Assert.Same(back, snapshot.Parts[1].Item);
        Assert.Equal(new(1, 0, 0, .25f), snapshot.Parts[0].Blood); Assert.Equal(new(0, 1, 0, .5f), snapshot.Parts[1].Blood);
        cold.Inventory.Items.Clear(); cold.Appearance = new() { Race = appearance.Race };
        Assert.Same(appearance, snapshot.Appearance); Assert.Equal(2, snapshot.Parts.Count);
    }
    [Fact]
    public void Ambiguous_Always_parts_are_reported_without_colouring_a_duplicate_item()
    {
        var (appearance, blade) = Look(WeaponShown.Always); var a = Item(); var b = Item();
        var cold = new Combatant().MakeCold(); cold.Appearance = appearance; cold.Inventory = new(); cold.Inventory.Items.AddRange([a, b]);
        cold.Fighter = new(new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = a }, []);
        var state = new WeaponBloodState(); using var world = new Meitou.Simulation.World(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [state]);
        var id = world.Characters.Spawn(new(), cold, 0); state.Deposit(a, new(), new(25, 0, 0, 1), true, true); state.SlowWorld(world);
        var snapshot = state.FrameAt(1)!.Characters[id]; Assert.Empty(snapshot.Parts); Assert.Equal(WeaponBloodPresentationGap.AmbiguousAttachment, snapshot.Gaps);
        Assert.Same(blade, Assert.Single(snapshot.Appearance.Parts));
    }
}
