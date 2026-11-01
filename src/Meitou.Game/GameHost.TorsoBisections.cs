using System.Numerics;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;

namespace Meitou.Game;

/// <summary>Permanent torso cuts and their two held-pose visual halves. The simulation retains one lootable corpse.</summary>
sealed partial class GameHost
{
    readonly TorsoBisections torsoCuts = new();
    readonly Dictionary<CharacterId, (long Tick, Vector2 Direction)> torsoBirths = [];
    readonly Dictionary<CharacterId, TorsoFall> torsoFalls = [];

    sealed class TorsoFall
    {
        public required Matrix4x4 World;
        public required Vector3 PositivePivot, NegativePivot, CutAt, Origin, Along;
        public required Quaternion PositiveTurn, NegativeTurn;
        public required (Vector3 Point, float Radius)[] PositiveBounds, NegativeBounds;
    }

    void ClearTorsoCuts()
    {
        torsoCuts.Clear();
        torsoBirths.Clear();
        torsoFalls.Clear();
    }

    void ReadTorsoCuts(string folder)
    {
        string path = Path.Combine(folder, TorsoBisections.FileName);
        if (!File.Exists(path)) return;
        var retained = loadedSave?.Source.Characters.Select(c => (c.Handle.C, c.Handle.I)).ToHashSet();
        var read = torsoCuts.Read(File.ReadLines(path), SavedWoundCharacter, retained is null ? null : retained.Contains);
        Console.WriteLine($"wounds    torso cuts: {read.Restored} restored, {read.Pending} waiting for characters, {read.Rejected} rejected rows");
    }

