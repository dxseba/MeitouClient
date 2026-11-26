using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The original's <c>nitoryu move</c> kinds (its <c>FCS.md</c>, read in the DLL's technique loader).
/// </summary>
enum MoveKind
{
    None = 0,
    /// <summary>A strike on the second blade's turn (an impact with <c>limb</c> 1 is a main-blade one).</summary>
    Strike = 1,
    /// <summary>A twin strike: one right-arm and one left-arm impact.</summary>
    Twin = 2,
    /// <summary>A special on a staggering enemy, one under full pressure, or just after a block.</summary>
    Finisher = 3,
    /// <summary>A special from a distance (<c>nitoryu min gap</c> to <c>max gap</c> beyond reach).</summary>
    Opener = 4,
}

/// <summary>
/// One of the private pool's techniques: a COMBAT_TECHNIQUE with <c>nitoryu move</c> set and no weapon flag (<c>animal</c> ATTACK_NULL, so the
/// game never picks it), by its index in the combat system's list, with the original's fields.
/// </summary>
sealed record Move(int Index, CombatTechnique Tech, MoveKind Kind)
{
    public bool BaseStrike { get; init; }
    public bool NeedsClip { get; init; }
    /// <summary>The original's own track laid over the technique (<c>nitoryu clip</c>), or null.</summary>
    public string? Track { get; init; }
    public int CooldownMs { get; init; }
    public float MinGap { get; init; }
    public float MaxGap { get; init; }
    public float Roll { get; init; } = 1;
    /// <summary>The X cut: a finisher by the original's rules though its record says twin.</summary>
    public bool XCut { get; init; }

    /// <summary>Whether any impact is the second blade's (<c>limb</c> 0, the left arm).</summary>
    public bool UsesSecondBlade
    {
        get
        {
            for (int blow = 1; blow <= Tech.Blows; blow++)
            {
                if (Tech.LimbOf(blow) == 0) return true;
            }

            return false;
        }
    }

    /// <summary>Hits with both blades (twins, specials): these share the double-hit cooldown.</summary>
    public bool DoubleHit => Kind is MoveKind.Twin or MoveKind.Finisher or MoveKind.Opener || XCut;
}

/// <summary>The private pool of a world's combat techniques.</summary>
sealed class MovePool
{
    public IReadOnlyList<Move> All { get; }

    readonly Dictionary<int, Move> _byIndex;

    MovePool(List<Move> moves)
    {
        All = moves;
        _byIndex = moves.ToDictionary(m => m.Index);
    }

    public Move? Of(int techniqueIndex) => _byIndex.GetValueOrDefault(techniqueIndex);

    /// <summary>
    /// The pool of <paramref name="techniques"/>: records with <c>nitoryu move</c> 1 to 4 and no weapon flag (a record that keeps one is ignored, as
    /// the original's log says), not disabled.
    /// </summary>
    public static MovePool Build(GameDatabase db, IReadOnlyList<CombatTechnique> techniques)
    {
        var moves = new List<Move>();
        for (int i = 0; i < techniques.Count; i++)
        {
            var t = techniques[i];
            if (t.Disabled || WeaponFlags(t) != WeaponKinds.None || db.Find(t.StringId) is not { } r) continue;

            int kind = r.GetInt("nitoryu move");
            if (kind is < 1 or > 4) continue;

            moves.Add(FromRecord(i, t, (MoveKind)kind, r));
        }

        return new MovePool(moves);
    }

    /// <summary>
    /// The weapon type flags of a technique. <c>1 handed</c> is left out: the shipped moves all set it, and the game does not match it against the
    /// weapon either.
    /// </summary>
    static WeaponKinds WeaponFlags(CombatTechnique t) => t.Kinds & ~WeaponKinds.OneHanded;

    static Move FromRecord(int index, CombatTechnique t, MoveKind kind, GameRecord r)
    {
        string clip = r.GetString("nitoryu clip");
        return new Move(index, t, kind)
        {
            BaseStrike = r.GetBool("nitoryu base strike"),
            NeedsClip = r.GetBool("nitoryu needs clip"),
            Track = clip.Length > 0 ? NitoryuData.TrackOf(clip) : null,
            CooldownMs = r.GetInt("nitoryu cooldown ms"),
            MinGap = r.GetFloat("nitoryu min gap"),
            MaxGap = r.GetFloat("nitoryu max gap"),
            Roll = r.GetFloat("nitoryu roll", 1),
            XCut = clip == "x_cut",
        };
    }
}
