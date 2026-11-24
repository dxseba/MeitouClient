using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.AutoPauseOnLoad;

/// <summary>
/// Port of Auto-Pause on Load (MCM fork) (workshop 3813851956, LEOPARD's fork of Emkej8's mod, GPL-3.0): pauses the game after a save is loaded
/// or imported, when trading starts, when an inventory opens (resuming after either if it paused), and when a character of a squad that is not
/// the selected one takes a hit. Its settings show through the installed original's catalogues (<c>auto_pause_on_load</c>).
/// </summary>
public sealed class AutoPauseOnLoadMod : IMod
{
    internal static readonly AutoPauseSettings Settings = new();

    public void Initialize(ModContext context)
    {
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("auto_pause_on_load", original);

        context.AddSettings(Settings);
        var director = new PauseDirector(context.Interface, context.Log);
        context.AddSystem("load-watch", _ => new LoadWatch(director), SystemAnchor.End);
        context.AddCombatHitFeed(director.Hits);
        context.AddFrame(director.Frame);
        context.Log("Auto-Pause on Load initialized");
    }
}

/// <summary>
/// Tells the director how the running world started (<see cref="IWorldReady"/>): a load or an import arms its pause after loading.
/// </summary>
sealed class LoadWatch(PauseDirector director) : IWorldReady
{
    public void Ready(World world, bool fromSave)
    {
        if (fromSave) director.Arm(world.Start.Kind);
    }
}
