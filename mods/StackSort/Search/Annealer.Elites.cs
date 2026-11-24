namespace Meitou.StackSort.Search;

/// <summary>
/// The elite orders and path relinking (StackSort.md "Elites"): when <see cref="SearchParams.UseElites"/> is on (the refinement and the
/// synchronous search; the first pass has it off), each new best and each restart's last order join up to <see cref="SearchParams.Elites"/>
/// elites (when full, the first worst is replaced by a better one); after the restarts every ordered pair of elites is relinked.
/// </summary>
sealed partial class Annealer
{
    readonly List<(int[] Order, long Score)> _elites = [];

    void AddElite(int[] order, long score)
    {
        if (_elites.Count < _p.Elites)
        {
            _elites.Add(((int[])order.Clone(), score));
            return;
        }

        int worst = 0;
        for (int i = 1; i < _elites.Count; i++)
        {
            if (_elites[i].Score < _elites[worst].Score) worst = i;
        }
        if (_elites[worst].Score < score) _elites[worst] = ((int[])order.Clone(), score);
    }

    /// <summary>
    /// For each ordered pair of elites, walks from the first toward the second.
    /// </summary>
    void Relink()
    {
        if (_elites.Count < 2) return;

        for (int a = 0; a < _elites.Count; a++)
        {
            for (int b = 0; b < _elites.Count; b++)
            {
                if (a != b) Walk((int[])_elites[a].Order.Clone(), _elites[b].Order);
            }
        }
    }

    /// <summary>
    /// At each position where the orders differ, swaps in the guide's item from further on and scores the order, keeping a new best; stops
    /// when the guide's item is not further on.
    /// </summary>
    void Walk(int[] order, int[] guide)
    {
        for (int k = 0; k < order.Length; k++)
        {
            if (order[k] == guide[k]) continue;

            int m = Array.IndexOf(order, guide[k], k + 1);
            if (m < 0) return;

            (order[k], order[m]) = (order[m], order[k]);
            long score = ScoreOf(order);
            if (score > _best.Score) SetBest(order, score);
        }
    }
}
