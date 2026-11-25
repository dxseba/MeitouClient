using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;

namespace Meitou.SlaverMod;

/// <summary>The player's <see cref="Player"/> opens the slave trade with the slave trader <see cref="Slaver"/> (buying, or selling).</summary>
public sealed record SlaveTradeOrder(CharacterId Player, CharacterId Slaver, bool Selling) : SimCommand;

/// <summary>The trade window's Buy / Sell with the ticked characters.</summary>
public sealed record TradeConfirm(IReadOnlyList<CharacterId> Characters) : SimCommand;

/// <summary>The trade window's shackle kit button.</summary>
public sealed record BuyKitOrder : SimCommand;

/// <summary>The trade window's Close.</summary>
public sealed record CloseTradeWindow : SimCommand;

/// <summary>A row of the trade window: the character, its name, sex and price.</summary>
public sealed record TradeRow(CharacterId Character, string Name, bool Female, long Price);

/// <summary>The open trade window as the interface shows it (made in the serial step).</summary>
public sealed record TradeView(long Id, CharacterId Player, CharacterId Slaver, string SlaverName, bool Selling, IReadOnlyList<TradeRow> Rows,
    long Money, long KitPrice);

/// <summary>
/// The original's own slave trade window (<c>TradeWindow=1</c>): buying lists the trader's faction's slaves within the trade radius of it, selling
/// the player's slaves within it; prices by the original's worth; bought slaves join the slave squad shackled, sold ones leave as the trader's.
/// </summary>
sealed partial class SlaverSystem
{
    long _tradeId;

    /// <summary>The open window (null: none).</summary>
    internal static volatile TradeView? Trade;

