using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Explicit developer diagnostics only; display state never enters gameplay, saves, hashes or normal logging.</summary>
internal static class DeveloperCombatDiagnostics
{
    sealed class TraceState(Action<string> write, int limit)
    {
        internal readonly Action<string> Write = write;
        internal readonly int Limit = limit;
        internal int Lines;
        internal readonly Dictionary<GoreHitKey, int> NativeEvents = [];
    }
    static readonly ConditionalWeakTable<World, TraceState> worlds = new();
    static bool enabled;
    internal static bool EnabledBy(bool developerTools, string? request) => developerTools && request == "1";
    internal static void Configure(bool developerTools, string? request) => enabled = EnabledBy(developerTools, request);
    static TraceState? State(World world) => worlds.TryGetValue(world, out var state) ? state
        : enabled ? worlds.GetValue(world, static _ => new(Console.WriteLine, 256)) : null;
    internal static bool Active(World world) => State(world) is { } state && state.Lines < state.Limit;

    internal static void CaptureNative(World world, in CombatHit hit, int eventCount)
    {
        if (State(world) is not { } state || state.Lines >= state.Limit || state.NativeEvents.Count >= state.Limit) return;
        state.NativeEvents[GoreHitKey.From(hit)] = eventCount;
    }

    internal static void Trace(World world, in CombatHit hit, GoreHitBridge? bridge, float brutality, float maximum,
        in TorsoBisectionInput torso, bool accepted, string kinds)
    {
        if (State(world) is not { } state || state.Lines >= state.Limit) return;
        int events = state.NativeEvents.Remove(GoreHitKey.From(hit), out int count) ? count : -1;
        float threshold = MathF.Max(TorsoBisectionPolicy.MinimumCut, TorsoBisectionPolicy.MaximumHpFraction * maximum);
        string reason = bridge is null ? "missing-before-context" : TorsoReason(torso);
        string text = FormattableString.Invariant($"Brutality real hit: tick={hit.Tick} source={hit.Source} attacker={hit.Attacker} victim={hit.Victim} seq={hit.Sequence} blow={hit.Blow} part={hit.Part} maxHp={maximum:F3} usefulBefore={bridge?.UsefulRemainingBeforeHit:F3} rawCut={bridge?.PreArmourCut:F3} actualCut={hit.Damage.Cut:F3} brutality={brutality:F3} direction={hit.CutDirection} mechanism={bridge?.Mechanism} aliveBefore={hit.VictimWasAlive} lethal={torso.Lethal} torsoThreshold={threshold:F3} torsoChance={TorsoBisectionPolicy.Chance(torso):F3} torsoGate={reason} torsoAccepted={accepted} acceptedKinds={kinds} nativeEvents={events}");
        state.Write(text); state.Lines++;
        if (state.Lines >= state.Limit) state.NativeEvents.Clear();
    }

    internal static string TorsoReason(in TorsoBisectionInput input)
    {
        if (!input.AliveBeforeHit) return "dead-before";
        if (!input.Lethal) return "not-lethal";
        if (input.AlreadyBisected) return "already-bisected";
        if (input.Animal) return "animal";
        if (input.Source != CombatHitSource.Melee) return "not-melee";
        if (input.Mechanism != CombatWoundMechanism.Slash) return "not-slash";
        if (input.CutDirection is not (1 or 2 or 3 or 7 or 9)) return "direction";
        if (input.Part is not { Type: BodyPartType.Torso, StringId: ArmourData.ChestPartId or GorePartIds.Stomach }) return "part";
        if (!float.IsFinite(input.Maximum) || input.Maximum <= 0 || !float.IsFinite(input.Cut)) return "invalid-damage";
        if (input.Cut < MathF.Max(TorsoBisectionPolicy.MinimumCut, TorsoBisectionPolicy.MaximumHpFraction * input.Maximum)) return "cut-below-threshold";
        if (BrutalityPolicy.BisectionChance(input.Brutality) <= 0) return "brutality-locked";
        return "eligible";
    }

    internal static void EnableForTest(World world, Action<string> write, int limit = 256)
    {
        worlds.Remove(world); worlds.Add(world, new(write, Math.Clamp(limit, 1, 256)));
    }
}
