using System.Collections.ObjectModel;
using Meitou.Simulation;

namespace Meitou.Brutality;

public sealed record BodyWoundFrame(long Tick, IReadOnlyDictionary<CharacterId, BrutalityActorView> Characters);

public sealed partial class BrutalityGameplaySystem
{
    readonly Queue<BodyWoundFrame> woundHistory = [];
    volatile BodyWoundFrame[] woundFrames = [];
    long woundHealStep;

    public BodyWoundFrame? WoundFrameAt(long tick)
    {
        var frames = woundFrames;
        for (int i = frames.Length - 1; i >= 0; i--) if (frames[i].Tick <= tick) return frames[i];
        return null;
    }
    static BrutalityDurableState Durable(string key, Actor actor) => new(key, actor.Progress.Experience, actor.Severe, actor.Head, actor.Torso, actor.Limbs.ToArray())
    { Wounds = actor.Wounds.Snapshot(), ChestHole = actor.ChestHole };
    void HealBodyWounds(World world)
    {
        if (world.Tick / 3000 == woundHealStep) return;
        woundHealStep = world.Tick / 3000;
        var known = new HashSet<Actor>();
        foreach (var actor in actors.Values) Heal(actor);
        foreach (var stored in world.Platoons.All.Where(platoon => platoon.Stored is not null).SelectMany(platoon => platoon.Stored!.Members))
            if (retained.TryGetValue(stored.Cold, out var actor)) Heal(actor);
        foreach (var (key, pending) in unresolved.ToArray())
            if (pending.Wounds.HasAny || pending.Wounds.BellyHits > 0)
            {
                var body = WoundBody.From(pending.Wounds);
                unresolved[key] = pending with { Wounds = body.Heal(.01f) ? body.Snapshot() : BodyWoundState.Empty };
                changed = true;
            }
        void Heal(Actor actor)
        {
            if (!known.Add(actor) || !actor.Wounds.Snapshot().HasAny && actor.Wounds.BellyHits == 0) return;
            if (!actor.Wounds.Heal(.01f)) actor.Wounds = new();
            changed = true;
        }
    }
    void PublishBodyWounds(World world)
    {
        var states = ActorViews.Where(view => view.Wounds.HasAny || view.Wounds.BellyHits > 0 || view.ChestHole is not null
            || view.SevereHead.Kind != SevereHeadWoundKind.None || view.Head is not null || view.Torso is not null || view.Limbs.Count > 0)
            .ToDictionary(view => view.Character);
        if (states.Count == 0 && woundHistory.Count == 0) return;
        woundHistory.Enqueue(new(world.Tick + 1, new ReadOnlyDictionary<CharacterId, BrutalityActorView>(states)));
        while (woundHistory.Count > 64) woundHistory.Dequeue();
        woundFrames = woundHistory.ToArray();
    }
}
