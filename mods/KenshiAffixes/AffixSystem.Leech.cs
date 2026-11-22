using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Leeching (the original's lifeSteal): of the victim's real health lost to a melee blow (each part's flesh - stun + rig, held to 0..maximum,
/// before and after, so overkill is not counted), the leech share (capped at 10%) heals the attacker's recoverable stun only, the part with the
/// most first; never cut wounds, stumps or crushed limbs. The health before is read as the blow lands (the hit filter), after when its hit is
/// told; the heal is made in the serial slow step of the same tick.
/// </summary>
sealed partial class AffixSystem
{
    readonly Dictionary<(CharacterId Attacker, CharacterId Victim, int Sequence, int Blow), (float[] Before, float Leech)> _leeches = [];
    readonly List<(CharacterId Attacker, float Budget)> _heals = [];

    void NoteLeech(World world, in PendingHit hit, float leech)
    {
        if (!world.Characters.TryResolveNext(hit.Victim, out int v) || world.Characters.Cold(v)?.Medical is not { } medical) return;

        _leeches[(hit.Attacker, hit.Victim, hit.Sequence, hit.Blow)] = ([.. medical.Parts.Select(Effective)], leech);
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (hit.Source != CombatHitSource.Melee || !_leeches.Remove((hit.Attacker, hit.Victim, hit.Sequence, hit.Blow), out var noted)) return;
        if (!world.Characters.TryResolveNext(hit.Victim, out int v) || world.Characters.Cold(v)?.Medical is not { } medical) return;

        float dealt = 0;
        for (int i = 0; i < noted.Before.Length && i < medical.Parts.Count; i++) dealt += Math.Max(0, noted.Before[i] - Effective(medical.Parts[i]));
        if (dealt > 0) _heals.Add((hit.Attacker, dealt * noted.Leech));
    }

    static float Effective(HealthPart p) => Math.Clamp(p.Flesh - p.Stun + p.Rig, 0, Math.Max(0, p.MaxHp));

    static float RecoverableStun(HealthPart p) => p.MaxHp <= 0 ? 0 : Math.Min(Math.Max(0, p.Stun), Math.Max(0, p.MaxHp - (p.Flesh - p.Stun + p.Rig)));

    /// <summary>The heals of this tick's blows; a blow blocked or dodged after its filter leaves its note, dropped here.</summary>
    void Heal(World world)
    {
        foreach (var (attacker, budget) in _heals)
        {
            var table = world.Characters;
            if (table.TryResolveNext(attacker, out int a) && table.Cold(a)?.Medical is { Dead: false } medical) HealStun(medical, budget);
        }

        _heals.Clear();
        _leeches.Clear();
    }

    static void HealStun(MedicalState medical, float budget)
    {
        while (budget > 0.00001f)
        {
            HealthPart? worst = null;
            float most = 0;
            foreach (var part in medical.Parts)
            {
                if (part.Limb is LimbState.Stump or LimbState.Crushed) continue;

                float missing = RecoverableStun(part);
                if (float.IsFinite(missing) && missing > most) (worst, most) = (part, missing);
            }

            if (worst is null) return;

            float used = Math.Min(budget, most);
            worst.Stun -= used;
            budget -= used;
        }
    }
}
