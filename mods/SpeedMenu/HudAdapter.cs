using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SpeedMenu;

/// <summary>
/// Writes the sorted list into the HUD bindings (a postfix on the private <c>PlayerInterface.BindUi</c>, once a frame on the interface's thread).
/// The order is the original's: the shown maximum speed (mph to a tenth), slowest first; a tie keeps the rows' previous order, then goes by name.
/// </summary>
static class HudAdapter
{
    /// <summary>
    /// The binding keys: the row count, and per row its name, its speed line and its current-speed line.
    /// </summary>
    public const string CountKey = "speedmenu.count", NamePrefix = "speedmenu.name.", SpeedPrefix = "speedmenu.speed.",
        CurrentPrefix = "speedmenu.current.";

    /// <summary>
    /// The original's conversion: world units per second per mph.
    /// </summary>
    const float UnitsPerMph = 4.4704f;

    /// <summary>
    /// The interface's session field.
    /// </summary>
    static FieldInfo? s_sessionField;

    /// <summary>
    /// The interface's selection accessor.
    /// </summary>
    static MethodInfo? s_selectedMethod;

    /// <summary>
    /// The rows' order last frame (the tie-break).
    /// </summary>
    static List<CharacterId> s_lastOrder = [];

    /// <summary>
    /// Patches the interface.
    /// </summary>
    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        if (type is null) return;

        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(type.FullName, "WorldSession");
        s_selectedMethod = AccessTools.Method(type, "Selected") ?? throw new MissingMethodException(type.FullName, "Selected");
        context.Harmony.Patch(AccessTools.Method(type, "BindUi"), postfix: new HarmonyMethod(typeof(HudAdapter), nameof(Bound)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (s_sessionField?.GetValue(__instance) is not WorldSession session) return;
        if (session.World.Systems.OfType<SpeedSystem>().FirstOrDefault() is not { } speeds) return;
        if (s_selectedMethod?.Invoke(__instance, null) is not IReadOnlyList<CharacterSnapshot> selected) return;

        var rows = Sorted(selected, speeds);
        s_lastOrder = [.. rows.Select(r => r.Id)];
        Write(__result, rows);
    }

    /// <summary>
    /// The selected characters with published speeds, in the original's order.
    /// </summary>
    static List<(CharacterId Id, Runner Runner)> Sorted(IReadOnlyList<CharacterSnapshot> selected, SpeedSystem speeds)
    {
        var previous = s_lastOrder;
        return [.. selected
            .Where(s => speeds.Published.ContainsKey(s.Id))
            .Select(s => (s.Id, Runner: speeds.Published[s.Id]))
            .OrderBy(r => Tenths(r.Runner.MaxSpeed))
            .ThenBy(r => previous.IndexOf(r.Id) is var at and >= 0 ? at : int.MaxValue)
            .ThenBy(r => r.Runner.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Id.Slot)];
    }

    /// <summary>
    /// The rows' texts into the bindings.
    /// </summary>
    static void Write(UiBindings bindings, List<(CharacterId Id, Runner Runner)> rows)
    {
        bindings.Text[CountKey] = rows.Count.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < rows.Count; i++)
        {
            var runner = rows[i].Runner;
            float current = Mph(runner.Speed), max = Mph(runner.MaxSpeed);
            string index = i.ToString(CultureInfo.InvariantCulture);
            bindings.Text[NamePrefix + index] = runner.Name;
            bindings.Text[SpeedPrefix + index] = string.Format(CultureInfo.InvariantCulture, "{0:0.0} mph ({1:0.0} max)", current, max);
            bindings.Text[CurrentPrefix + index] = string.Format(CultureInfo.InvariantCulture, "Current speed: {0:0.0} mph", current);
        }
    }

    /// <summary>
    /// A speed in mph; 0 for one that is not finite, negative or 10000 and over (the original's guard).
    /// </summary>
    static float Mph(float unitsPerSecond)
    {
        float mph = unitsPerSecond / UnitsPerMph;
        return float.IsFinite(mph) && mph >= 0 && mph < 10000 ? mph : 0;
    }

    /// <summary>
    /// The sort key: the maximum speed in tenths of an mph, rounded.
    /// </summary>
    static int Tenths(float unitsPerSecond) => (int)MathF.Floor(Mph(unitsPerSecond) * 10 + 0.5f);
}
