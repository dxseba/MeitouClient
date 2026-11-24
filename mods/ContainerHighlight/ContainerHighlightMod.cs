using Meitou.Modding;

namespace Meitou.ContainerHighlight;

/// <summary>
/// Port of Container Highlight (MCM fork) (workshop 3813852014, LEOPARD's fork of Emkej8's mod): while the highlight key is held, nearby lootable
/// containers get a marker (an icon and an outlined label, each optional) and are tinted in the world. Its settings and key label show through the
/// installed original's catalogues (<c>container_highlight</c>: en_GB, ru_RU, zh_CN); its icon is read from there.
/// </summary>
public sealed class ContainerHighlightMod : IMod
{
    const string IconFile = "container_highlight_chest.png";

    internal static readonly ContainerSettings Settings = new();

    public void Initialize(ModContext context)
    {
        string? original = OriginalFolder.Find(context);
        if (original is not null) context.AddTranslations("container_highlight", original);

        context.AddSettings(Settings);
        var scan = new ContainerScan(new ContainerKinds(context.Data, context.Log), context.Log);
        scan.Install(context);
        context.AddUi(new ContainerMarkersUi(scan, Icon(original)));
        ContainerOutline.Install(context, scan);
        context.Log("Container Highlight initialized");
    }

    /// <summary>
    /// The original's marker icon (<c>gui/gfx/container_highlight_chest.png</c> in its folder); null when it is not installed (no icon shows).
    /// </summary>
    static string? Icon(string? original) =>
        original is not null && Path.Combine(original, "gui", "gfx", IconFile) is var path && File.Exists(path) ? path : null;
}
