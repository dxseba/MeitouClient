using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Transmog;

/// <summary>
/// Port of GHOST's Transmog: a character can show other gear's look over its real armour, backpack and limbs (several appearances per slot), dye
/// them with two colours and move, turn and scale them, from a workbench opened at its inventory. Display only: stats, carrying and trading use
/// the real items. The outfits are saved with the game; the original's texts and icon are read from the installed original.
/// </summary>
public sealed class TransmogMod : IMod
{
    internal static readonly TransmogSettings Settings = new();

    public void Initialize(ModContext context)
    {
        string? original = OriginalFiles.Find(context);
        OriginalFiles.AddTranslations(context, original);
        context.AddSettings(Settings).Changed += _ => PublishPatch.SettingsChanged();

        context.AddSystem("transmog", _ => new TransmogSystem(), SystemAnchor.End);
        PublishPatch.Install(context, new LookBuilder(context.Data, context.Population, Settings), Settings);
        RenderPatch.Install(context);
        Previews.Install(context);

        var window = new TransmogWindow(context, Settings, new Library(context), OriginalFiles.Icon(original));
        context.AddUi(window);
        context.AddFrame(window.Frame);
        context.Log("Transmog initialized");
    }
}
