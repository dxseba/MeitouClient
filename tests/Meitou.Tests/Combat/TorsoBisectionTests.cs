using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.DuelFixture;

namespace Meitou.Tests.Combat;

public class TorsoBisectionTests
{
    static readonly BodyPartTemplate CanonicalChest = Chest with { StringId = ArmourData.ChestPartId };
    static TorsoBisectionInput Eligible => new(CombatHitSource.Melee, CombatWoundMechanism.Slash, CanonicalChest, 2, 75, 100) { Brutality = 60, Lethal = true };

    [Fact]
    public void Only_powerful_lethal_hits_qualify_and_extra_damage_does_not_bypass_the_skill_chance()
    {
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Cut = 74.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 149.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 150 }));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 69.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 70 }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Cut = 112.5f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Cut = 150 }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Cut = 10000 }));
        Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.099f));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.10f));
    }

    [Fact]
    public void Armour_can_prevent_bisection_even_when_the_weapon_packet_is_huge()
    {
        var packet = new DamagePacket(500, 250, 250, 0, 1, 0);
        var exposed = HitResolver.Resolve(packet, ArmourEffect.None, 50, 1, CombatFixtures.Constants);
        var protectedHit = HitResolver.Resolve(packet, new ArmourEffect(0.99f, 0, 0, 0), 50, 1, CombatFixtures.Constants);
        Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = exposed.Cut }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = protectedHit.Cut }, 0));
        Assert.True(protectedHit.Blunt + protectedHit.Pierce > 70);
    }

    [Fact]
    public void The_mechanism_supported_cleave_axis_chest_and_living_humanoid_target_are_all_required()
    {
        TorsoBisectionInput[] excluded =
        [
            Eligible with { Source = CombatHitSource.Bolt }, Eligible with { Source = CombatHitSource.Execution },
            Eligible with { Mechanism = CombatWoundMechanism.Unspecified }, Eligible with { Mechanism = CombatWoundMechanism.Thrust },
            Eligible with { Mechanism = CombatWoundMechanism.Crush }, Eligible with { CutDirection = 0 },
            Eligible with { CutDirection = 4 }, Eligible with { CutDirection = 5 }, Eligible with { CutDirection = 6 },
            Eligible with { Part = Stomach }, Eligible with { Part = Head },
            Eligible with { Part = CanonicalChest with { Type = BodyPartType.Arm } },
            Eligible with { AliveBeforeHit = false }, Eligible with { AlreadyBisected = true },
            Eligible with { Animal = true },
            Eligible with { Lethal = false }, Eligible with { Brutality = 59.99f },
        ];
        foreach (var input in excluded) Assert.False(TorsoBisectionPolicy.ShouldBisect(input, 0));
        foreach (int direction in new[] { 1, 2, 3, 7, 9 })
            Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible with { CutDirection = direction }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { CutDirection = 1, Cut = 74.99f }, 0));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    public void Invalid_damage_health_and_rolls_do_not_open_a_cut(float invalid)
    {
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = invalid }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Maximum = invalid }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, invalid));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Maximum = 0 }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, 1));
    }

    [Fact]
    public void The_committed_plane_is_normalized_and_keeps_the_upper_half_positive()
    {
        foreach (int direction in new[] { 2, 3, 9 })
        {
            var plane = TorsoBisectionPolicy.PlaneForDirection(direction);
            Assert.True(new TorsoBisectionState(plane).IsValid);
            float height = -plane.W / plane.Y;
            Assert.InRange(height, TorsoBisectionPolicy.MinimumHeight, TorsoBisectionPolicy.MaximumHeight);
            Assert.InRange(Vector4.Dot(plane, new(0, height, 0, 1)), -0.00001f, 0.00001f);
            Assert.True(Vector4.Dot(plane, new(0, 14, 0, 1)) > 0);
            Assert.True(Vector4.Dot(plane, new(0, 11, 0, 1)) < 0);
        }
    }

    [Fact]
    public void Continuous_plane_variants_cover_distinct_heights_and_angles_inside_the_shared_limb_envelope()
    {
        var heights = new HashSet<float>();
        var normals = new HashSet<Vector3>();
        foreach (ulong seed in Enumerable.Range(0, 256).Select(i => (ulong)i))
            foreach (int direction in new[] { 2, 3, 9 })
                foreach (var travel in new[] { Vector2.UnitX, Vector2.UnitY, new Vector2(-0.7f, 0.4f) })
                {
                    var plane = TorsoBisectionPolicy.PlaneForDirection(direction, seed, travel, 0.7f);
                    Assert.Equal(plane, TorsoBisectionPolicy.PlaneForDirection(direction, seed, travel, 0.7f));
                    Assert.True(new TorsoBisectionState(plane).IsValid);
                    float height = -plane.W / plane.Y, sx = plane.X / plane.Y, sz = plane.Z / plane.Y;
                    Assert.InRange(height, TorsoBisectionPolicy.MinimumHeight - 0.00001f, TorsoBisectionPolicy.MaximumHeight + 0.00001f);
                    Assert.InRange(MathF.Abs(sx), 0, TorsoBisectionPolicy.MaximumSlopeX + 0.00001f);
                    Assert.InRange(MathF.Abs(sz), 0, TorsoBisectionPolicy.MaximumSlopeZ + 0.00001f);
                    float reach = TorsoBisectionPolicy.JunctionHalfWidth * MathF.Abs(sx) + TorsoBisectionPolicy.JunctionHalfDepth * MathF.Abs(sz);
                    Assert.True(height - reach >= TorsoBisectionPolicy.LegJunctionCeiling - 0.00002f);
                    Assert.True(height + reach <= TorsoBisectionPolicy.ShoulderJunctionFloor + 0.00002f);
                    Assert.True(plane.Y >= MathF.Cos(TorsoBisectionPolicy.MaximumTiltDegrees * MathF.PI / 180) - 0.00001f);
                    heights.Add(height);
                    normals.Add(new(plane.X, plane.Y, plane.Z));
                }
        Assert.True(heights.Count > 200);
        Assert.True(heights.Max() - heights.Min() > 1.8f);
        Assert.True(normals.Count > 1000);
        Assert.True(normals.Min(n => n.Y) < 0.96f);
    }

    [Fact]
    public void Incoming_direction_and_opposing_blade_axes_change_the_angle_in_victim_space()
    {
        var left = TorsoBisectionPolicy.PlaneForDirection(2, 1, new(0, -1));
        var right = TorsoBisectionPolicy.PlaneForDirection(3, 1, new(0, -1));
        Assert.Equal(left.W, right.W);
        Assert.Equal(left.Y, right.Y);
        Assert.Equal(left.X, -right.X);
        Assert.Equal(left.Z, -right.Z);
        Assert.True(Vector4.Distance(left, TorsoBisectionPolicy.PlaneForDirection(2, 1, Vector2.UnitX)) > 0.02f);
        float yaw = 1.1f;
        var rotatedWorldTravel = -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        Assert.InRange(Vector4.Distance(left, TorsoBisectionPolicy.PlaneForDirection(2, 1, rotatedWorldTravel, yaw)), 0, 0.00001f);
        Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(2, 1), TorsoBisectionPolicy.PlaneForDirection(2, 1, new(float.NaN, 1), float.NaN));
    }

    [Fact]
    public void Eligible_native_axes_choose_oblique_cleaves_alongside_their_existing_families()
    {
        bool vertical = false, diagonal = false, downwardOblique = false, sidewaysOblique = false, transverse = false;
        foreach (ulong seed in Enumerable.Range(0, 128).Select(i => (ulong)i))
        {
            foreach (int direction in new[] { 1, 7 })
            {
                var state = TorsoBisectionPolicy.StateForDirection(direction, seed, new(0, -1));
                Assert.True(state.FullBody);
                Assert.True(state.IsValid);
                vertical |= state.Plane.Y == 0;
                float tilt = MathF.Asin(MathF.Abs(state.Plane.Y)) * 180 / MathF.PI;
                diagonal |= tilt is >= 8 and <= 35.0001f;
                downwardOblique |= tilt >= TorsoBisectionPolicy.ObliqueMinimumTiltDegrees - 0.0001f;
            }
            foreach (int direction in new[] { 2, 3, 9 })
            {
                var state = TorsoBisectionPolicy.StateForDirection(direction, seed, Vector2.UnitX);
                var explicitTransverse = TorsoBisectionPolicy.StateForDirection(direction, seed, Vector2.UnitX, style: TorsoBisectionStyle.Transverse);
                Assert.False(explicitTransverse.FullBody);
                Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(direction, seed, Vector2.UnitX), explicitTransverse.Plane);
                if (state.FullBody)
                {
                    sidewaysOblique = true;
                    Assert.Equal(TorsoBisectionPolicy.StateForDirection(direction, seed, Vector2.UnitX, style: TorsoBisectionStyle.Oblique), state);
                }
                else
                {
                    transverse = true;
                    Assert.Equal(explicitTransverse, state);
                }
            }
        }
        Assert.True(vertical);
        Assert.True(diagonal);
        Assert.True(downwardOblique && sidewaysOblique && transverse);
        Assert.False(TorsoBisectionPolicy.StateForDirection(5, 1).IsValid);
        Assert.False(TorsoBisectionPolicy.StateForDirection(1, 1, style: TorsoBisectionStyle.Transverse).IsValid);
    }

    [Fact]
    public void Full_body_planes_follow_the_entire_local_approach_angle_and_ignore_world_rotation()
    {
        float largestJitter = 0;
        foreach (ulong seed in Enumerable.Range(0, 32).Select(i => (ulong)i))
        foreach (var style in new[] { TorsoBisectionStyle.Vertical, TorsoBisectionStyle.Diagonal, TorsoBisectionStyle.Oblique })
        {
            var baseline = TorsoBisectionPolicy.StateForDirection(1, seed, -Vector2.UnitY, style: style).Plane;
            foreach (int degrees in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
            {
                float angle = degrees * MathF.PI / 180, yaw = 0.7f;
                var travel = Rotate(-Vector2.UnitY, angle);
                var state = TorsoBisectionPolicy.StateForDirection(1, seed, travel, style: style);
                Assert.True(state.IsValid);
                Assert.True(state.FullBody);
                var p = state.Plane;
                var expected = Rotate(new(baseline.X, baseline.Z), angle);
                Assert.InRange(Vector2.Distance(expected, new(p.X, p.Z)), 0, 0.000002f);
                Assert.InRange(MathF.Abs(baseline.Y - p.Y), 0, 0.000002f);
                var horizontal = Vector2.Normalize(new(p.X, p.Z));
                float alignment = Vector2.Dot(horizontal, new(travel.Y, -travel.X));
                Assert.True(alignment >= MathF.Cos(TorsoBisectionPolicy.FullBodyAzimuthDegrees * MathF.PI / 180) - 0.000002f);
                largestJitter = MathF.Max(largestJitter, MathF.Acos(Math.Clamp(alignment, -1, 1)) * 180 / MathF.PI);
                var turnedWorld = TorsoBisectionPolicy.StateForDirection(1, seed, Rotate(travel, -yaw), yaw, style);
                Assert.InRange(Vector4.Distance(p, turnedWorld.Plane), 0, 0.00001f);
                Assert.Equal(state, TorsoBisectionPolicy.StateForDirection(7, seed, travel, style: style));
            }
            Assert.Equal(TorsoBisectionPolicy.StateForDirection(1, seed, style: style),
                TorsoBisectionPolicy.StateForDirection(1, seed, new(float.NaN, 1), float.NaN, style));
        }
        Assert.True(largestJitter > 18);

        static Vector2 Rotate(Vector2 value, float angle) => new(value.X * MathF.Cos(angle) - value.Y * MathF.Sin(angle),
            value.X * MathF.Sin(angle) + value.Y * MathF.Cos(angle));
    }

    [Fact]
    public void Vertical_offsets_and_both_diagonal_families_remain_inside_their_body_ellipses_and_pivot_bands()
    {
        bool longPositive = false, longNegative = false, obliquePositive = false, obliqueNegative = false;
        float minimumVerticalOffset = 1, maximumVerticalOffset = -1, minimumLongTilt = 90, maximumObliqueTilt = 0;
        foreach (ulong seed in Enumerable.Range(0, 128).Select(i => (ulong)i))
        foreach (var travel in new[] { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY, new Vector2(1, 1) })
        foreach (var style in new[] { TorsoBisectionStyle.Vertical, TorsoBisectionStyle.Diagonal })
        {
            var state = TorsoBisectionPolicy.StateForDirection(1, seed, travel, 0.4f, style);
            Assert.Equal(state, TorsoBisectionPolicy.StateForDirection(1, seed, travel, 0.4f, style));
            Assert.True(state.IsValid);
            Assert.True(state.FullBody);
            var p = state.Plane;
            float horizontalLength = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            var horizontal = new Vector2(p.X, p.Z) / horizontalLength;
            float radius = 1 / MathF.Sqrt(horizontal.X * horizontal.X / MathF.Pow(TorsoBisectionPolicy.FullBodyOffsetX, 2)
                + horizontal.Y * horizontal.Y / MathF.Pow(TorsoBisectionPolicy.FullBodyOffsetZ, 2));
            float intercept = -p.W / horizontalLength;
            if (style == TorsoBisectionStyle.Vertical)
            {
                Assert.Equal(0, p.Y);
                Assert.InRange(intercept, -radius - 0.00001f, radius + 0.00001f);
                minimumVerticalOffset = MathF.Min(minimumVerticalOffset, intercept / radius);
                maximumVerticalOffset = MathF.Max(maximumVerticalOffset, intercept / radius);
                continue;
            }
            float slope = p.Y / horizontalLength, tilt = MathF.Atan(MathF.Abs(slope)) * 180 / MathF.PI;
            bool oblique = tilt > TorsoBisectionPolicy.LongDiagonalMaximumTiltDegrees + 0.0001f;
            float minimumPivot, maximumPivot, maximumOffset;
            if (oblique)
            {
                Assert.InRange(tilt, TorsoBisectionPolicy.ObliqueDiagonalMinimumTiltDegrees - 0.0001f,
                    TorsoBisectionPolicy.ObliqueDiagonalMaximumTiltDegrees + 0.0001f);
                minimumPivot = TorsoBisectionPolicy.ObliqueDiagonalPivotMinimum;
                maximumPivot = TorsoBisectionPolicy.ObliqueDiagonalPivotMaximum;
                maximumOffset = radius * TorsoBisectionPolicy.ObliqueDiagonalOffsetScale;
                obliquePositive |= slope > 0;
                obliqueNegative |= slope < 0;
                maximumObliqueTilt = MathF.Max(maximumObliqueTilt, tilt);
            }
            else
            {
                Assert.InRange(tilt, TorsoBisectionPolicy.LongDiagonalMinimumTiltDegrees - 0.0001f,
                    TorsoBisectionPolicy.LongDiagonalMaximumTiltDegrees + 0.0001f);
                minimumPivot = TorsoBisectionPolicy.LongDiagonalPivotMinimum;
                maximumPivot = TorsoBisectionPolicy.LongDiagonalPivotMaximum;
                maximumOffset = radius * TorsoBisectionPolicy.LongDiagonalOffsetScale;
                longPositive |= slope > 0;
                longNegative |= slope < 0;
                minimumLongTilt = MathF.Min(minimumLongTilt, tilt);
            }
            // The plane must cross the permitted ellipse at some height inside its family's pivot band.
            float low = MathF.Min(slope * minimumPivot, slope * maximumPivot) - maximumOffset;
            float high = MathF.Max(slope * minimumPivot, slope * maximumPivot) + maximumOffset;
            Assert.InRange(intercept, low - 0.00001f, high + 0.00001f);
        }
        Assert.True(longPositive && longNegative && obliquePositive && obliqueNegative);
        Assert.True(minimumVerticalOffset < -0.9f && maximumVerticalOffset > 0.9f);
        Assert.True(minimumLongTilt < 9 && maximumObliqueTilt > 33);
    }

    [Fact]
    public void Opposing_oblique_blades_cut_across_the_torso_with_a_shared_height_and_opposite_slopes()
    {
        float minimumHeight = 100, maximumHeight = 0, minimumAngle = 90, maximumAngle = 0;
        foreach (ulong seed in Enumerable.Range(0, 128).Select(i => (ulong)i))
        foreach (var travel in new[] { Vector2.UnitX, Vector2.UnitY, new Vector2(-1, 1) })
        {
            var left = TorsoBisectionPolicy.StateForDirection(2, seed, travel, 0.4f, TorsoBisectionStyle.Oblique);
            var right = TorsoBisectionPolicy.StateForDirection(3, seed, travel, 0.4f, TorsoBisectionStyle.Oblique);
            Assert.True(left.IsValid && right.IsValid && left.FullBody && right.FullBody);
            Assert.Equal(left, TorsoBisectionPolicy.StateForDirection(2, seed, travel, 0.4f, TorsoBisectionStyle.Oblique));
            var a = left.Plane; var b = right.Plane;
            Assert.True(a.Y > 0 && b.Y < 0);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Z, b.Z);
            Assert.Equal(a.Y, -b.Y);
            float horizontalLength = MathF.Sqrt(a.X * a.X + a.Z * a.Z);
            float angleAboveHorizontal = MathF.Atan(horizontalLength / a.Y) * 180 / MathF.PI;
            Assert.InRange(angleAboveHorizontal, 30 - 0.0001f, 50 + 0.0001f);
            // Opposing planes meet at the same torso height; their average offset stays near its centre.
            float height = -(a.W - b.W) / (a.Y - b.Y), offset = -(a.W + b.W) / (2 * horizontalLength);
            Assert.InRange(height, TorsoBisectionPolicy.ObliquePivotMinimum - 0.00001f, TorsoBisectionPolicy.ObliquePivotMaximum + 0.00001f);
            float hx = a.X / horizontalLength, hz = a.Z / horizontalLength;
            float radius = 1 / MathF.Sqrt(hx * hx / (TorsoBisectionPolicy.FullBodyOffsetX * TorsoBisectionPolicy.FullBodyOffsetX)
                + hz * hz / (TorsoBisectionPolicy.FullBodyOffsetZ * TorsoBisectionPolicy.FullBodyOffsetZ));
            Assert.InRange(MathF.Abs(offset), 0, radius * TorsoBisectionPolicy.ObliqueOffsetScale + 0.00001f);
            minimumHeight = MathF.Min(minimumHeight, height); maximumHeight = MathF.Max(maximumHeight, height);
            minimumAngle = MathF.Min(minimumAngle, angleAboveHorizontal); maximumAngle = MathF.Max(maximumAngle, angleAboveHorizontal);
            foreach (int nativeAxis in new[] { 1, 7, 9 })
                Assert.True(TorsoBisectionPolicy.StateForDirection(nativeAxis, seed, travel, 0.4f, TorsoBisectionStyle.Oblique).IsValid);
        }
        Assert.True(maximumHeight - minimumHeight > 1.1f);
        Assert.True(minimumAngle < 31 && maximumAngle > 49);
        Assert.False(TorsoBisectionPolicy.StateForDirection(4, style: TorsoBisectionStyle.Oblique).IsValid);
        Assert.False(TorsoBisectionPolicy.StateForDirection(5, style: TorsoBisectionStyle.Oblique).IsValid);
    }

    [Fact]
    public void Each_opening_plane_survives_save_load_exactly_and_cannot_be_moved_by_a_later_hit()
    {
        foreach (ulong seed in new ulong[] { 0, 1, 3, 123456789 })
        foreach (var style in new[] { TorsoBisectionStyle.Transverse, TorsoBisectionStyle.Vertical, TorsoBisectionStyle.Diagonal, TorsoBisectionStyle.Oblique })
        {
            var victim = Victim(out int chest);
            var events = new List<MedicalEvent>();
            int axis = style == TorsoBisectionStyle.Transverse ? 3 : 1;
            victim.Medical!.ApplyHit(chest, new(500, 0, 0), Context(victim.Race), events);
            Assert.True(CombatSystem.TryBisect(victim, chest, new(500, 0, 0), CombatWoundMechanism.Slash, true, 0, axis, events, seed, Vector2.UnitX, 0.4f, style, brutality: 100));
            var fixedPlane = victim.TorsoBisection!.Value;
            Assert.False(CombatSystem.TryBisect(victim, chest, new(500, 0, 0), CombatWoundMechanism.Slash, true, 0, 2, events, seed + 1, Vector2.UnitY, 2, brutality: 100));
            Assert.Equal(fixedPlane, victim.TorsoBisection);
            var source = new TorsoBisections();
            Assert.True(source.Set(new(3, 1), fixedPlane));
            var written = source.Write(_ => (12, 4)).ToArray();
            var loaded = new TorsoBisections();
            Assert.Equal(1, loaded.Read(written, handle => handle == (12, 4) ? new CharacterId(17, 2) : null).Restored);
            Assert.Equal(fixedPlane, loaded.Of(new(17, 2)));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void An_actual_melee_hit_uses_its_own_visual_hash_and_preserves_the_existing_chance_roll(int direction)
    {
        using var world = NewWorld(7, 1, out var combat, techniques: [CombatFixtures.Cut("cut left", direction: direction)]);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world,
            new Combatant { Attack = 100, Defence = 100, WeaponSkill = 100, Dexterity = 100,
                Weapon = CombatFixtures.Katana with { CutDamageMultiplier = 25 } },
            new Combatant { Attack = 0, Defence = 0, Dodge = 0, Weapon = null });
        world.Characters.Cold(attacker.Slot)!.Brutality = BrutalityProgress.FromLevel(100);
        var victim = world.Characters.Cold(defender.Slot)!;
        var race = new RaceData { StringId = Human.StringId, Name = Human.Name, Anatomy = [new(CanonicalChest, 100, 100)] };
        victim.Race = race;
        victim.Medical = MedicalState.Create(race, 50);
        RunToEnd(world, attacker, defender, 300);
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit && e.Victim == defender);
        var log = Assert.Single(combat.Log, e => e.Outcome == BlowOutcome.Hit && e.Defender == defender.Slot);
        ulong counter = CombatRolls.BlowCounter(attacker.Slot, combat.StateOf(attacker).AttackSeq, log.Blow, 0);
        float chance = CombatRolls.Float(world.Seed, Rng.Key(defender), CombatRoll.BodyBisection, counter);
        Assert.InRange(chance, 0, TorsoBisectionPolicy.MaximumChance);
        Assert.True(TorsoBisectionPolicy.ShouldBisect(new(CombatHitSource.Melee, hit.Mechanism, CanonicalChest,
            hit.CutDirection, hit.Damage.Cut, victim.Medical.Parts[0].MaxHp) { Brutality = 100, Lethal = true }, chance));
        Assert.NotNull(victim.TorsoBisection);
        ulong visual = TorsoBisectionPolicy.VisualSeedFor(world.Seed, defender, attacker, counter);
        Assert.Equal(TorsoBisectionPolicy.StateForDirection(hit.CutDirection, visual, hit.Direction, hit.VictimYaw), victim.TorsoBisection!.Value);
        if (direction == 1) Assert.True(victim.TorsoBisection!.Value.FullBody);
        else if (victim.TorsoBisection!.Value.FullBody)
            Assert.True(MathF.Abs(victim.TorsoBisection.Value.Plane.Y) >= MathF.Sin(TorsoBisectionPolicy.ObliqueMinimumTiltDegrees * MathF.PI / 180) - 0.00001f);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died && e.Victim == defender);
        Assert.Equal(chance, CombatRolls.Float(world.Seed, Rng.Key(defender), CombatRoll.BodyBisection, counter));
    }

    [Fact]
    public void A_powerful_nonlethal_chest_hit_stays_nonlethal_even_at_mastery()
    {
        var victim = Victim(out int chest);
        var medical = victim.Medical!;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(120, 0, 0);
        medical.ApplyHit(chest, damage, Context(victim.Race), events);
        Assert.False(medical.Dead);
        var part = medical.Parts[chest];
        var before = (part.Flesh, part.Stun, part.Bandage, part.Rig, medical.Blood);
        Assert.False(CombatSystem.TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, 2, events, brutality: 100));
        Assert.False(medical.Dead);
        Assert.Null(victim.TorsoBisection);
        Assert.Equal(before, (part.Flesh, part.Stun, part.Bandage, part.Rig, medical.Blood));
        Assert.DoesNotContain(events, e => e.Kind == MedicalEventKind.Died);
    }

    [Fact]
    public void A_native_lethal_hit_can_bisect_but_a_preexisting_corpse_cannot()
    {
        var victim = Victim(out int chest);
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(350, 0, 0);
        victim.Medical!.ApplyHit(chest, damage, Context(victim.Race), events);
        Assert.True(victim.Medical.Dead);
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
        Assert.True(CombatSystem.TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, 3, events, brutality: 100));
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);

        var corpse = Victim(out chest);
        corpse.Medical!.Butcher();
        events.Clear();
        Assert.False(CombatSystem.TryBisect(corpse, chest, damage, CombatWoundMechanism.Slash, false, 0, 3, events, brutality: 100));
        Assert.Null(corpse.TorsoBisection);
        Assert.Empty(events);
        Assert.Equal(DeathCause.Butchered, corpse.Medical.Cause);
    }

    [Fact]
    public void Bisection_is_published_after_native_death_once_with_the_original_hit_metadata()
    {
        using var world = NewWorld(11, 1, out var combat);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(350, 0, 0);
        cold.Medical!.ApplyHit(chest, damage, Context(cold.Race), events);
        bool bisected = CombatSystem.TryBisect(cold, chest, damage, CombatWoundMechanism.Slash, true, 0, 2, events, brutality: 100);
        combat.PublishHit(world, CombatHitSource.Melee, attacker, defender.Slot, cold.Medical, chest, damage,
            Vector2.UnitX, events, 2, mechanism: CombatWoundMechanism.Slash, bodyBisected: bisected);
        var published = queue.ToArray();
        Assert.Equal(CombatEventKind.Hit, published[0].Kind);
        Assert.Single(published, e => e.Kind == CombatEventKind.Died);
        var cut = Assert.Single(published, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal(CombatEventKind.BodyBisected, published[^1].Kind);
        Assert.Equal((attacker, defender, chest, damage, Vector2.UnitX, CombatWoundMechanism.Slash),
            (cut.Attacker, cut.Victim, cut.Part, cut.Damage, cut.Direction, cut.Mechanism));

        events.Clear();
        bisected = CombatSystem.TryBisect(cold, chest, damage, CombatWoundMechanism.Slash, false, 0, 2, events, brutality: 100);
        combat.PublishHit(world, CombatHitSource.Melee, attacker, defender.Slot, cold.Medical, chest, damage,
            Vector2.UnitX, events, 2, mechanism: CombatWoundMechanism.Slash, bodyBisected: bisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
    }

    [Fact]
    public void The_debug_command_runs_at_its_tick_and_publishes_the_actual_direction_only_once()
    {
        using var world = NewWorld(11, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        world.Player.Selection.Add(attacker);
        float before = cold.Medical!.Parts[chest].Flesh;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 3 });
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 3 });
        while (world.Tick <= 3) world.RunTick();

        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        var cut = Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
        Assert.Equal(3L, cut.Tick);
        Assert.Equal((attacker, defender, CombatWoundMechanism.Slash), (cut.Attacker, cut.Victim, cut.Mechanism));
        var a = world.Characters.Previous[attacker.Slot].Position;
        var b = world.Characters.Previous[defender.Slot].Position;
        Assert.Equal(new Vector2(b.X - a.X, b.Z - a.Z), cut.Direction);
        Assert.True(cold.Medical.Dead);
        Assert.Equal(before - hit.Damage.Cut, cold.Medical.Parts[chest].Flesh);
        Assert.True(cold.TorsoBisection!.Value.IsValid);
        var plane = cold.TorsoBisection;
        ulong visual = TorsoBisectionPolicy.VisualSeedFor(world.Seed, defender, attacker, 3);
        Assert.Equal(TorsoBisectionPolicy.StateForDirection(hit.CutDirection, visual, hit.Direction, hit.VictimYaw), plane!.Value);
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = world.Tick, Attacker = attacker });
        world.RunTick();
        Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
        Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal(plane, cold.TorsoBisection);
    }

    [Fact]
    public void Debug_visual_seeds_are_repeatable_across_worlds_and_support_opposite_diagonals()
    {
        var examples = new HashSet<Vector4>();
        foreach (ulong seed in new ulong[] { 0, 1, 3 })
        {
            var first = DebugPlane(11, seed, style: TorsoBisectionStyle.Transverse);
            Assert.Equal(first, DebugPlane(31, seed, style: TorsoBisectionStyle.Transverse));
            examples.Add(first);
        }
        Assert.Equal(3, examples.Count);
        Assert.True(examples.Max(p => -p.W / p.Y) - examples.Min(p => -p.W / p.Y) > 1.4f);
        Assert.NotEqual(DebugPlane(11, null, style: TorsoBisectionStyle.Transverse), DebugPlane(31, null, style: TorsoBisectionStyle.Transverse));
        var left = DebugPlane(11, 1, 2, TorsoBisectionStyle.Transverse);
        var right = DebugPlane(11, 1, 3, TorsoBisectionStyle.Transverse);
        Assert.Equal(left.Y, right.Y);
        Assert.Equal(left.W, right.W);
        Assert.Equal(left.X, -right.X);
        Assert.Equal(left.Z, -right.Z);
    }

    [Theory]
    [InlineData(TorsoBisectionStyle.Vertical)]
    [InlineData(TorsoBisectionStyle.Diagonal)]
    public void Explicit_debug_styles_choose_a_downward_cut_with_a_repeatable_full_body_plane(TorsoBisectionStyle style)
    {
        var first = DebugPlane(11, 1, style: style);
        Assert.Equal(first, DebugPlane(31, 1, style: style));
        Assert.True(style == TorsoBisectionStyle.Vertical ? first.Y == 0 : MathF.Abs(first.Y) > 0.1f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9)]
    public void An_explicit_oblique_debug_cut_accepts_native_side_and_downward_axes(int direction)
    {
        var first = DebugPlane(11, 1, direction, TorsoBisectionStyle.Oblique);
        Assert.Equal(first, DebugPlane(31, 1, direction, TorsoBisectionStyle.Oblique));
        float horizontal = MathF.Sqrt(first.X * first.X + first.Z * first.Z);
        Assert.InRange(MathF.Atan(horizontal / MathF.Abs(first.Y)) * 180 / MathF.PI, 30, 50);
    }

    [Theory]
    [InlineData(false, true, 2, TorsoBisectionStyle.Auto)]
    [InlineData(false, false, 1, TorsoBisectionStyle.Transverse)]
    [InlineData(false, false, 4, TorsoBisectionStyle.Oblique)]
    [InlineData(false, false, 5, TorsoBisectionStyle.Oblique)]
    public void The_debug_command_rejects_ineligible_targets_and_style_axis_mismatches_before_applying_damage(bool robot, bool animal, int direction, TorsoBisectionStyle style)
    {
        using var world = NewWorld(11, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = BisectionRace(robot);
        cold.Medical = setup.Medical;
        cold.Fighter = new Fighter(null) { IsAnimal = animal };
        float flesh = cold.Medical!.Parts[chest].Flesh, blood = cold.Medical.Blood;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 0, Attacker = attacker, CutDirection = direction, Style = style });
        world.RunTick();
        Assert.Equal(flesh, cold.Medical.Parts[chest].Flesh);
        Assert.Equal(blood, cold.Medical.Blood);
        Assert.False(cold.Medical.Dead);
        Assert.Null(cold.TorsoBisection);
        Assert.Empty(queue);
    }

    static Vector4 DebugPlane(ulong worldSeed, ulong? visualSeed, int direction = 2, TorsoBisectionStyle style = TorsoBisectionStyle.Auto)
    {
        using var world = NewWorld(worldSeed, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out _);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 0, Attacker = attacker, VisualSeed = visualSeed, CutDirection = direction, Style = style });
        world.RunTick();
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        Assert.True(cold.Medical!.Dead);
        var state = cold.TorsoBisection!.Value;
        ulong variation = visualSeed ?? TorsoBisectionPolicy.VisualSeedFor(worldSeed, defender, attacker, 0);
        Assert.Equal(TorsoBisectionPolicy.StateForDirection(hit.CutDirection, variation, hit.Direction, hit.VictimYaw, style), state);
        if (style is TorsoBisectionStyle.Vertical or TorsoBisectionStyle.Diagonal) Assert.True(state.FullBody && hit.CutDirection is 1 or 7);
        if (style == TorsoBisectionStyle.Oblique)
        {
            Assert.True(state.FullBody);
            Assert.Equal(direction, hit.CutDirection);
        }
        return state.Plane;
    }

    static CharacterCold Victim(out int chest)
    {
        var race = BisectionRace();
        var medical = MedicalState.Create(race, 50);
        chest = Enumerable.Range(0, medical.Parts.Count).Single(i => medical.Parts[i].Template.StringId == ArmourData.ChestPartId);
        return new CharacterCold { Race = race, Medical = medical, Fighter = new Fighter(null) };
    }

    static RaceData BisectionRace(bool robot = false) => new()
    {
        StringId = Human.StringId,
        Name = Human.Name,
        IsRobot = robot,
        StatMultipliers = Human.StatMultipliers,
        Anatomy = [.. Human.Anatomy.Select(e => e.Part == Chest ? e with { Part = CanonicalChest } : e)],
    };
}