    void OpenTrade(World world, SlaveTradeOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Player, out int p) || !table.TryResolveNext(order.Slaver, out int s) || table.Cold(s) is not { } slaver
            || Distance(world, p, s) > settings.TradeRadius)
        {
            world.Post("Too far away.");
            return;
        }

        if (!settings.TradeWindow)
        {
            var kind = order.Selling ? Simulation.Dialogue.CharacterTradeKind.SlaveSelling : Simulation.Dialogue.CharacterTradeKind.SlaveBuying;
            if (!GameWindows.CharacterTrade(world, kind, order.Slaver, order.Player)) log("the character trading window could not be opened");
            return;
        }

        var rows = order.Selling ? SellRows(world, s) : BuyRows(world, s, slaver);
        if (rows.Count == 0) world.Post(order.Selling ? "You have no slaves near the trader to sell." : "The trader has no slaves for sale here.");

        Trade = new TradeView(++_tradeId, order.Player, order.Slaver, slaver.Name, order.Selling, rows, world.Player.Money,
            (long)settings.ShackleKitPrice);
    }

    List<TradeRow> BuyRows(World world, int s, CharacterCold slaver)
    {
        var table = world.Characters;
        var rows = new List<TradeRow>();
        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is not { SlaveState: 1, Medical.Dead: false } c || c.IsPlayer || c.SlaveOwner != slaver.Faction) continue;
            if (Distance(world, i, s) > settings.TradeRadius) continue;

            rows.Add(Row(table.IdOf(i), c, Race(c).Start ?? settings.SubmissionStart, settings.TradeBuyMult));
        }

        return rows;
    }

    List<TradeRow> SellRows(World world, int s)
    {
        var rows = new List<TradeRow>();
        foreach (var (lasting, record) in _state.Slaves)
        {
            if (Resolve(world, lasting, out int i, out var c) && PlayerSlave(c) && Distance(world, i, s) <= settings.TradeRadius)
                rows.Add(Row(world.Characters.IdOf(i), c, record.Submission, settings.TradeSellMult));
        }

        return rows;
    }

    TradeRow Row(CharacterId id, CharacterCold c, float submission, float mult) =>
        new(id, c.Name, c.Appearance?.Female == true, (long)MathF.Round(Worth(c, submission) * mult));

    /// <summary>
    /// The original's worth: (<c>TradeBaseValue</c> + <c>TradeStatValue</c> x (strength + toughness + dexterity + melee attack + melee defence +
    /// labouring)) x the race's Value x (0.75 + submission / 200), less <c>TradeMissingLimbPenalty</c> per arm or leg gone or crushed (at least 10%
    /// of it left).
    /// </summary>
    float Worth(CharacterCold c, float submission)
    {
        var st = c.Stats;
        float stats = st is null ? 0 : st.Strength + st[StatsEnumerated.Toughness] + st.Dexterity + st[StatsEnumerated.MeleeAttack]
            + st[StatsEnumerated.MeleeDefence] + st[StatsEnumerated.Labouring];
        float worth = (settings.TradeBaseValue + settings.TradeStatValue * stats) * Race(c).Value * (0.75f + submission / 200);
        int missing = c.Medical?.Parts
            .Count(p => p.Template.LimbSlot >= 0 && p.Replacement is null && p.Limb is LimbState.Stump or LimbState.Crushed) ?? 0;
        return worth * MathF.Max(0.1f, 1 - settings.TradeMissingLimbPenalty * missing);
    }

    void Confirm(World world, TradeConfirm confirm)
    {
        if (Trade is not { } view) return;

        var rows = view.Rows.Where(r => confirm.Characters.Contains(r.Character)).ToList();
        if (rows.Count == 0)
        {
            world.Post("Click the slaves you want first.");
            return;
        }

        foreach (var row in rows)
        {
            if (view.Selling) Sell(world, view, row);
            else if (!Buy(world, view, row)) break;
        }

        OpenTrade(world, new SlaveTradeOrder(view.Player, view.Slaver, view.Selling) { Tick = world.Tick });
    }

    bool Buy(World world, TradeView view, TradeRow row)
    {
        if (world.Player.Money < row.Price)
        {
            world.Post($"Not enough money to buy {row.Name}");
            return false;
        }

        var table = world.Characters;
        if (!table.TryResolveNext(row.Character, out int s) || table.Cold(s) is not { SlaveState: 1 } c) return true;

        ulong lasting = Lasting(world, row.Character);
        float start = (Race(c).Start ?? settings.SubmissionStart) + settings.TradeBoughtBonus;
        if (!MakeSlave(world, s, c, lasting, Math.Clamp(start, 0, 100), FactionId(c.Faction)))
        {
            world.Post($"The game refused the purchase of {row.Name}");
            return true;
        }

        world.Player.Money -= row.Price;
        if (LockSystem.WornShackles(c) is null && population.Items.Create(settings.ShackleItem) is { Lock: not null } shackles)
        {
            shackles.Lock.Locked = true;
            if (Simulation.Items.InventoryCommands.TryEquip(c.Inventory!, shackles, c))
                CharacterRefresh.Refresh(population, c, CharacterRefreshKind.Equipment);
        }

        Say(world, row.Character, "BoughtSlave");
        world.Post($"bought {row.Name} for {row.Price}");
        return true;
    }

    /// <summary>The slave leaves for the trader's squad as a slave of the trader's faction; the player is paid.</summary>
    void Sell(World world, TradeView view, TradeRow row)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(row.Character, out int s) || table.Cold(s) is not { } c || !PlayerSlave(c)) return;
        if (!table.TryResolveNext(view.Slaver, out int t) || table.Cold(t) is not { SquadId: >= 0 } slaver
            || world.Squads.Find(slaver.SquadId) is not { } squad)
            return;

        _state.Slaves.Remove(Lasting(world, row.Character));
        Say(world, row.Character, "SoldSlave");
        JoinNpcSquad(world, s, c, slaver.Faction, squad);
        c.Role = (int)SquadRole.Slave;
        FixtureRegistry.Enslave(world, c, slaver.Faction, squad.Id);
        world.Player.Money += row.Price;
        world.Post($"sold {row.Name} for {row.Price}");
    }

    void BuyKit(World world, BuyKitOrder _)
    {
        if (Trade is not { } view) return;

        long price = (long)settings.ShackleKitPrice;
        if (world.Player.Money < price)
        {
            world.Post("Not enough money for a shackle kit.");
            return;
        }

        if (!world.Characters.TryResolveNext(view.Player, out int p) || world.Characters.Cold(p)?.Inventory is not { } inv) return;
        if (population.Items.Create(settings.ShackleKitItem) is not { } kit)
        {
            world.Post("The shackle kit item is missing from the game data.");
            return;
        }

        inv.Add(kit);
        world.Player.Money -= price;
        world.Post($"{world.Characters.Cold(p)!.Name} bought a shackle kit.");
        Trade = view with { Money = world.Player.Money };
    }
}
