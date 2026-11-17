using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LevelUpNotify;

/// <summary>
/// Projects each recent level up over its character's head, rising as it ages: a postfix on the interface's <c>BindUi</c> (which owns the camera,
/// <c>PlayerInterface.Project</c>) writes one binding per shown text, "x|y|text", for <see cref="FloatingTextUi"/> to draw.
/// </summary>
static class FloatingText
{
    public const string Key = "levelup.float.";
    public const int Slots = 8;

    /// <summary>World units above a character's feet where the text starts, and how far it rises over its life.</summary>
    const float Head = 22, Rise = 8;

    static LevelUpSettings s_settings = new();
    static FieldInfo? s_sessionField;
    static MethodInfo? s_project;

    public static void Install(ModContext context, LevelUpSettings settings)
    {
        s_settings = settings;
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(WorldSession));
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(FloatingText), nameof(Bound)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (!s_settings.Enabled || !s_settings.Floating || s_sessionField?.GetValue(__instance) is not WorldSession session) return;
        if (session.World.Systems.OfType<LevelWatchSystem>().FirstOrDefault() is not { } watch) return;

        float life = s_settings.Seconds / session.World.TickSeconds;
        int slot = 0;
        foreach (var level in watch.Recent.Reverse())
        {
            float age = (session.World.Tick - level.Tick) / life;
            if (age is < 0 or > 1 || slot >= Slots) continue;
            if (Screen(__instance, session, level.Character, age) is not { } at) continue;

            __result.Text[Key + slot++] = string.Create(CultureInfo.InvariantCulture, $"{at.X:0}|{at.Y:0}|{level.Text}");
        }
    }

    static Vector2? Screen(object player, WorldSession session, CharacterId id, float age)
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id);
        if (character is null) return null;

        var head = character.Position + new Vector3(0, Head + Rise * age, 0);
        return (Vector2?)s_project!.Invoke(player, [head]);
    }
}

/// <summary>Draws the bound floating texts as labels centred on their points.</summary>
public sealed class FloatingTextUi : IUiExtension
{
    const float Width = 220, Height = 24;

    public string Id => "dxseba.levelup.float";

    public void Register(UiRegistration registration)
    {
        string style = registration.RegisterStyle("float", new() { Text = new(.98f, .85f, .35f, 1), TextPadding = new(0, 0, 0, 0) });
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, style), order: 41);
    }

    static UiNode Build(UiFrameContext frame, string style)
    {
        var root = new UiNode("levelup-float") { Placement = UiPlacement.Fill, Pickable = false };
        for (int i = 0; i < FloatingText.Slots; i++)
        {
            if (Parse(frame.Text(FloatingText.Key + i)) is not var (x, y, text)) continue;

            var placement = new UiPlacement(Width, Height, Left: x - Width / 2, Top: y - Height / 2);
            root.Children.Add(new("levelup-float-" + i, UiNodeKind.Label) { Placement = placement, Text = text, Style = style, Pickable = false });
        }

        return root;
    }

    static (float X, float Y, string Text)? Parse(string bound)
    {
        var parts = bound.Split('|', 3);
        if (parts.Length != 3) return null;

        bool ok = float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x) & float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y);
        return ok ? (x, y, parts[2]) : null;
    }
}
