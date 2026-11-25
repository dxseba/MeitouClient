using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.ForceAwakening;

/// <summary>A cast's display: its particle systems on a character (or at a point) and the light it gives the character.</summary>
sealed record FxEvent((string System, float Seconds)[] Systems, CharacterId On, Vector3 At, Vector3 Light);

/// <summary>
/// The casts' particles and lights, queued by the tick and played on the host's frame (<see cref="ModContext.PlayEffect(string, EffectAnchor,
/// float, Vector3?)"/> needs the host's thread): each of the kind's Particle Universe systems that is loaded (the original's
/// <c>fx/arcanista_fx.pu</c>, read with its data mod), and a short glow on the character as the runtime light. Display only: never read by the tick.
/// </summary>
sealed class ForceFx(ModContext context, ForceSettings settings)
{
    const int MaxQueued = 256;

    readonly ConcurrentQueue<FxEvent> _queue = new();

    public void Post(FxEvent fx)
    {
        if (_queue.Count < MaxQueued) _queue.Enqueue(fx);
    }

    /// <summary>The circles kept up last frame, by caster.</summary>
    readonly Dictionary<CharacterId, EffectAnchor> _circles = [];

    public void Frame(ModFrame frame)
    {
        Circles();
        while (_queue.TryDequeue(out var fx))
        {
            try
            {
                Play(fx);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                context.LogError($"effect not shown: {e.Message}");
            }
        }
    }

    /// <summary>
    /// The casting circle (<c>ARC_cast_magic_circle</c>) under each caster casting now, kept up frame by frame and stopped when the cast ends; the
    /// <c>circle</c> setting raises it to the knees (5) or the waist (10), or hides it (the heights are the port's).
    /// </summary>
    void Circles()
    {
        var now = settings.Circle == "off" || !context.HasEffect(ForceSystem.CastCircle) || ForceAwakeningMod.Running is not { } system
            ? System.Collections.Frozen.FrozenDictionary<CharacterId, Vector3>.Empty
            : system.Circles;
        float lift = settings.Circle switch { "knees" => 5, "waist" => 10, _ => 0.5f };
        foreach (var (id, anchor) in _circles.ToArray())
        {
            if (now.ContainsKey(id)) continue;

            context.StopEffect(ForceSystem.CastCircle, anchor);
            _circles.Remove(id);
        }

        foreach (var (id, at) in now)
        {
            if (!_circles.TryGetValue(id, out var anchor)) _circles[id] = anchor = EffectAnchor.At(at + new Vector3(0, lift, 0));
            context.KeepEffect(ForceSystem.CastCircle, anchor, 2);
        }
    }

    void Play(FxEvent fx)
    {
        var anchor = fx.On.IsNone ? EffectAnchor.At(fx.At) : EffectAnchor.Bone(fx.On, "Bip01 Spine2");
        foreach (var (system, seconds) in fx.Systems)
        {
            if (context.HasEffect(system)) context.PlayEffect(system, anchor, Math.Clamp(seconds, 0.1f, 30));
        }

        if (!fx.On.IsNone) context.Glow(GlowTarget.Character(fx.On), fx.Light, 0.8f, 0.6f);
    }
}

/// <summary>The status effects that show a running skill on the HUD's State line (their modifiers are per stack: a stack per point of power).</summary>
sealed class ForceEffects(ModContext context)
{
    public StatusEffect Deflect { get; } = context.AddStatusEffect(new StatusEffect("force.deflect", Name("SHORT_deflect", "Deflect"))
    {
        Tone = StatusEffectTone.Good, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100, Description = Text("DESC_deflect"),
    });

    public StatusEffect DarkRage { get; } = context.AddStatusEffect(new StatusEffect("force.dark_rage", Name("SHORT_dark_rage", "Dark Rage"))
    {
        Tone = StatusEffectTone.Good, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100, Description = Text("DESC_dark_rage"),
    });

    public StatusEffect Meditation { get; } = context.AddStatusEffect(new StatusEffect("force.meditation", Name("SHORT_battle_meditation", "Battle Meditation"))
    {
        Tone = StatusEffectTone.Good, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100, Description = Text("DESC_battle_meditation"),
        Modifiers = [new(StatsEnumerated.MeleeAttack, 1), new(StatsEnumerated.MeleeDefence, 1)],
    });

    public StatusEffect Hunt { get; } = context.AddStatusEffect(new StatusEffect("force.hunt", Name("SPELL_526", "Smuggler's Luck"))
    {
        Tone = StatusEffectTone.Good, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100,
        Modifiers = [new(StatsEnumerated.Athletics, 1), new(StatsEnumerated.Dodge, 1)],
    });

    public StatusEffect Calm { get; } = context.AddStatusEffect(new StatusEffect("force.calm", Name("SHORT_mind_trick", "Mind Trick"))
    {
        Tone = StatusEffectTone.Neutral, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100, Description = Text("DESC_command"),
    });

    public StatusEffect Choke { get; } = context.AddStatusEffect(new StatusEffect("force.choke", Name("SHORT_force_choke", "Force Choke"))
    {
        Tone = StatusEffectTone.Bad, Stacking = StatusEffectStacking.Refresh, MaxStacks = 100, Description = Text("DESC_force_choke"),
    });

    public IEnumerable<StatusEffect> All => [Deflect, DarkRage, Meditation, Hunt, Calm, Choke];

    static string Name(string key, string fallback)
    {
        string text = ForceTexts.Has(key) ? ForceTexts.T(key) : fallback;
        return text.Length is > 0 and <= 32 ? text : fallback;
    }

    static string Text(string key) => ForceTexts.Has(key) ? ForceTexts.T(key) : "";
}
