using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;

namespace Meitou.QuestsContracts;

/// <summary>The overlay key is held (<see cref="On"/>) around <see cref="Centre"/>, the ground under the camera.</summary>
public sealed record OverlayHeld(bool On, Vector3 Centre) : SimCommand;

/// <summary>What the overlay puts over a character (the original's <c>MarkersKind</c>): none, a talker, a giver, a job target.</summary>
enum MarkKind : byte { None, Talker, Giver, Target }

/// <summary>
/// The overlay (the original's H): while the key is held, every character within the radius of the ground under the camera who is not the
/// player's and not dead gets a mark: a locked job target "!!", a giver with something to give "!", anyone the player can talk to "...". The
/// marks are worked out in the serial step and published for the interface.
/// </summary>
sealed partial class LedgerSystem
{
    bool _overlay;
    Vector3 _overlayCentre;

    /// <summary>The overlay's marks (a copy swapped whole in the serial step).</summary>
    internal static volatile IReadOnlyDictionary<CharacterId, MarkKind> Marks = new Dictionary<CharacterId, MarkKind>();

    void Overlay(OverlayHeld held)
    {
        (_overlay, _overlayCentre) = (held.On, held.Centre);
        if (!held.On) Marks = new Dictionary<CharacterId, MarkKind>();
    }

    void MarkTick(World world)
    {
        if (!_overlay) return;

        var table = world.Characters;
        var dialogue = world.Systems.OfType<DialogueSystem>().FirstOrDefault();
        var marks = new Dictionary<CharacterId, MarkKind>();
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { IsPlayer: false, Medical.Dead: false } c) continue;
            if (Vector3.Distance(table.Next[i].Position, _overlayCentre) > _overlayRadius) continue;

            var kind = Kind(world, dialogue, i, c);
            if (kind != MarkKind.None) marks[table.IdOf(i)] = kind;
        }

        Marks = marks;
    }

    MarkKind Kind(World world, DialogueSystem? dialogue, int slot, CharacterCold c)
    {
        ulong lasting = world.LastingIds.GetOrGive(world.Characters.IdOf(slot)).Value;
        if (_state.Slots.Any(s => s.Target == lasting && s.State != 2)) return MarkKind.Target;
        if (IsGiver(world, c.RecordId)) return MarkKind.Giver;

        return dialogue?.CanTalk(world, slot) == true ? MarkKind.Talker : MarkKind.None;
    }

    /// <summary>
    /// A barman always; a debtor while no escort runs or rests and no target job is open; the caravan boss while none runs or rests.
    /// </summary>
    bool IsGiver(World world, string record)
    {
        if (!_tables.Givers.TryGetValue(record, out int kind)) return false;

        return kind switch
        {
            8 => RideSlot(8) is null && _state.EscortRestDay <= _day && !_state.Slots.Any(s => s.Targets.Count > 0 && s.State != 2),
            9 => RideSlot(9) is null && _state.CaravanRestHours <= world.GameHours,
            13 => _scoutHere != 0 || MarkOffer() != 0,
            _ => true,
        };
    }
}

/// <summary>The readout (the original's F10 panel) and the Ledger's Bounties tab, made in the serial step.</summary>
sealed partial class LedgerSystem
{
    IReadOnlyList<string> Readout()
    {
        var lines = new List<string>
        {
            $"Day {_day}  town {(_ctx?.Town is { Length: > 0 } t ? t : "-")}  rep {_ctxRep}  jobs {TownJobs(_ctx ?? new Who("", "", ""))}",
            $"offer {_offer}  special {_offerSpecial}  bounty {_bountyOffer}/{_bountyOffer2}  heist {_heistOffer}  rescue {_rescueOffer}",
            $"escort route {_escortRoute}  caravan route {_caravanRoute}  scout {_scoutHere}/{_scoutFound}  rescue here {_rescueHere}/{_rescueFound}",
        };
        foreach (var s in _state.Slots)
        {
            string target = s.TargetName.Length > 0 ? $" -> {s.TargetName}" : "";
            lines.Add($"job {s.Kind} {s.Type} state {s.State} for {s.Giver.Npc} ({s.Giver.Town}){target}");
        }

        return lines;
    }

    IReadOnlyList<string> BountyLines()
    {
        var lines = new List<string>();
        foreach (var b in _tables.Bounties)
        {
            if (!_state.Posters.TryGetValue(b.Poster, out var t) || t.ReadDay < 0) continue;

            string seen = t.SeenDay >= 0 ? $", seen in {t.SeenTown} on day {t.SeenDay}" : "";
            string state = t.ClaimedDay >= 0 ? $"claimed on day {t.ClaimedDay}" : $"{b.Reward} cats, {b.Faction}";
            lines.Add($"{b.Name}: {state}{seen}");
        }

        return lines;
    }
}

/// <summary>
/// The map's quest dots and the readout's faces (the original sends them to the Atlas and paints portraits), made in the serial step.
/// </summary>
sealed partial class LedgerSystem
{
    IReadOnlyList<(Vector2 At, string Label, bool Person)> Dots(World world)
    {
        var dots = new List<(Vector2, string, bool)>();
        foreach (var s in _state.Slots)
        {
            if (s.Target != 0 && world.Characters.TryResolveNext(world.LastingIds.Find(new LastingId(s.Target)), out int t))
            {
                var p = world.Characters.Next[t].Position;
                dots.Add((new(p.X, p.Z), s.TargetName, true));
            }
            else if (s.Kind is 12 or 13 or 14 && _population.Sites.FirstOrDefault(x => x.Town.Name == s.DestName) is { } site)
            {
                dots.Add((new(site.Position.X, site.Position.Z), $"{s.Type}: {s.DestName}", false));
            }
        }

        return dots;
    }

    IReadOnlyList<CharacterId> Faces(World world) =>
        [.. _state.Slots.Where(s => s.Target != 0).Select(s => world.LastingIds.Find(new LastingId(s.Target))).Where(id => !id.IsNone).Take(4)];
}
