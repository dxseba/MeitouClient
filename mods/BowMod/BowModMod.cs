using Meitou.Modding;
using Meitou.Simulation.Bodies;

namespace Meitou.BowMod;

/// <summary>
/// Port of BowMod [1.0] (workshop 3838294517, its plugin BowSkills.dll): the bows of the original's data (CROSSBOW records with
/// <c>bowmod bow</c>) use and train their own Bows skill instead of Crossbows, on the original's proficiency curve. Record names and the skill's
/// texts follow the game's language from the original's <c>Localization</c> files.
/// </summary>
public sealed class BowModMod : IMod, IModDataPhase
{
    /// <summary>The original's record names and descriptions in the game's language (<see cref="BowTexts.Rename"/>).</summary>
    public void EditData(ModDataContext context) => BowTexts.Rename(context);

    public void Initialize(ModContext context)
    {
        var skill = context.AddSkill(Skill(BowTexts.Ui(context)));
        BowSkill.Patch(context, skill);
        BowSave.Patch(context, skill);
        context.Log("BowMod initialized");
    }

    /// <summary>The Bows line under the Weapons column (the original lists it after Crossbows), its pane texts the original's.</summary>
    static ModSkill Skill(IReadOnlyDictionary<string, string> texts)
    {
        string T(string key) => texts[key].TrimStart('-').TrimEnd(':', '：').Trim();
        string label = T("BOW_LABEL");

        return new ModSkill("bowmod.archery", label.Length is > 0 and <= 32 ? label : "Bows", SkillColumn.Weapons)
        {
            Description = texts["BOW_DESCRIPTION"],
            Affects = [T("ACCURACY"), T("RELOAD")],
            Training = [T("TRAIN_BOW"), T("SHARED_PRECISION")],
            Start = 1,
        };
    }
}
