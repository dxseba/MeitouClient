using Meitou.Game.Host;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Game.Ui;

namespace Meitou.SpeedMenu;

/// <summary>A HUD button opening a list of the selected characters by top speed, slowest first, to find who holds a group back.</summary>
public sealed class SpeedMenuMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("speeds", _ => new SpeedSystem(), SystemAnchor.End);
        HudAdapter.Install(context);
        context.AddUi(new SpeedMenuUi());
        context.Log("Speed menu initialized");
    }
}

public readonly record struct Runner(string Name, float Speed);

/// <summary>Publishes each player character's name and current top speed (the speed stat with its load and injuries, u/s) about once a second.</summary>
sealed class SpeedSystem : ITickSystem
{
    volatile FrozenDictionary<CharacterId, Runner> _published = FrozenDictionary<CharacterId, Runner>.Empty;
    public FrozenDictionary<CharacterId, Runner> Published => _published;

    public void SlowWorld(World world)
    {
        if (world.Tick % 30 != 0) return;

        var table = world.Characters;
        var map = new Dictionary<CharacterId, Runner>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true } cold)
                map[new(slot, table.Next[slot].Generation)] = new(cold.Name, table.Next[slot].MaxSpeed);
        }
        _published = map.ToFrozenDictionary();
    }
}

/// <summary>Writes the sorted list into the HUD bindings (a postfix on the private <c>PlayerInterface.BindUi</c>, as Brutality does).</summary>
static class HudAdapter
{
    public const string CountKey = "speedmenu.count", LinePrefix = "speedmenu.line.";
    public const int MaxLines = 16;
    static FieldInfo? s_sessionField;
    static MethodInfo? s_selectedMethod;

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        if (type is null) return;

        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(f => f.FieldType == typeof(WorldSession))
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

        var runners = selected.Select(s => speeds.Published.TryGetValue(s.Id, out var r) ? r : (Runner?)null).OfType<Runner>()
            .OrderBy(r => r.Speed).ThenBy(r => r.Name, StringComparer.Ordinal).Take(MaxLines).ToList();
        __result.Text[CountKey] = runners.Count.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < runners.Count; i++)
            __result.Text[LinePrefix + i] = $"{runners[i].Speed.ToString("0.0", CultureInfo.InvariantCulture),6}  {runners[i].Name}";
    }
}

/// <summary>The toggle button and the list panel under the toolbar's right side.</summary>
public sealed class SpeedMenuUi : IUiExtension
{
    bool _open;
    public string Id => "dxseba.speedmenu.ui";

    public void Register(UiRegistration registration)
    {
        string panel = registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .94f), Text = new(.91f, .86f, .74f, 1) });
        string text = registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) });
        string button = registration.RegisterStyle("button", new() { Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), TextPadding = new(6, 2, 6, 2) });
        string toggle = registration.RegisterAction("toggle", _ => _open = !_open);
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, panel, text, button, toggle), order: 50);
    }

    UiNode Build(UiFrameContext frame, string panel, string text, string button, string toggle)
    {
        if (!int.TryParse(frame.Text(HudAdapter.CountKey), CultureInfo.InvariantCulture, out int count)) return new("speedmenu-hidden") { Visible = false };

        var root = new UiNode("speedmenu") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(new("speedmenu-toggle", UiNodeKind.Button) { Placement = new(140, 26, Left: 340, Top: 44), Text = "Speeds", Action = toggle, Style = button });
        if (!_open) return root;

        var body = new UiNode("speedmenu-list") { Placement = new(260, 30 + 22 * Math.Max(1, count), Left: 340, Top: 72), Style = panel, Flow = UiFlow.Column, Padding = 4, Gap = 1 };
        body.Children.Add(new("speedmenu-head", UiNodeKind.Label) { Placement = new(UiLength.Fill, 22), Text = "Top speed (u/s), slowest first", Style = text });
        if (count == 0) body.Children.Add(new("speedmenu-none", UiNodeKind.Label) { Placement = new(UiLength.Fill, 20), Text = "Select your characters.", Style = text });
        for (int i = 0; i < count; i++)
            body.Children.Add(new($"speedmenu-{i}", UiNodeKind.Label) { Placement = new(UiLength.Fill, 20), Text = frame.Text(HudAdapter.LinePrefix + i), Style = text });
        root.Children.Add(body);
        return root;
    }
}
