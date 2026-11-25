using System.Text.Json;
using Meitou.Simulation;

namespace Meitou.SlaverMod;

/// <summary>
/// One of the player's slaves (keyed by its <see cref="LastingId"/>): submission 0..100, the faction it was taken from (string id), and the game
/// hours of its last punishments and reward.
/// </summary>
sealed class SlaveRecord
{
    public float Submission { get; set; }
    public string Origin { get; set; } = "";
    public double PunishedAt { get; set; } = double.NegativeInfinity;
    public int Punishments { get; set; }
    public double RewardedAt { get; set; } = double.NegativeInfinity;
}

/// <summary>
/// The slave caravan: its phase, the game hours of its next step, its platoon's tag and whether the warning was given.
/// </summary>
sealed class CaravanState
{
    public CaravanPhase Phase { get; set; }
    public double NextAt { get; set; } = -1;
    public string Tag { get; set; } = "";
    public int Visits { get; set; }
    public bool Warned { get; set; }
}

/// <summary>Where the caravan stands: none (waiting for the next visit), coming, staying, leaving.</summary>
enum CaravanPhase : byte { None, Coming, Staying, Leaving }

/// <summary>
/// What the port keeps per save (the original's <c>saves/</c> files and <c>SlaverMod_origins.ini</c>): the slaves, the runaways (their submission
/// when they ran, for a recapture), and the enslavements under way (target, owner, the tick they give up).
/// </summary>
sealed class SlaverState
{
    public SortedDictionary<ulong, SlaveRecord> Slaves { get; set; } = [];
    public SortedDictionary<ulong, SlaveRecord> Runaways { get; set; } = [];
    public SortedDictionary<ulong, (ulong Owner, long Until)> Pending { get; set; } = [];
    public ulong Draws { get; set; }
    public int Hour { get; set; } = int.MinValue;

    /// <summary>Rebels (lasting ids): never rejoin through dialogue while <c>RebelsDistrust</c> is on.</summary>
    public SortedSet<ulong> Rebels { get; set; } = [];

    /// <summary>The game day of the next rebellion check (-1: not set yet).</summary>
    public int NextRebellionDay { get; set; } = -1;

    /// <summary>Slaves the night cages hold (lasting ids), let out in the morning.</summary>
    public SortedSet<ulong> Caged { get; set; } = [];

    /// <summary>The slave caravan.</summary>
    public CaravanState Caravan { get; set; } = new();

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    public static SlaverState Deserialize(Stream stream) => JsonSerializer.Deserialize<SlaverState>(stream, Options) ?? new SlaverState();

    static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
}
