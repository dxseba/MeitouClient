namespace Meitou.StackSort.Search;

/// <summary>
/// One search's budget and knobs. The defaults are the original's compiled defaults for <c>PackAnnealed</c> (StackSort.md "Search"):
/// every budget is a count of iterations, restarts or steps, never a time.
/// </summary>
sealed record SearchParams
{
    /// <summary>
    /// Restarts of the late-acceptance climb (first pass 16, refine 24, sync 4).
    /// </summary>
    public int Restarts { get; init; } = 16;

    /// <summary>
    /// Iterations per restart (first pass 4000, refine 8000, sync 1000).
    /// </summary>
    public int Iterations { get; init; } = 4000;

    /// <summary>
    /// Length of the late-acceptance history (200).
    /// </summary>
    public int History { get; init; } = 200;

    /// <summary>
    /// A restart ends after this many iterations without a new best (first pass 1500, refine 2000).
    /// </summary>
    public int Plateau { get; init; } = 1500;

    /// <summary>
    /// The move thresholds on a roll of 0-99: below the first a swap, then a shift, then a turn, else a hole move (50, 75, 90).
    /// </summary>
    public int SwapBelow { get; init; } = 50;

    /// <summary>
    /// See <see cref="SwapBelow"/>.
    /// </summary>
    public int ShiftBelow { get; init; } = 75;

    /// <summary>
    /// See <see cref="SwapBelow"/>.
    /// </summary>
    public int TurnBelow { get; init; } = 90;

    /// <summary>
    /// The first item of a move is the latest of this many / 4 draws, except on a roll below <see cref="UniformPercent"/> (8 and 35: two draws,
    /// 65 % of the time).
    /// </summary>
    public int Bias { get; init; } = 8;

    /// <summary>
    /// See <see cref="Bias"/>.
    /// </summary>
    public int UniformPercent { get; init; } = 35;

    /// <summary>
    /// Score weights: grouping x concentration (1), concentration (50), the largest empty rectangle's squared area (1).
    /// </summary>
    public int GroupWeight { get; init; } = 1;

    /// <summary>
    /// See <see cref="GroupWeight"/>.
    /// </summary>
    public int ConcentrationWeight { get; init; } = 50;

    /// <summary>
    /// See <see cref="GroupWeight"/>.
    /// </summary>
    public int RectangleWeight { get; init; } = 1;

    /// <summary>
    /// The exponent of a same-record cluster's contact length, in quarters (6: length^1.5).
    /// </summary>
    public int ClusterExponent { get; init; } = 6;

    /// <summary>
    /// The share of the related-item (affinity) clusters in the grouping term, percent (50).
    /// </summary>
    public int AffinityShare { get; init; } = 50;

    /// <summary>
    /// The placement rule's weight of cells wasted under an item against its same-record contact (3).
    /// </summary>
    public int WasteWeight { get; init; } = 3;

    /// <summary>
    /// The elite orders kept for path relinking (8).
    /// </summary>
    public int Elites { get; init; } = 8;

    /// <summary>
    /// The first pass's compiled budget.
    /// </summary>
    public static SearchParams FirstPass { get; } = new();

    /// <summary>
    /// The refinement's compiled budget (24 x 8000, history 200, plateau 2000).
    /// </summary>
    public static SearchParams Refine { get; } = new() { Restarts = 24, Iterations = 8000, Plateau = 2000 };

    /// <summary>
    /// The synchronous fallback's cfg default (<c>sync_restarts</c> 4, <c>sync_iters</c> 1000).
    /// </summary>
    public static SearchParams Sync { get; } = new() { Restarts = 4, Iterations = 1000 };
}

/// <summary>
/// The budgets of a whole target sweep: the first pass, the refinement of the targets that need it, and the item count above which even an
/// already ideal layout is refined (<c>refine_item_threshold</c>, 20). <see cref="Refine"/> null: no refinement (the synchronous budget).
/// </summary>
sealed record SweepBudget(SearchParams FirstPass, SearchParams? Refine, int RefineItemThreshold = 20, int Workers = 1)
{
    /// <summary>
    /// The original's compiled first pass and refinement.
    /// </summary>
    public static SweepBudget Default { get; } = new(SearchParams.FirstPass, SearchParams.Refine);

    /// <summary>
    /// Text for the cache key: everything that changes the result (not <see cref="Workers"/>).
    /// </summary>
    public string Key() => $"{FirstPass}|{Refine}|{RefineItemThreshold}";
}
