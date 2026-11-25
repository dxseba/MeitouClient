using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;

namespace Meitou.SentientSands;

/// <summary>
/// Opens the game's own windows from code, in a serial phase, through the paths its dialogue effects use (Meitou's internals, by reflection; a
/// missing member answers false and is logged by the caller):
/// <list type="bullet">
/// <item>the trade window (DA_TRADE): <c>Meitou.Simulation.Trading.OpenTradeNow</c> deferred to the next tick;</item>
/// <item>the character trading window (DA 41 / 42 / 48): <c>DialogueSystem._characterTrading.OpenAfter</c>, then <c>OpenCharacterTrade</c>.</item>
/// </list>
/// </summary>
static class GameWindows
{
    static readonly Type? OpenTradeNow = typeof(Simulation.Trading.OpenTrade).Assembly.GetType("Meitou.Simulation.Trading.OpenTradeNow");
    static readonly FieldInfo? Trading = typeof(DialogueSystem).GetField("_characterTrading", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>The trade window between the player's <paramref name="customer"/> and <paramref name="trader"/>, where they stand.</summary>
    public static bool Trade(World world, CharacterId customer, CharacterId trader)
    {
        if (OpenTradeNow is null || Activator.CreateInstance(OpenTradeNow, customer, trader) is not SimCommand command) return false;

        typeof(SimCommand).GetProperty(nameof(SimCommand.Tick))!.SetValue(command, world.Tick + 1);
        world.Defer(command);
        return true;
    }

    /// <summary>The character trading window of <paramref name="kind"/> with <paramref name="slaver"/>, for the player's <paramref
    /// name="player"/>.</summary>
    public static bool CharacterTrade(World world, CharacterTradeKind kind, CharacterId slaver, CharacterId player, int price = 0)
    {
        if (world.Systems.OfType<DialogueSystem>().FirstOrDefault() is not { } dialogue
            || Trading?.GetValue(dialogue) is not { } trading) return false;

        var type = trading.GetType();
        if (type.GetMethod("OpenAfter") is not { } after || type.GetMethod("OpenCharacterTrade") is not { } open) return false;

        after.Invoke(trading, [kind, slaver, player, price]);
        open.Invoke(trading, [world]);
        return true;
    }
}
