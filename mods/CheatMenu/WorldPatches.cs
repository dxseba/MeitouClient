using Meitou.Modding;

namespace Meitou.CheatMenu;

/// <summary>
/// The world and town cheats' host side. No Harmony patch is needed: the simulation side uses public calls, and the host side
/// (<see cref="HostBridge"/>) reaches the weather, camera and level editor by reflection from the host thread.
/// </summary>
internal static class WorldPatches
{
    public static void Install(ModContext context) => HostBridge.Install(context);
}
