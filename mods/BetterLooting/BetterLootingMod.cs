using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.BetterLooting;

/// <summary>
/// Port of Better Looting (workshop 3816121198, GPL-3.0): with a character's inventory window open, Insert loots the bodies within the radius into
/// that character by the value, weight, category and theft rules; Alt+Insert stores its items in the player's storage. The press is polled in a
/// postfix on the interface's <c>BindUi</c> and travels as a <see cref="SeenDiscoveriesCommand"/> tagged in a side table, one of the interface
/// commands the paused world still applies (World.ApplyPausedInterfaceCommands), so it works paused; a prefix on
/// <c>PlayerSystem.ApplyViewCommand</c> runs it instead of the command's own effect.
/// </summary>
public sealed class BetterLootingMod : IMod
{
    internal static readonly LootSettings Settings = new();

    static readonly ConditionalWeakTable<SeenDiscoveriesCommand, LootRequest> Requests = new();

    static FieldInfo? s_sessionField;
    static bool s_insertDown;

    /// <summary>
    /// The mod's folder, where the original's <c>BetterLooting.ini</c> (the "Always take these items" list) is read at each press.
    /// </summary>
    static string s_folder = "";

    /// <summary>
    /// The mod's log.
    /// </summary>
    static Action<string> s_log = _ => { };

    public void Initialize(ModContext context)
    {
        s_folder = context.Folder;
        s_log = context.Log;
        context.AddSettings(Settings);
        context.AddSystem("looting", build => new LootSystem(build.Data), SystemAnchor.End);
        PatchKeyPoll(context);
        PatchCommand(context);
        context.Log("Better Looting initialized");
    }

    static void PatchKeyPoll(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(type.FullName, "WorldSession");

        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(BetterLootingMod), nameof(Bound)));
    }

    static void PatchCommand(ModContext context)
    {
        var apply = AccessTools.Method(typeof(PlayerSystem), "ApplyViewCommand")
            ?? throw new MissingMethodException("PlayerSystem", "ApplyViewCommand");
        context.Harmony.Patch(apply, prefix: new HarmonyMethod(typeof(BetterLootingMod), nameof(BeforeView)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance)
    {
        if (s_sessionField?.GetValue(__instance) is not WorldSession session) return;

        bool down = session.Input.IsHeld(LootKey);
        bool pressed = down && !s_insertDown;
        s_insertDown = down;
        if (!pressed || !Settings.Enabled) return;

        if (InventoryLead(__instance) is { } lead) Send(session, new LootRequest(lead, AltDown(session, __instance), Required()));
    }

    /// <summary>
    /// The key the Looting key setting names (Insert by default, the original's <c>ActivationVirtualKey</c> 0x2D).
    /// </summary>
    static Key LootKey => Enum.TryParse<Key>(Settings.LootingKey, out var key) ? key : Key.Insert;

    /// <summary>
    /// The "Always take these items" list from <c>BetterLooting.ini</c>, read again at each press as the original applies changes at once.
    /// </summary>
    static RequiredItems Required()
    {
        try
        {
            return RequiredItems.Parse(RequiredItems.ReadIni(s_folder), s_log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            s_log($"BetterLooting.ini unreadable: {e.Message}");
            return RequiredItems.None;
        }
    }

    static bool BeforeView(World world, SimCommand command, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (command is not SeenDiscoveriesCommand seen || !Requests.TryGetValue(seen, out var request)) return true;

        world.Systems.OfType<LootSystem>().FirstOrDefault()?.Run(world, request);
        __result = true;
        return false;
    }

    /// <summary>The character the open inventory window shows (<c>InventoryWindows.InventoryOwner</c>); null without one or while typing.</summary>
    static CharacterId? InventoryLead(object player)
    {
        var t = Traverse.Create(player);
        var screens = t.Field("FaithfulScreens");
        if (screens.Property("HasKeyboardFocus").GetValue<bool>() || !screens.Method("IsOpen", [typeof(string)]).GetValue<bool>("inventory"))
            return null;

        return t.Property("InventoryWindows").Method("InventoryOwner").GetValue() is CharacterSnapshot { IsPlayer: true } owner ? owner.Id : null;
    }

    static bool AltDown(WorldSession session, object player) =>
        session.Input.IsHeld(Key.LeftAlt) || session.Input.IsHeld(Key.RightAlt) || Traverse.Create(player).Field("AltHeld").GetValue<bool>();

    static void Send(WorldSession session, LootRequest request)
    {
        var command = new SeenDiscoveriesCommand { Tick = session.World.Tick };
        Requests.Add(command, request);
        session.World.Commands.Enqueue(command);
    }
}
