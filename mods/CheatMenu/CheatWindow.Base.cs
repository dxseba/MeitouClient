using Meitou.Game.Ui;

namespace Meitou.CheatMenu;

/// <summary>
/// The Base and research page's rows after Finish buildings, in the original's order: Free research, Free construction, Generators, Fill
/// storages, Grow farms; and Ruin buildings / Repair ruins (the original's Experimental "Towns and buildings" rows).
/// </summary>
internal sealed partial class CheatWindow
{
    IEnumerable<UiNode> BaseRows()
    {
        yield return Switch(CheatMenu.Toggle.FreeResearch, "Free research (no artifacts)");
        yield return Switch(CheatMenu.Toggle.FreeConstruction, "Free construction (no materials)");
        yield return Switch(CheatMenu.Toggle.Generators, "Generators: full power, no fuel");
        yield return Do("Fill storages near camera");
        yield return Do("Grow farms near camera");
        yield return Do("Ruin buildings near camera");
        yield return Do("Repair ruins near camera");
    }

    bool BaseAct(string english)
    {
        BaseDeedKind? which = english switch
        {
            "Fill storages near camera" => BaseDeedKind.FillStorages,
            "Grow farms near camera" => BaseDeedKind.GrowFarms,
            "Ruin buildings near camera" => BaseDeedKind.Ruin,
            "Repair ruins near camera" => BaseDeedKind.Repair,
            _ => null,
        };
        if (which is not { } kind) return false;

        Send(new BaseDeed(kind, _camera) { Tick = T });
        Say(Tr(english));
        return true;
    }
}
