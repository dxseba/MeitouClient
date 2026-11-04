using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Rendering;
using SimWorld = Meitou.Simulation.World;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Modding;

public sealed class BrutalityBodyWoundTests
{
    static WoundInput Input(float cutting = 5, int category = -1) => new(2, WoundAttack.Front, false, true, cutting, 0, 0, 30, WeaponCategory: category);

    [Fact]
    public void Nearby_hits_deepen_one_mark_keep_its_axis_and_birth_and_obey_reference_caps()
    {
        var body = new WoundBody { Now = 41 };
        Assert.True(body.ApplyLimb(9, .5f, Vector3.UnitZ, .3f, Input()));
        var first = Assert.Single(body.Snapshot().Entries).Mark;
        Assert.Same(body.Snapshot(), body.Snapshot());
        for (int i = 0; i < 30; i++)
        {
            body.Now = 100 + i;
            Assert.True(body.ApplyLimb(9, .52f, Vector3.UnitZ, -.7f, Input() with { CutDirection = 1 }));
        }
        var merged = Assert.Single(body.Snapshot().Entries).Mark;
        Assert.Equal(first.Shape.Cosine, merged.Shape.Cosine);
        Assert.Equal(first.Shape.Sine, merged.Shape.Sine);
        Assert.Equal(first.T, merged.T);
        Assert.Equal(41, merged.Born);
        Assert.Equal(4, merged.Dose);
        Assert.Equal(8, merged.Hits);
        Assert.Equal(31, merged.Age);
        Assert.True(body.Heal(.01f));
        var healing = Assert.Single(body.Snapshot().Entries).Mark;
        Assert.Equal(3.99f, healing.Dose, 5);
        Assert.Equal(1, healing.Shape.Depth);
        Assert.False(body.Heal(4));
        Assert.Empty(body.Snapshot().Entries);
    }

    [Fact]
    public void The_ninety_seventh_distinct_mark_replaces_the_oldest_instead_of_growing_the_save_or_geometry_budget()
    {
        var body = new WoundBody();
        for (int i = 0; i < 97; i++)
        {
            float angle = i / 10 * .6f;
            Assert.True(body.ApplyLimb(i % 10, .5f, new(MathF.Sin(angle), 0, MathF.Cos(angle)), 0, Input()));
        }
        var wounds = body.Snapshot();
        Assert.Equal(96, wounds.Entries.Count);
        Assert.DoesNotContain(wounds.Entries, entry => entry.Mark.Age == 1);
        Assert.Contains(wounds.Entries, entry => entry.Mark.Age == 97);
        Assert.True(wounds.IsValid);
    }

    [Fact]
    public void Delivered_melee_damage_opens_wounds_at_level_zero_and_heals_once_each_three_thousand_ticks()
    {
        var system = new BrutalityGameplaySystem(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        using var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [system]);
        var source = new Combatant().MakeCold("source"); source.IsPlayer = true; source.Faction = 1;
        var victim = new Combatant().MakeCold("victim"); victim.Faction = 2;
        var attacker = world.Characters.Spawn(new(), source, 0);
        var target = world.Characters.Spawn(new(), victim, 0);
        system.Schedule(world);
        var hit = new CombatHit(0, CombatHitSource.Melee, attacker, target, 1, 0, LArm, new(6, 2, 0), default,
            Vector3.Zero, -Vector3.UnitZ, 0, 2, false, true, true, false);
        var bridge = system.CaptureBeforeHit(world, attacker, target, LArm, new(100, 0, 0, 0, 1, 0), CombatWoundMechanism.Slash, Context())
            with { ChallengeFactor = 0, WeaponCategory = 0 };
        system.RecordHitBridge(GoreHitKey.From(hit), bridge);
        system.HitResolved(world, hit); system.SlowWorld(world);
        var state = Assert.Single(system.ActorViews, view => view.Character == target);
        var mark = Assert.Single(state.Wounds.Entries).Mark;
        Assert.Equal(0, system.LevelOf(world, attacker));
        Assert.Equal(7 / (victim.Medical!.Parts[LArm].MaxHp * .3f), mark.Dose, 6);
        Assert.Equal(.12f, mark.Shape.Aspect);
        Assert.Equal(0, mark.Shape.WeaponCategory);
        Assert.Empty(state.Limbs);
        Assert.Null(system.WoundFrameAt(0));
        Assert.NotNull(system.WoundFrameAt(1));
        world.RunTicks(3000);
        system.SlowWorld(world);
        var healed = Assert.Single(Assert.Single(system.ActorViews, view => view.Character == target).Wounds.Entries).Mark;
        Assert.Equal(mark.Dose - .01f, healed.Dose, 6);
        system.SlowWorld(world);
        Assert.Equal(healed, Assert.Single(Assert.Single(system.ActorViews, view => view.Character == target).Wounds.Entries).Mark);
    }

