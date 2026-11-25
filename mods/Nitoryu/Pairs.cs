using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>What a pair of blades gives (README "What you pair").</summary>
readonly record struct PairTraits(bool LongShort, bool Equal, bool Mixed);

/// <summary>
/// Which weapons may pair (README "How to use it", FCS.md "Weapons"): the WEAPON's <c>nitoryu dual wield</c> (1 always, 2 never, 0 by its
/// category), else the two vendor lists <c>Nitoryu weapons allowed / refused in a pair</c>, else the ini's <c>[Weapons] allow / refuse</c>, else its
/// <c>skill category</c> in <c>nitoryu weapon categories</c> (katanas and sabres as shipped). Heavy weapons, polearms and bows never pair, and the
/// second blade needs a <c>bare sword</c> mesh (else the pair fights with one).
/// </summary>
sealed class Pairs
{
    const string AllowedList = "51-Nitoryu.mod";
    const string RefusedList = "52-Nitoryu.mod";

    readonly GameDatabase _db;
    readonly NitoryuConfig _config;
    readonly HashSet<string> _allowed;
    readonly HashSet<string> _refused;
    readonly Dictionary<string, bool> _cache = [];
    readonly Lock _gate = new();

    public Pairs(GameDatabase db, NitoryuConfig config)
    {
        _db = db;
        _config = config;
        _allowed = [.. ListIds(db, AllowedList), .. Ids(config.IniRaw("weapons.allow"))];
        _refused = [.. ListIds(db, RefusedList), .. Ids(config.IniRaw("weapons.refuse"))];
    }

    static IEnumerable<string> ListIds(GameDatabase db, string list) =>
        db.Find(list)?.GetReferences("weapons").Select(r => r.TargetStringId) ?? [];

    static IEnumerable<string> Ids(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether the weapon may be one of a pair; with <paramref name="any"/> (the second button) blunt weapons and hackers too.</summary>
    public bool Allowed(WeaponData weapon, bool any) =>
        Allowed(weapon) || any && AnyKind(weapon) && !_refused.Contains(weapon.StringId) && _db.Find(weapon.StringId)?.GetInt("nitoryu dual wield") != 2;

    /// <summary>A blunt weapon or a hacker (the second button's kinds).</summary>
    public static bool AnyKind(WeaponData weapon) => weapon.SkillCategory is (int)WeaponCategory.Blunt or (int)WeaponCategory.Hackers;

    /// <summary>Whether the second button shows: back and hip hold two of katanas, sabres, blunt and hackers, one of them blunt or a hacker that
    /// does not pair by the rules alone.</summary>
    public bool AnyButton(Fighter fighter)
    {
        if (fighter.BackWeapon is not { } back || fighter.HipWeapon is not { } hip) return false;

        bool kinds = Kind(back.Data) && Kind(hip.Data);
        return kinds && (AnyKind(back.Data) && !Allowed(back.Data) || AnyKind(hip.Data) && !Allowed(hip.Data));

        static bool Kind(WeaponData w) => w.SkillCategory is (int)WeaponCategory.Katanas or (int)WeaponCategory.Sabres or (int)WeaponCategory.Blunt
            or (int)WeaponCategory.Hackers;
    }

    /// <summary>Whether the weapon may be one of a pair (cached per record).</summary>
    public bool Allowed(WeaponData weapon)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(weapon.StringId, out bool known)) return known;

            bool allowed = Decide(weapon);
            _cache[weapon.StringId] = allowed;
            return allowed;
        }
    }

    bool Decide(WeaponData weapon)
    {
        if (weapon.SkillCategory is (int)WeaponCategory.Heavy or (int)WeaponCategory.Polearms or (int)WeaponCategory.Bow) return false;

        int field = _db.Find(weapon.StringId)?.GetInt("nitoryu dual wield") ?? 0;
        if (field == 1 || _allowed.Contains(weapon.StringId)) return true;
        if (field == 2 || _refused.Contains(weapon.StringId)) return false;

        return _config.Categories.Contains(weapon.SkillCategory);
    }

    /// <summary>Whether the weapon has the left-hand blade mesh (<c>bare sword</c>).</summary>
    public bool HasBlade(WeaponData weapon) => _db.Find(weapon.StringId)?.GetPath("bare sword").Length > 0;

    /// <summary>The WEAPON's <c>nitoryu off hand damage</c> (1 when unset).</summary>
    public float OffHandFactor(WeaponData weapon) => _db.Find(weapon.StringId)?.GetFloat("nitoryu off hand damage", 1) ?? 1;

    /// <summary>
    /// The second blade of a fighter: the hip weapon while the back one is in hand, else the back one; null when the two do not pair or the second
    /// has no left-hand mesh.
    /// </summary>
    public (WeaponInstance Off, bool OffIsBack)? SecondBlade(Fighter fighter, bool any = false)
    {
        if (fighter.IsAnimal || fighter.Weapon is not { } main || fighter.BackWeapon is not { } back || fighter.HipWeapon is not { } hip) return null;

        var off = ReferenceEquals(main, back) ? hip : back;
        if (!HasBlade(off.Data)) return null;

        bool? rule = Addons.Rule(main.Data, off.Data);
        if (rule == false || rule is null && (!Allowed(main.Data, any) || !Allowed(off.Data, any))) return null;

        return (off, ReferenceEquals(off, back));
    }

    /// <summary>The pair's traits by the blades' lengths (<c>nitoryu pair length</c> where set) and kinds.</summary>
    public PairTraits Traits(WeaponData a, WeaponData b)
    {
        float la = Length(a), lb = Length(b);
        float ratio = MathF.Max(la, lb) > 0 ? MathF.Min(la, lb) / MathF.Max(la, lb) : 1;
        bool mixed = a.SkillCategory != b.SkillCategory;
        return new PairTraits(ratio <= _config.PairShortRatio, ratio >= _config.PairEqualRatio, mixed);
    }

    float Length(WeaponData w) => _db.Find(w.StringId)?.GetFloat("nitoryu pair length") is > 0 and var set ? set : w.Length;

    /// <summary>The ids of the records <paramref name="type"/> that tick <paramref name="field"/>.</summary>
    public static HashSet<string> Ticked(GameDatabase db, FcsRecordType type, string field) =>
        [.. db.OfType(type).Where(r => r.GetBool(field)).Select(r => r.StringId)];
}
