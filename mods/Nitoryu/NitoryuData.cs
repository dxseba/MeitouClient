using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Modding;

namespace Meitou.Nitoryu;

/// <summary>
/// The data phase: makes the original's own clips (<c>Nitoryu_L3_male.skeleton</c> / <c>_female</c>, 67 tracks named <c>nitoryu ...</c>) playable
/// by Meitou's animation system, which the original does with its own pose engine (README "Facts"):
/// <list type="bullet">
/// <item>the original's ANIMATION_FILE <c>Nitoryu animations</c> gets the two skeletons as its <c>male animation</c> / <c>female animation</c>
/// and is linked to every RACE (<c>animation files</c>), so the tracks join each rig's clips;</item>
/// <item>each track gets an ANIMATION record of category 3 (played by name only: no selector of the game picks it), named as the track, whole
/// body for strikes, blocks, draws and specials, upper body and looping for stances and runs.</item>
/// </list>
/// Nothing is changed without the original's data (its ANIMATION_FILE) and skeletons.
/// </summary>
static class NitoryuData
{
    /// <summary>The original's ANIMATION_FILE record.</summary>
    public const string AnimationFileId = "28-Nitoryu.mod";

    /// <summary>The prefix of the original's tracks.</summary>
    public const string ClipPrefix = "nitoryu ";

    const string MaleSkeleton = "character/meshes/male_skeleton/Nitoryu_L3_male.skeleton";
    const string FemaleSkeleton = "character/meshes/female_skeleton/Nitoryu_L3_female.skeleton";

    /// <summary>The ANIMATION category no selector takes (<c>AnimationLibrary</c>: category 3 is played by name only).</summary>
    const int ByNameOnly = 3;

    public static void Edit(ModDataContext context)
    {
        if (context.Data.Find(AnimationFileId) is not { Type: FcsRecordType.AnimationFile })
        {
            context.Log("the original's data (Nitoryu.mod) is not enabled: its moves and animations are missing");
            return;
        }

        string? male = OriginalFiles.Find(context.Folder, MaleSkeleton, context.Log);
        string? female = OriginalFiles.Find(context.Folder, FemaleSkeleton, context.Log);
        if (male is null || female is null)
        {
            context.Log("the original's skeletons were not found: its own animations do not play");
            return;
        }

        var clips = ClipNames(male, context.Log);
        LinkFile(context);
        AddClips(context, clips);
        AddMirrors(context);
        context.Log($"{clips.Count} of the original's tracks linked to {context.Data.OfType(FcsRecordType.Race).Count()} races");
    }

    /// <summary>The <c>nitoryu ...</c> track names of a skeleton file, in file order.</summary>
    static List<string> ClipNames(string path, Action<string> log)
    {
        try
        {
            var skeleton = OgreSkeletonReader.Read(File.ReadAllBytes(path));
            return [.. skeleton.Animations.Select(a => a.Name).Where(n => n.StartsWith(ClipPrefix, StringComparison.Ordinal)).Distinct()];
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log($"{Path.GetFileName(path)} not read: {e.Message}");
            return [];
        }
    }

    static void LinkFile(ModDataContext context)
    {
        var file = context.Edit(AnimationFileId);
        file.Filenames["male animation"] = ".\\" + MaleSkeleton.Replace('/', '\\');
        file.Filenames["female animation"] = ".\\" + FemaleSkeleton.Replace('/', '\\');
        foreach (var race in context.Data.OfType(FcsRecordType.Race))
        {
            var edit = context.Edit(race.StringId);
            edit.References["animation files"] = [new FcsReference(AnimationFileId, 0, 0, 0)];
        }
    }

    static void AddClips(ModDataContext context, IReadOnlyList<string> clips)
    {
        for (int i = 0; i < clips.Count; i++)
        {
            string clip = clips[i];
            bool held = IsHeld(clip);
            var record = context.Add($"{i + 1}-nitoryu-port", FcsRecordType.Animation, clip);
            record.Strings["anim name"] = clip;
            record.Strings["layer"] = held || IsUpper(clip) ? "upper" : "all";
            record.Ints["category"] = ByNameOnly;
            record.Bools["loop"] = held;
            record.Floats["play speed"] = 1;
        }
    }

    /// <summary>
    /// A mirrored copy (<see cref="Mirror"/>) of each vanilla clip a pool move without a track of its own plays: an ANIMATION record
    /// <c>nitoryu mirror &lt;clip&gt;</c> of category 3 over that clip, whole body.
    /// </summary>
    static void AddMirrors(ModDataContext context)
    {
        var clips = context.Data.OfType(FcsRecordType.CombatTechnique)
            .Where(r => r.GetInt("nitoryu move") is >= 1 and <= 4 && r.GetString("nitoryu clip").Length == 0)
            .Select(r => r.GetString("anim name")).Where(c => c.Length > 0).Distinct().ToList();
        for (int i = 0; i < clips.Count; i++)
        {
            var record = context.Add($"{i + 1}-nitoryu-port-mirror", FcsRecordType.Animation, Mirror.Prefix + clips[i]);
            record.Strings["anim name"] = clips[i];
            record.Strings["layer"] = "all";
            record.Ints["category"] = ByNameOnly;
            record.Floats["play speed"] = 1;
        }
    }

    /// <summary>A track that is held while a state lasts (a stance, a run or jog hold): looping, upper body.</summary>
    public static bool IsHeld(string clip) => clip.Contains(" stance ", StringComparison.Ordinal) || clip.EndsWith(" trail", StringComparison.Ordinal);

    /// <summary>A track of the arms alone: blocks, draws, sheathes, the flick, the hit taken.</summary>
    static bool IsUpper(string clip) =>
        new[] { " block", " draw", " sheathe", "chiburi", "hit taken", "back guard" }.Any(k => clip.Contains(k, StringComparison.Ordinal));

    /// <summary>The track a technique's <c>nitoryu clip</c> names (<c>x_cut</c>: <c>nitoryu x cut</c>).</summary>
    public static string TrackOf(string clipField) => ClipPrefix + clipField.Replace('_', ' ');
}
