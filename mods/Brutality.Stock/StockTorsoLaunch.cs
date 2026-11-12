using System.Numerics;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality
{
    /// <summary>Bounds the fatal blow that starts the native corpse before the cut halves are ready.</summary>
    internal static class StockTorsoLaunch
    {
        const float NativePushLimit = 11, NativeMotionLimit = 12;
        static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

        internal static void Install(ModContext context)
        {
            var apply = AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Apply),
                [typeof(World), typeof(Effect).MakeByRefType()]);
            context.Harmony.Patch(apply, postfix: new HarmonyMethod(typeof(StockTorsoLaunch), nameof(AfterMelee)));
        }

        static void AfterMelee(World world, in Effect e)
        {
            if (e.Kind != CombatSystem.BlowEffect) return;
            var characters = world.Characters;
            if ((uint)e.Target >= (uint)characters.HighWater
                || characters.Cold(e.Target) is not { Medical.Dead: true, Blow: { } raw } cold
                || !StockTorsoRules.TryGetSaved(world, characters.IdOf(e.Target), out var cut)
                || cut.Tick != world.Tick || cut.Restoring) return;

            // The fragment worker starts from this corpse's rendered pose. A slice can topple it
            // but shouldn't send the struck bone ahead of the rest while the two meshes prepare.
            var limited = Limit(raw, cut.Robot);
            if (limited == raw) return;
            cold.Blow = limited;
            cold.Knockdown = limited.Velocity;
            if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock torso launch actor={cut.Victim} nativePush={raw.Push.Length():F1}->{limited.Push.Length():F1} speed={limited.Velocity.Length():F1} robot={cut.Robot}"));
        }

        internal static RagdollBlow Limit(RagdollBlow blow, bool robot)
        {
            var motion = Clamp(blow.Velocity - blow.Push, NativeMotionLimit);
            var push = Clamp(blow.Push, NativePushLimit * RobotRagdollWeight.Response(robot));
            return new(motion + push, push, null);
        }

        internal static Vector3 Clamp(Vector3 value, float limit)
        {
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared)) return Vector3.Zero;
            return lengthSquared > limit * limit ? value * (limit / MathF.Sqrt(lengthSquared)) : value;
        }
    }
}

namespace Meitou.Brutality.Physics
{
    internal sealed partial class AdvancedFragmentScene
    {
        /// <summary>Finds the support below a settled limb without depending on the time it freezes.</summary>
        internal Vector3? PoolContact(Piece piece, IRagdollEnvironment ground)
        {
            if (piece.Carried) return null;
            foreach (var handle in piece.Bodies)
            {
                if (handle is not { } id) continue;
                var body = physics.Simulation.Bodies.GetBodyReference(id);
                if (body.Velocity.Linear.LengthSquared() > 2.25f || body.Velocity.Angular.LengthSquared() > 2.25f) return null;
            }

            if (At(piece.Definition.Root) is { } root) return root;
            for (int i = 0; i < piece.Bodies.Length; i++)
                if (i != piece.Definition.Root && At(i) is { } support) return support;
            return null;

            Vector3? At(int index)
            {
                if (piece.Bodies[index] is not { } id) return null;
                var body = physics.Simulation.Bodies.GetBodyReference(id);
                var at = body.Pose.Position;
                float floor = ground.GroundHeight(at.X, at.Z, at.Y);
                float gap = body.BoundingBox.Min.Y - floor;
                return gap is >= -.40f and <= .28f ? new(at.X, floor, at.Z) : null;
            }
        }

        internal bool RestingNearGround(Piece piece, IRagdollEnvironment ground)
        {
            if (piece.Frozen) return true;
            if (piece.Carried || piece.Age < 3) return false;
            var root = piece.Pose[piece.Definition.Root].Position;
            float floor = ground.GroundHeight(root.X, root.Z, root.Y);
            if (!float.IsFinite(floor) || root.Y - floor > 4) return false;
            foreach (var handle in piece.Bodies)
            {
                if (handle is not { } h) continue;
                var body = physics.Simulation.Bodies.GetBodyReference(h);
                if (body.Velocity.Linear.LengthSquared() > 12.25f || body.Velocity.Angular.LengthSquared() > 9) return false;
            }
            return true;
        }

        internal void SeparateTorsoPair(Piece first, Piece second)
        {
            // Complementary bind-space hulls can start overlapped in the animated cut pose.
            // Allow them to separate under gravity and their small cut velocity before contact.
            first.PairedCutTime = second.PairedCutTime = .20f;
            SetCutGroup(first, -first.Number);
            SetCutGroup(second, -first.Number);
        }

        void AdvanceTorsoSeparation(Piece piece, float stepped)
        {
            if (piece.PairedCutTime <= 0 || stepped <= 0) return;
            piece.PairedCutTime -= stepped;
            if (piece.PairedCutTime > 0) return;
            foreach (var body in piece.Bodies)
                if (body is { } handle) physics.Tag(handle).CutPair *= -1;
        }

        void SetCutGroup(Piece piece, int group)
        {
            foreach (var body in piece.Bodies)
                if (body is { } handle) physics.Tag(handle).CutPair = group;
        }
    }
}
