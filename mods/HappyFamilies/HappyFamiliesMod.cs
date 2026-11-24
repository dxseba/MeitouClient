using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.HappyFamilies;

/// <summary>
/// Port of Happy Families (Natural Childbirth) (workshop 3800064617, a KenshiLua script plus data): a couple in one bed building may have a child
/// that looks like both, inherits half their average stats and grows up over ten game days; F opens a family window. The original's
/// <c>HF_Random&lt;race&gt;</c> templates come from its own data mod, a required item.
/// </summary>
public sealed class HappyFamiliesMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("families", _ => new FamilySystem(context), SystemAnchor.End);

        var window = new FamilyWindow(context);
        context.AddUi(window);
        context.AddFrame(window.Follow);
        context.AddKeyAction("family-tree", "Family tree", key => window.Toggle(key.Selected), Binding.Of(Key.F));
        context.Log("HappyFamilies initialized");
    }
}
