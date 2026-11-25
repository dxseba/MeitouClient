using System.Reflection;
using HarmonyLib;

namespace Meitou.QuickStartSelect;

/// <summary>One advanced option of the game's panel: a slider (range, step 0.05) or a tick, its label, default and help line.</summary>
sealed record GameOption(int Index, bool Tick, string Label, float Min, float Max, float Default, int HelpLine);

/// <summary>
/// The game's advanced options read from its <c>NewGameSettings</c> tables (sliders then ticks, in the panel's order) and set through its own
/// copy-on-write methods (<c>Set</c> clamps a slider to its range, <c>SetTick</c>); reflection, as the type is internal to the game.
/// </summary>
static class OptionTable
{
    static readonly Type? Settings = AccessTools.TypeByName("Meitou.Game.Title.NewGameSettings");
    static readonly FieldInfo? Sliders = Settings is null ? null : AccessTools.Field(Settings, "Options");
    static readonly FieldInfo? Ticks = Settings is null ? null : AccessTools.Field(Settings, "Ticks");
    static readonly MethodInfo? SetSlider = Settings is null ? null : AccessTools.Method(Settings, "Set", [typeof(int), typeof(float)]);
    static readonly MethodInfo? SetTick = Settings is null ? null : AccessTools.Method(Settings, "SetTick", [typeof(int), typeof(bool)]);
    static readonly MethodInfo? Ticked = Settings is null ? null : AccessTools.Method(Settings, "Ticked", [typeof(int)]);
    static readonly PropertyInfo? Item = Settings is null ? null : AccessTools.Property(Settings, "Item");

    public static bool Ready => Sliders is not null && Ticks is not null && SetSlider is not null && SetTick is not null && Ticked is not null
        && Item is not null;

    static IReadOnlyList<GameOption>? s_all;

    /// <summary>Every option, sliders first (default 1), then ticks (default the loader's).</summary>
    public static IReadOnlyList<GameOption> All => s_all ??= Read();

    static List<GameOption> Read()
    {
        var options = new List<GameOption>();
        if (!Ready) return options;

        int i = 0;
        foreach (object o in (Array)Sliders!.GetValue(null)!)
        {
            options.Add(new GameOption(i++, false, Get<string>(o, "Label"), Get<float>(o, "Min"), Get<float>(o, "Max"), 1, Get<int>(o, "HelpLine")));
        }

        i = 0;
        foreach (object t in (Array)Ticks!.GetValue(null)!)
        {
            float on = Get<bool>(t, "Default") ? 1 : 0;
            options.Add(new GameOption(i++, true, Get<string>(t, "Label"), 0, 1, on, Get<int>(t, "HelpLine")));
        }

        return options;
    }

    static T Get<T>(object o, string name) => (T)AccessTools.Property(o.GetType(), name)!.GetValue(o)!;

    /// <summary>The option's value in <paramref name="settings"/> (a tick as 0 or 1).</summary>
    public static float Value(object settings, GameOption option) =>
        option.Tick ? (Ticked!.Invoke(settings, [option.Index]) is true ? 1 : 0) : (float)Item!.GetValue(settings, [option.Index])!;

    /// <summary>A copy of <paramref name="settings"/> with the option at <paramref name="value"/> (a slider rounded to 0.05).</summary>
    public static object With(object settings, GameOption option, float value) => option.Tick
        ? SetTick!.Invoke(settings, [option.Index, value >= .5f])!
        : SetSlider!.Invoke(settings, [option.Index, MathF.Round(value * 20) / 20])!;
}
