using Meitou.Modding;

namespace Meitou.KathunLightsabers;

/// <summary>
/// Port of Glowing Lightsabers for Project Kathun (workshop 3833105321): drawn lightsabers glow in their colour, with halo glows along the blade
/// and a coloured light at night. The settings file <c>KathunSaberGlow.cfg</c> is read from this folder, else from the installed original.
/// </summary>
public sealed class KathunLightsabersMod : IMod
{
    const string ConfigFile = "KathunSaberGlow.cfg";

    public void Initialize(ModContext context)
    {
        var config = ReadConfig(context);
        var catalog = new SaberCatalog(config, context.Log);
        if (config.Light) SaberLamps.Patch(context);

        var frame = new SaberFrame(context, config, catalog);
        context.AddFrame(frame.Frame);
        context.Log($"{config.Colours.Count} colours, {config.Textures.Count} textures, {config.Meshes.Count} saber models, " +
            $"light {(config.Light ? "on" : "off")}, halo {(config.Halo ? "on" : "off")}");
    }

    static SaberConfig ReadConfig(ModContext context)
    {
        if (new OriginalFiles(context).Find(ConfigFile) is not { } path)
        {
            context.Log($"{ConfigFile} not found: no saber is known");
            return SaberConfig.Read([], context.Log);
        }

        try
        {
            return SaberConfig.Read(File.ReadAllLines(path), context.Log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"{ConfigFile} not read ({e.Message})");
            return SaberConfig.Read([], context.Log);
        }
    }
}
