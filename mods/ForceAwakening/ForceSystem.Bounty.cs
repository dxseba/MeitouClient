using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>
/// The Veteran Hunter's contracts (<b>Verified</b>, decompile: the plugin's <c>Dialogue::say</c> and line-condition hooks and its contract check):
/// a dialogue line whose string field <c>arc_bounty</c> is <c>none</c> shows only while no contract is held, <c>has</c> only while one is; when the
/// NPC says a line with <c>arc_open</c> = <c>bounty_new</c>, a held contract is told again, else one of the <c>bounty=</c> lines not yet done is
/// drawn at random (none left: <c>ST_BOUNTY_NONE</c>); <c>bounty_drop</c> drops it. Every check (the port: every 2 s) a non-player character of the
/// contract's record that is dead or down with a living player character within 600 pays the cats at once and marks the contract done. One
/// contract for the player's faction, saved.
/// </summary>
sealed partial class ForceSystem
{
    const float BountyReach = 600;

    /// <summary>The contract's character record; null for none.</summary>
    string? _contract;

    /// <summary>The contracts paid, by character record.</summary>
    readonly SortedSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>Whether a line's <c>arc_bounty</c> condition passes (no such field: it does).</summary>
    public bool BountyLinePasses(string? condition) => condition switch
    {
        "none" => _contract is null,
        "has" => _contract is not null,
        _ => true,
    };

    /// <summary>An NPC says a line with <c>arc_open</c>: the contract's new draw, or its drop.</summary>
    public void BountyAction(World world, string action)
    {
        if (action == "bounty_new") NewBounty(world);
        else if (action == "bounty_drop" && _contract is not null)
        {
            _contract = null;
            world.Post(ForceTexts.T("ST_BOUNTY_DROPPED"));
        }
    }

    void NewBounty(World world)
    {
        if (_contract is null)
        {
            var open = _config.Bounties.Where(b => !_done.Contains(b.Character) && population.Db.Find(b.Character) is not null).ToList();
            if (open.Count == 0)
            {
                world.Post(ForceTexts.T("ST_BOUNTY_NONE"));
                return;
            }

            int pick = (int)(Rng.Hash(world.Seed, 0xB0B7, purpose, (ulong)world.Tick) % (ulong)open.Count);
            _contract = open[pick].Character;
        }

        var (character, cats, where) = _config.Bounties.Find(b => b.Character == _contract);
        world.Post(ForceTexts.T("ST_BOUNTY_ON", BountyName(character), $"{cats}", where.Length > 0 ? where : ForceTexts.T("BOUNTY_WILDS")));
    }

    string BountyName(string record) => population.Db.Find(record)?.Name ?? record;

    void CheckBounty(World world)
    {
        if (_contract is not { } record) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { IsPlayer: false, Medical: { Incapacitated: true } } cold || cold.RecordId != record) continue;
            if (!PlayerNear(world, Flat2(table.Next[slot].Position), BountyReach)) continue;

            long cats = _config.Bounties.Find(b => b.Character == record).Cats;
            world.Player.Money += cats;
            _done.Add(record);
            _contract = null;
            world.Post(ForceTexts.T("ST_BOUNTY_PAID", BountyName(record), $"{cats}"));
            return;
        }
    }

    static System.Numerics.Vector2 Flat2(System.Numerics.Vector3 v) => new(v.X, v.Z);

    void WriteBounty(BinaryWriter w)
    {
        w.Write(_contract ?? "");
        w.Write(_done.Count);
        foreach (string d in _done) w.Write(d);
    }

    void ReadBounty(BinaryReader r)
    {
        string contract = r.ReadString();
        _contract = contract.Length > 0 ? contract : null;
        _done.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) _done.Add(r.ReadString());
    }
}
