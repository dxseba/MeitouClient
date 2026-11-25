using Meitou.Modding;

namespace Meitou.DirectControl;

/// <summary>
/// Port of Direct Control (Re_Kenshi) 1.5.1 (smokefoolius, workshop 3737240806; GPLv3): V drives the selected character with WASD, with a
/// first-person and a third-person view and manual combat (Attack, Block with a timed perfect block, Dodge). README "Facts" for what follows the
/// original's DLL and ini, "Port choices" for the differences.
/// </summary>
public sealed class DirectControlMod : IMod
{
    public void Initialize(ModContext context)
    {
        ControlCore.Start(context, new ControlSettings());
        context.Log("Direct Control initialized");
    }
}
