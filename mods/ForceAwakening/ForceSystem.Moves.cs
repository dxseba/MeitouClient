using System.Collections.Frozen;
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>
/// The skills' clips and casting circles. A skill with a strike animation (the 9th field of its <c>spell</c> line: the Sith's <c>heavy swing</c>,
/// <c>chop down</c>, <c>bigchopv2</c>) plays that game clip as it goes off; a skill with a cast time (Brainwash) plays the first clip of
/// <c>cast_animations</c> the data has while it is cast (<b>Verified</b>, ForceBase.cfg and the DLL's cast step; the class-only clips
/// <c>ARC_cast_staff</c>, <c>ARC_bow_reload</c> and Arcane Magic's <c>AM_*</c> are left out, as the original keeps them to their classes), and the
/// casting circle <c>ARC_cast_magic_circle</c> stands at the caster's feet (the DLL picks <c>cast_magic</c> for every kind past its seven mage
/// kinds), raised by the <c>circle</c> setting. Clips play through <c>AddCombatMove</c> (animation only), read from a dictionary this system
/// publishes in its serial step.
/// </summary>
sealed partial class ForceSystem
{
    /// <summary>How long a strike clip plays (the port's choice: the original plays it once at the clip's own speed).</summary>
    const float StrikeSeconds = 0.9f;

    public const string CastCircle = "ARC_cast_magic_circle";

    readonly Dictionary<CharacterId, SkillMove> _moves = [];
    volatile FrozenDictionary<CharacterId, SkillMove> _movesView = FrozenDictionary<CharacterId, SkillMove>.Empty;
    volatile FrozenDictionary<CharacterId, Vector3> _circles = FrozenDictionary<CharacterId, Vector3>.Empty;
    HashSet<string>? _clips;

    /// <summary>The casters now casting, with the point at their feet, for the frame's circle.</summary>
    public FrozenDictionary<CharacterId, Vector3> Circles => _circles;

    /// <summary>
    /// The <see cref="Meitou.Simulation.CombatMoveSource"/>: the skill clip a character plays at <paramref name="tick"/>, as of the last serial step.
    /// </summary>
    public CombatMove? MoveOf(World world, CharacterId id, long tick)
    {
        if (!_movesView.TryGetValue(id, out var move) || tick < move.Start || tick >= move.End) return null;

        return CombatMove.Clip(move.Clip, Math.Clamp((tick - move.Start) / (float)Math.Max(1, move.End - move.Start), 0, 1));
    }

    void Move(World world, CharacterId caster, CharacterCold cold, Spell spell)
    {
        if (spell.Strike is { } strike && HasClip(strike))
            _moves[caster] = new SkillMove(strike, world.Tick, world.Tick + Ticks(world, StrikeSeconds));
        else if (spell.CastTime > 0 && CastClip() is { } clip)
            _moves[caster] = new SkillMove(clip, world.Tick, world.Tick + Ticks(world, spell.CastTime));
    }

    string? CastClip() => _config.CastAnimations.FirstOrDefault(name =>
        !name.StartsWith("ARC_cast_staff", StringComparison.Ordinal) && !name.StartsWith("ARC_bow", StringComparison.Ordinal)
        && !name.StartsWith("AM_", StringComparison.Ordinal) && HasClip(name));

    bool HasClip(string name)
    {
        _clips ??= population.Db.OfType(FcsRecordType.Animation).Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        return _clips.Contains(name);
    }

    /// <summary>Drops ended clips, ends a cast clip whose cast stopped, and publishes the clips and the circles.</summary>
    void PublishMoves(World world)
    {
        foreach (var (id, move) in _moves.ToArray())
        {
            if (move.End <= world.Tick || !world.Characters.IsAlive(id)) _moves.Remove(id);
        }

        var table = world.Characters;
        var circles = new Dictionary<CharacterId, Vector3>();
        foreach (var t in _timed)
        {
            if (t.Kind == Lasting.Brainwash && t.Power >= 0 && table.TryResolveNext(t.By, out int s)) circles[t.By] = table.Next[s].Position;
        }

        foreach (var id in _moves.Keys.ToArray())
        {
            if (_moves[id].End - _moves[id].Start > Ticks(world, StrikeSeconds) && !circles.ContainsKey(id)) _moves.Remove(id);
        }

        _movesView = _moves.ToFrozenDictionary();
        _circles = circles.ToFrozenDictionary();
    }
}