    [Fact]
    public void Projectile_and_fully_absorbed_packets_do_not_open_a_melee_wound()
    {
        var body = new WoundBody();
        var hit = new CombatHit(1, CombatHitSource.Projectile, default, new(2, 0), 1, -1, LArm, new(30, 0, 0), default,
            Vector3.Zero, Vector3.UnitZ, 0, -1, false, true, true, false);
        Assert.False(body.Apply(hit, LeftArm, 100, CombatWoundMechanism.Thrust, 0));
        Assert.False(body.Apply(hit with { Source = CombatHitSource.Melee, CutDirection = 2, Damage = default }, LeftArm, 100, CombatWoundMechanism.Slash, 0));
        Assert.Empty(body.Snapshot().Entries);
    }

    [Fact]
    public void Ordinary_wounds_and_pending_belly_progress_round_trip_without_sharing_mutable_source_arrays()
    {
        var body = new WoundBody { Now = 73 };
        body.ApplyLimb(9, .51f, Vector3.UnitZ, .3f, Input(7, 3)); body.BellyHits = 10;
        var state = body.Snapshot();
        var saved = new BrutalityDurableState("pending/npc/full-key", 12, new(0), null, null, []) { Wounds = state };
        using var stream = new MemoryStream(); BrutalityStateCodec.Write(stream, [saved]); stream.Position = 0;
        var loaded = Assert.Single(BrutalityStateCodec.Read(stream));
        Assert.Equal(state.Entries, loaded.Wounds.Entries);
        Assert.Equal(state.StateKey(), loaded.Wounds.StateKey());
        var restored = WoundBody.From(loaded.Wounds);
        Assert.True(restored.DeepAbdomen);
        restored.Heal(.01f);
        Assert.NotEqual(state.StateKey(), restored.Snapshot().StateKey());
        Assert.Equal(state.StateKey(), loaded.Wounds.StateKey());
        var invalid = state with { Entries = [new(-1, state.Entries[0].Mark with { Side = new(float.NaN, 0, 1) })] };
        Assert.False(invalid.IsValid);
        Assert.False(BrutalityStateCodec.Valid(saved with { Wounds = invalid }));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Legacy_generic_rows_resolve_full_save_handles_and_keep_optional_kind_and_weapon_category(int fields)
    {
        string folder = Directory.CreateTempSubdirectory("brutality-ordinary-legacy-").FullName;
        try
        {
            var builder = new SaveBuilder();
            var platoon = builder.AddPlatoon("test", "Testfolk", "20-t", "10-t", Vector3.Zero);
            var source = builder.AddCharacter(platoon, "2-t", "same name", "20-t", Vector3.Zero, Quaternion.Identity);
            builder.Game.UpdateDerived();
            string row = $"{source.Handle.C} {source.Handle.I} -1 9 .55 0 0 1 1 0 .2 .5 .6 2 4 1.2 33"
                + (fields >= 18 ? " 1" : "") + (fields == 19 ? " 4" : "");
            File.WriteAllLines(Path.Combine(folder, "wounds.meitou"), ["bad row", row]);
            LegacyGoreSave.Loaded(SaveFolder.Open(folder), builder.Game);
            var loaded = Assert.Single(LegacyGoreSave.Read(builder.Game));
            Assert.Equal(SaveIdentityAdapter.KeyFor(source.Handle), loaded.Key);
            var mark = Assert.Single(loaded.Wounds.Entries).Mark;
            Assert.Equal(fields >= 18 ? WoundKind.Slash : WoundKind.None, mark.Shape.Kind);
            Assert.Equal(fields == 19 ? 4 : -1, mark.Shape.WeaponCategory);
            Assert.Equal(33, mark.Born); Assert.Equal(.6f, mark.Dose); Assert.Equal(1.2f, mark.Size);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_surface_wound_and_its_clothing_open_only_the_struck_side_and_keep_a_finite_local_relief(int side)
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01 Pelvis", Position = new(0, 10, 0) });
        skeleton.Bones.Add(new() { Handle = 1, Name = "Bip01 Spine1", Position = new(0, 3, 0), Parent = 0 });
        skeleton.Bones.Add(new() { Handle = 2, Name = "Bip01 Neck", Position = new(0, 3, 0), Parent = 1 });
        var rig = new SkeletonRig(skeleton);
        var db = new GameDatabase(); var file = new FcsFile();
        file.Records.Add(new FcsRecord { StringId = "race", Name = "human", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        db.Apply(file, "test.mod");
        var look = new CharacterAppearance { Race = db.Find("race")! };
        var body = MeshCut.FromTriangles(Faces(1));
        var wound = new Wound(9, .5f, new(0, 0, side), new(1, 0, .2f, .7f, WoundKind.Slash), .84f, 2, 1, 1.2f, 3);
        var plan = BodyWoundGeometry.Build(body, rig, look, new([new(-1, wound)], BellyHits: 10));
        Assert.NotEmpty(plan.Skin); Assert.NotEmpty(plan.Clothing); Assert.NotNull(plan.Relief);
        Assert.Equal(3, plan.AbdomenAnchor.Count);
        Assert.All(plan.AbdomenAnchor, vertex => Assert.Equal(side, vertex.Position.Z, .0001f));
        var clipped = MeshCut.Clip(body, rig, [], removeVolumes: plan.Skin).Skin;
        Assert.True(Area(clipped, side) < Area(body, side) - .001f);
        Assert.Equal(Posterior(body), Posterior(clipped));
        var clothing = MeshCut.FromTriangles(Faces(1.08f));
        var torn = MeshCut.Clip(clothing, rig, [], removeVolumes: plan.Clothing).Skin;
        Assert.True(Area(torn, side) < Area(clothing, side) - .001f);
        Assert.Equal(Posterior(clothing), Posterior(torn));
        Assert.All(Vertices(plan.Relief!), v =>
        {
            Assert.True(float.IsFinite(v.Position.LengthSquared()));
            Assert.InRange(v.Position.Z * side, .54f, 1.01f);
            Assert.InRange(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W, .9999f, 1.0001f);
        });
        var bruise = BodyWoundGeometry.Build(body, rig, look, new([new(-1, wound with { Shape = wound.Shape with { Kind = WoundKind.Crush, Aspect = .85f } })]));
        Assert.Empty(bruise.Skin); Assert.Empty(bruise.Clothing);
        Assert.NotNull(bruise.Relief);
        Assert.All(Vertices(bruise.Relief!), vertex => Assert.True(vertex.Position.Z * side > 1));
        Vector3[] Posterior(OgreMesh mesh) => Vertices(mesh).Where(v => v.Normal.Z * side < -.9f).Select(v => v.Position).ToArray();
        Vertex[] Vertices(OgreMesh mesh) => Model.Build(mesh, rig.BoneCount).Parts.SelectMany(p => p.Vertices).ToArray();
        float Area(OgreMesh mesh, int sign)
        {
            float area = 0;
            foreach (var part in Model.Build(mesh, rig.BoneCount).Parts)
                for (int i = 0; i < part.Indices.Length; i += 3)
                {
                    var a = part.Vertices[part.Indices[i]]; var b = part.Vertices[part.Indices[i + 1]]; var c = part.Vertices[part.Indices[i + 2]];
                    if (a.Normal.Z * sign > .9f) area += Vector3.Cross(b.Position - a.Position, c.Position - a.Position).Length() * .5f;
                }
            return area;
        }
        static Vertex[] Faces(float halfDepth)
        {
            var vertices = new List<Vertex>();
            foreach (int sign in new[] { 1, -1 })
            {
                Vector3[] corners = [new(-2, 10, sign * halfDepth), new(2, 10, sign * halfDepth), new(2, 16, sign * halfDepth), new(-2, 16, sign * halfDepth)];
                foreach (int i in sign == 1 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 })
                    vertices.Add(new() { Position = corners[i], Normal = new(0, 0, sign), Tangent = new(1, 0, 0, 1), Colour = Vector4.One, Bones = 1, Weights = Vector4.UnitX });
            }
            return vertices.ToArray();
        }
    }
}
