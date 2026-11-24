using Meitou.Modding;

namespace Meitou.GunManufactureSounds;

/// <summary>
/// Port of Firearms Sound Effects - GunManufacture+ (workshop 3087291056): each GunManufacture+ firearm plays its own gunshot from the original's
/// <c>GunSounds.bnk</c> when it fires, in place of the crossbow sound, and the reload animations' gun cues are silenced. The gun table is read
/// from the installed original's plugin; the bank loads through the original's <c>RE_Kenshi.json</c> when the original is enabled.
/// </summary>
public sealed class GunManufactureSoundsMod : IMod
{
    const string Plugin = "GunManufactureRealFireSounds.dll";

    public void Initialize(ModContext context)
    {
        var guns = ReadGuns(context);
        if (guns.Count == 0) return;

        GunSoundPatches.Patch(context, guns);
        context.AddFrame(GunSoundPatches.Frame);
        context.Log($"{guns.Count} guns, {guns.Values.Distinct().Count()} gun sounds");
    }

    static Dictionary<int, string> ReadGuns(ModContext context)
    {
        if (new OriginalFiles(context).Find(Plugin) is not { } path)
        {
            context.Log($"{Plugin} of the original not found: nothing changes");
            return [];
        }

        try
        {
            var guns = GunTable.Read(File.ReadAllBytes(path));
            if (guns.Count == 0) context.Log($"no gun table in {Plugin}: nothing changes");
            return guns;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            context.Log($"{Plugin} not read ({e.Message}): nothing changes");
            return [];
        }
    }
}
