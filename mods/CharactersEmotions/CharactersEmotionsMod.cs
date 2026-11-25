using Meitou.Data.Characters;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.CharactersEmotions;

/// <summary>
/// Port of Characters Emotions (Workshop 3791017374): real-time facial expressions on the player's characters through their body mesh's
/// <c>CE_*</c> shape keys: blinking, normal, combat, hungry, hurt, seated, sleeping, hit and knocked out expressions, animation-driven expressions
/// and speech mouth shapes. The expression definitions and menu texts are read from the installed original.
/// </summary>
public sealed class CharactersEmotionsMod : IMod
{
    /// <summary>
    /// A weight too small to see that every look gets on the blink keys: a look is only drawn with a morph when one of its poses is set
    /// (<c>CharacterContent.AddMorph</c>), and the live weights replace that morph.
    /// </summary>
    const float Seed = 1e-4f;

    internal static readonly EmotionSettings Settings = new();

    public void Initialize(ModContext context)
    {
        string? plugins = OriginalFiles.Plugins(context);
        var book = EmotionBook.Load(Ini(plugins, "CharactersEmotions_emotions.ini"), Ini(plugins, "CharactersEmotions_animemotions.ini"),
            context.Log);
        if (plugins is not null) context.AddTranslations(context.Language, OriginalFiles.Translations(plugins, context.Language));

        context.AddSettings(Settings);
        var driver = new FaceDriver(Settings, book, context.Log);
        context.AddKeyAction("toggle-ban", "Toggle Selected Character Ban Key", driver.ToggleBan, Binding.Of(Key.P));
        context.AddCombatHitFeed(driver.Hits);
        context.AddFrame(driver.Frame);

        var seed = book.Blink.Select(k => k.Key).Distinct().ToDictionary(k => k, _ => Seed);
        context.AddLookExtras(_ => seed.Count > 0 ? new LookExtras { PoseWeights = seed } : null);

        MorphPatch.Install(context, book);
        context.Log($"Characters Emotions initialized: {book.Normals.Count} normal, {book.Animations.Count} animation expression(s)");
    }

    static IniFile? Ini(string? folder, string name) => folder is null ? null : IniFile.Read(Path.Combine(folder, name));
}