    void RefreshTorsoCuts(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            var id = table.IdOf(slot);
            if (!table.IsAlive(id) || table.Cold(slot) is not { } cold) continue;
            if (torsoCuts.Of(id) is null && cold.TorsoBisection is { } stored) torsoCuts.Set(id, stored);
            if (cold.Save?.Source.Handle is { IsNull: false } handle) torsoCuts.Bind(id, (handle.C, handle.I));
        }
        torsoCuts.Refresh(SavedWoundCharacter, table.IsAlive);
        torsoCuts.PruneUnboundInactive(table.IsAlive);
        foreach (var id in torsoFalls.Keys.Where(id => !table.IsAlive(id)).ToArray()) torsoFalls.Remove(id);
        foreach (var id in torsoBirths.Keys.Where(id => !table.IsAlive(id)).ToArray()) torsoBirths.Remove(id);
        foreach (var (id, state) in torsoCuts.All)
            if (table.IsAlive(id) && table.Cold(id.Slot) is { } cold) cold.TorsoBisection = state;
    }

    void BeginTorsoCut(in CombatEvent hit, World world)
    {
        if (!world.Characters.IsAlive(hit.Victim) || world.Characters.Cold(hit.Victim.Slot)?.TorsoBisection is not { } cut) return;
        torsoCuts.Set(hit.Victim, cut);
        torsoBirths.TryAdd(hit.Victim, (hit.Tick, hit.Direction));
    }

    /// <summary>Lists two separately placed halves, preserving the original lower-half key for picking and portraits.</summary>
    void AddTorsoHalves(CharacterDrawList list, CharacterSnapshot c, in CharacterInstance instance)
    {
        if (torsoCuts.Of(c.Id) is not { } cut || gpu.Characters is not { } renderer)
        {
            list.Add(instance);
            return;
        }
        long lower = instance.Key, upper = lower ^ (1L << 52);
        // Renderer poses age out when a corpse stays offscreen. Re-prepare a clipped corpse instead of freezing from a missing key.
        if (torsoFalls.ContainsKey(c.Id) && renderer.LastDrawn(lower, "Bip01 Spine1") is null)
        {
            torsoFalls.Remove(c.Id);
            torsoBirths.Remove(c.Id); // returning corpses reconstruct at rest, without replaying the fatal impact
        }
        if (!torsoFalls.TryGetValue(c.Id, out var fall))
        {
            if (renderer.LastDrawn(lower, "Bip01 Spine1") is not ({ } world, _, var pivot))
            {
                // Prepare an unseen corpse from animation: ragdoll overrides stretched facial/hair vertices in the scripted capture.
                // A previously visible impact still holds its actual last pose; a restored corpse never flashes whole.
                list.Add(instance with { TorsoCut = new(cut.Plane, -1, cut.FullBody), Bones = null });
                return;
            }
            var positiveCut = new CharacterTorsoCut(cut.Plane, 1, cut.FullBody);
            pivot = renderer.TorsoCutPosition(lower, positiveCut) ?? pivot;
            Vector3 head = renderer.BonePosition(lower, "Bip01 Head") ?? pivot + Vector3.UnitY * 5;
            Vector3 feet = ((renderer.BonePosition(lower, "Bip01 L Foot") ?? pivot - Vector3.UnitY * 10)
                + (renderer.BonePosition(lower, "Bip01 R Foot") ?? pivot - Vector3.UnitY * 10)) / 2;
            Vector2 direction = torsoBirths.TryGetValue(c.Id, out var birth) ? birth.Direction : new(MathF.Cos(instance.Yaw), -MathF.Sin(instance.Yaw));
            Vector3 along = direction.LengthSquared() > 1e-6f ? Vector3.Normalize(new(direction.X, 0, direction.Y)) : Vector3.UnitX;
            (Vector3, float)[] Bounds(bool top) => BodyBones
                .Where(b => top == !(b.Bone.Contains("Thigh", StringComparison.Ordinal) || b.Bone.Contains("Calf", StringComparison.Ordinal) || b.Bone == "Bip01 Pelvis"))
                .Select(b => (Point: renderer.BonePosition(lower, b.Bone), b.Radius))
                .Where(b => b.Point.HasValue).Select(b => (b.Point!.Value - pivot, b.Radius * 0.8f)).ToArray();
            var positivePivot = pivot;
            var negativePivot = pivot;
            var positiveBounds = Bounds(true);
            var negativeBounds = Bounds(false).Concat(new[] { (feet - pivot, 0.6f) }).ToArray();
            var positiveTurn = TorsoTurn(head - pivot, along);
            var negativeTurn = TorsoTurn(feet - pivot, -along);
            if (cut.FullBody)
            {
                var normal = renderer.TorsoCutNormal(lower, positiveCut)
                    ?? Vector3.Normalize(Vector3.TransformNormal(new(cut.Plane.X, cut.Plane.Y, cut.Plane.Z), world));
                along = TorsoFragmentMotion.Across(normal);
                Vector3[] FallbackPoints() => [.. BodyBones.Select(b => renderer.BonePosition(lower, b.Bone)).Where(p => p.HasValue).Select(p => p!.Value)];
                var positivePoints = renderer.TorsoCutBounds(lower, positiveCut);
                var negativePoints = renderer.TorsoCutBounds(lower, positiveCut with { KeepSide = -1 });
                if (positivePoints is not { Length: > 0 }) positivePoints = FallbackPoints();
                if (negativePoints is not { Length: > 0 }) negativePoints = FallbackPoints();
                positivePivot = TorsoFragmentMotion.LowestPivot(positivePoints, feet);
                negativePivot = TorsoFragmentMotion.LowestPivot(negativePoints, feet);
                positiveBounds = [.. positivePoints.Select(p => (p - positivePivot, 0.15f))];
                negativeBounds = [.. negativePoints.Select(p => (p - negativePivot, 0.15f))];
                bool crossesTorso = MathF.Abs(cut.Plane.Y) >= 0.6f;
                positiveTurn = crossesTorso ? TorsoFragmentMotion.LyingTurn(head - feet, normal, 1) : TorsoFragmentMotion.OutwardTurn(head - feet, normal, 1);
                negativeTurn = crossesTorso ? TorsoFragmentMotion.LyingTurn(head - feet, normal, -1) : TorsoFragmentMotion.OutwardTurn(head - feet, normal, -1);
            }
            if (!renderer.HoldPose(lower, lower) || !renderer.HoldPose(upper, lower))
            {
                list.Add(instance with { TorsoCut = new(cut.Plane, -1, cut.FullBody), Bones = null });
                return;
            }
            fall = new TorsoFall
            {
                World = world, CutAt = pivot, PositivePivot = positivePivot, NegativePivot = negativePivot, Origin = c.Position, Along = along,
                PositiveTurn = positiveTurn, NegativeTurn = negativeTurn,
                PositiveBounds = positiveBounds, NegativeBounds = negativeBounds,
            };
            torsoFalls[c.Id] = fall;
            SpillTorsoGuts(c.Id, fall, TorsoAge(c.Id));
        }
        float age = TorsoAge(c.Id);
        float s = Math.Clamp(age / 1.1f, 0, 1);
        float ease = s * s * (3 - 2 * s);
        Vector3 delta = c.Position - fall.Origin;
        for (int side = -1; side <= 1; side += 2)
        {
            var turn = Quaternion.Slerp(Quaternion.Identity, side > 0 ? fall.PositiveTurn : fall.NegativeTurn, ease);
            var rotation = Matrix4x4.CreateFromQuaternion(turn);
            var piecePivot = side > 0 ? fall.PositivePivot : fall.NegativePivot;
            float reach = cut.FullBody ? side * 4.5f : side > 0 ? 4.8f : -2.8f;
            var at = piecePivot + delta + fall.Along * reach * ease;
            float bottom = TorsoFragmentMotion.Bottom(side > 0 ? fall.PositiveBounds : fall.NegativeBounds, turn);
            float floor = session.World.Walkability.GroundHeight(at.X, at.Z, piecePivot.Y + delta.Y);
            at.Y = MathF.Max(piecePivot.Y + delta.Y + 1.5f * MathF.Min(age, 0.8f) - 0.5f * FallGravity * MathF.Min(age * age, 9), floor - bottom + 0.1f);
            var placement = fall.World * Matrix4x4.CreateTranslation(-piecePivot) * rotation * Matrix4x4.CreateTranslation(at);
            list.Add(instance with
            {
                Key = side > 0 ? upper : lower, Position = at, World = placement,
                FreezeFrom = lower, Bones = null, Rotation = null, TorsoCut = new(cut.Plane, side, cut.FullBody),
            });
        }
    }

    float? TorsoMeshHit(Meitou.Rendering.Characters.CharacterRenderer? renderer, CharacterId id, Vector3 origin, Vector3 direction)
    {
        if (renderer is null) return null;
        long lower = DrawKey(id);
        float? hit = renderer.RayHit(lower, origin, direction);
        if (torsoCuts.Of(id) is null) return hit;
        float? upper = renderer.RayHit(lower ^ (1L << 52), origin, direction);
        return MathF.Min(hit ?? float.PositiveInfinity, upper ?? float.PositiveInfinity);
    }

    float TorsoAge(CharacterId id) => torsoBirths.TryGetValue(id, out var birth)
        ? MathF.Max(0, (float)((session.ShownTick + session.SimulationAlpha - birth.Tick) * session.World.TickSeconds)) : 3;

    static Quaternion TorsoTurn(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-6f) return Quaternion.Identity;
        from = Vector3.Normalize(from); to = Vector3.Normalize(to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (dot > 0.9999f) return Quaternion.Identity;
        if (dot < -0.9999f)
        {
            var axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(from, Vector3.UnitZ);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }

    void SpillTorsoGuts(CharacterId id, TorsoFall fall, float age)
    {
        if (gutFalls.ContainsKey(id) || gutsSpilled.Contains(id)) return;
        var gut = new GutFall
        {
            Position = fall.CutAt + fall.Along * 0.6f, Velocity = fall.Along * 2 + Vector3.UnitY,
            Floor = session.World.Walkability.GroundHeight(fall.CutAt.X, fall.CutAt.Z, fall.CutAt.Y)
                + (gpu.Debris?.Extent(DebrisRenderer.Bowel)?.Y ?? 0.35f) - 0.35f,
        };
        // A still may advance the hit before any render. Match the loose bowel to the halves' event-time clock.
        for (float remaining = MathF.Min(age, 4); remaining > 0; remaining -= 0.025f) StepGut(gut, MathF.Min(remaining, 0.025f));
        gutFalls[id] = gut;
    }
}
