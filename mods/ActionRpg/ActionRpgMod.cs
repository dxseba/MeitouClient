using Meitou.DirectControl;
using Meitou.Modding;

namespace Meitou.ActionRpg;

/// <summary>
/// Port of Action RPG (RE_Kenshi) v328 (cjy233, workshop 3824431439; GPLv3), the fork of Direct Control 1.5.0: V mode with the shoulder and
/// first-person camera, WASD at a walk (Shift runs), mouse combat (draw a direction with the left button and release to swing, hold the right
/// button to block in the direction last drawn) and a lock-on. Built on the Direct Control port's core with the fork's scheme and defaults
/// (<see cref="ControlSettings.ForActionRpg"/>); README "Port choices".
/// </summary>
public sealed class ActionRpgMod : IMod
{
    public void Initialize(ModContext context)
    {
        OriginalTexts.Add(context);
        ControlCore.Start(context, ControlSettings.ForActionRpg());
        context.Log("Action RPG initialized");
    }
}
