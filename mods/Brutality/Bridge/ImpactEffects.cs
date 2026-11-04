using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

[Flags]
public enum ImpactEffectKind { None = 0, OrganicSpray = 1, OrganicSplinters = 2, RobotShortCircuit = 4, RobotScrap = 8 }
public readonly record struct ImpactEffectEvent(long Sequence, CombatHit Hit, CombatWoundMechanism Mechanism, bool Execution, bool Robot,
    uint BloodColour, string ImpactBone, float FallbackHeight, ulong Seed, ImpactEffectKind Effects)
{
    public int RuptureKind { get; init; } = -1;
}
public sealed record ImpactEffectsFrame(long Tick, IReadOnlyList<ImpactEffectEvent> Events);

/// <summary>Immutable ordinary-hit presentation, independent of gameplay random streams and coalesced with accepted cut effects.</summary>
public sealed class ImpactEffects : ICombatHitObserver
{
    const int Capacity = 512;
    readonly record struct Contact(CombatWoundMechanism Mechanism, bool Execution, bool Robot, uint Colour, string Bone, float Height, int Rupture);
    readonly Dictionary<GoreHitKey, Contact> contacts = [];
    readonly Queue<(CombatHit Hit, Contact Contact)> pending = new();
    readonly HashSet<GoreHitKey> seen = [];
    readonly Queue<GoreHitKey> seenOrder = new();
    readonly Queue<ImpactEffectEvent> events = new();
    readonly Queue<ImpactEffectsFrame> history = new();
    volatile ImpactEffectsFrame[] frames = [];
    long sequence;
    public static ImpactEffects? ForWorld(World world) => world.Systems.OfType<ImpactEffects>().FirstOrDefault();
    public static void Install(ModContext context) => context.AddSystem("impact-effects", _ => new ImpactEffects(), SystemAnchor.End);
    public ImpactEffectsFrame? FrameAt(long tick)
    {
        var available = frames;
        for (int i = available.Length - 1; i >= 0; i--) if (available[i].Tick <= tick) return available[i];
        return null;
    }
    internal void Capture(World world, in CombatHit hit, CombatWoundMechanism mechanism, bool execution, int rupture = -1)
    {
        if (Read(world, hit, mechanism, execution, rupture) is not { } captured) return;
        if (contacts.Count >= Capacity) contacts.Remove(contacts.Keys.First());
        contacts[GoreHitKey.From(hit)] = captured;
    }
    static Contact? Read(World world, in CombatHit hit, CombatWoundMechanism mechanism, bool execution, int rupture)
    {
        if (!world.Characters.TryResolveNext(hit.Victim, out int slot) || world.Characters.Cold(slot) is not { Race: { } race, Medical: { } medical }
            || (uint)hit.Part >= (uint)medical.Parts.Count) return null;
        var part = medical.Parts[hit.Part].Template;
        return new(mechanism, execution, race.IsRobot, race.BloodColour, Bone(part, hit.LowStrike), Height(part), rupture);
    }
    public void HitResolved(World world, in CombatHit hit)
    {
        var key = GoreHitKey.From(hit);
        if (!seen.Add(key)) return;
        seenOrder.Enqueue(key); while (seenOrder.Count > Capacity * 4) seen.Remove(seenOrder.Dequeue());
        var context = contacts.Remove(key, out var captured) ? captured : Read(world, hit, CombatWoundMechanism.Unspecified, false, -1);
        if (context is not { } contact) return;
        if (pending.Count >= Capacity) pending.Dequeue(); pending.Enqueue((hit, contact));
    }
    public void SlowWorld(World world)
    {
        var cuts = BrutalityRuntime.For(world)?.CutRequests.Where(cut => cut.Kind != GoreCutKind.PairedLimbAttempt && cut.Hit is not null)
            .Select(cut => GoreHitKey.From(cut.Hit!.Value)).ToHashSet() ?? [];
        while (pending.TryDequeue(out var work))
        {
            var hit = work.Hit; var contact = work.Contact;
            if (contact.Robot && cuts.Contains(GoreHitKey.From(hit))) continue; // Robot rupture FX own the same electrical/scrap channels; organic Hit spray and splinters accompany rupture in the pin.
            ImpactEffectKind effect = contact.Robot ? ImpactEffectKind.None : ImpactEffectKind.OrganicSpray;
            if (!contact.Robot && hit.Damage.Cut + hit.Damage.Pierce >= 15) effect |= ImpactEffectKind.OrganicSplinters;
            if (contact.Robot && hit.Source == CombatHitSource.Melee && float.IsFinite(hit.Damage.Cut) && hit.Damage.Cut > 0
                && (contact.Mechanism == CombatWoundMechanism.Slash || contact.Mechanism == CombatWoundMechanism.Unspecified && hit.CutDirection != 4))
                effect |= ImpactEffectKind.RobotShortCircuit;
            if (contact.Robot && contact.Rupture >= 0) effect |= ImpactEffectKind.RobotScrap;
            if (effect == ImpactEffectKind.None) continue;
            events.Enqueue(new(++sequence, hit, contact.Mechanism, contact.Execution, contact.Robot, contact.Colour, contact.Bone, contact.Height,
                VisualSeed(hit, world.Seed), effect) { RuptureKind = contact.Rupture });
            while (events.Count > Capacity) events.Dequeue();
        }
        foreach (var stale in contacts.Keys.Where(key => key.Tick < world.Tick - 1).ToArray()) contacts.Remove(stale);
        history.Enqueue(new(world.Tick + 1, Array.AsReadOnly(events.ToArray()))); while (history.Count > 64) history.Dequeue(); frames = history.ToArray();
    }
    static string Bone(BodyPartTemplate part, bool low) => part.Type switch
    {
        BodyPartType.Head => "Bip01 Head",
        BodyPartType.Torso => part.StringId == GorePartIds.Stomach ? "Bip01 Spine" : "Bip01 Spine2",
        _ => part.LimbSlot switch { 0 => low ? "Bip01 L Forearm" : "Bip01 L UpperArm", 1 => low ? "Bip01 R Forearm" : "Bip01 R UpperArm",
            2 => low ? "Bip01 L Calf" : "Bip01 L Thigh", 3 => low ? "Bip01 R Calf" : "Bip01 R Thigh", _ => "Bip01 Spine" },
    };
    static float Height(BodyPartTemplate part) => part.Type == BodyPartType.Head ? 17 : part.Type == BodyPartType.Torso
        ? part.StringId == GorePartIds.Stomach ? 11 : 14 : part.LimbSlot is 2 or 3 ? 6 : 12;
    static ulong VisualSeed(in CombatHit hit, ulong worldSeed)
    {
        return Effects.RobotVisualRandom.DischargeSeed(hit, worldSeed);
    }
}
