using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// Draws the badges: a postfix on the portrait bar's private <c>PortraitBar.AddSquadCells</c> adds one text box per marked member, in its cell's
/// corner and clipped to the page as the cell is (the cell at <c>page.X + i % columns x size</c>,
/// <c>page.Y + i / columns x size - SquadBarScroll</c>).
/// </summary>
static class PortraitBadges
{
    static FieldInfo s_scroll = null!, s_player = null!;
    static PropertyInfo s_uiScale = null!;

    public static void Install(ModContext context)
    {
        var bar = Required(AccessTools.TypeByName("Meitou.Game.Ui.PortraitBar"), "PortraitBar");
        var player = Required(AccessTools.TypeByName("Meitou.Game.PlayerInterface"), "PlayerInterface");

        s_scroll = Required(AccessTools.Field(bar, "SquadBarScroll"), "PortraitBar.SquadBarScroll");
        s_player = Required(AccessTools.GetDeclaredFields(bar).FirstOrDefault(f => f.FieldType == player), "PortraitBar.player");
        s_uiScale = Required(AccessTools.Property(player, "UiScale"), "PlayerInterface.UiScale");

        var cells = Required(AccessTools.Method(bar, "AddSquadCells"), "PortraitBar.AddSquadCells");
        context.Harmony.Patch(cells, postfix: new HarmonyMethod(typeof(PortraitBadges), nameof(Added)));
    }

    static T Required<T>(T? found, string name) where T : class => found ?? throw new MissingMemberException("Vital Read badges: " + name);

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void Added(object __instance, List<UiControl> controls, IReadOnlyList<CharacterSnapshot> members, UiRect page, float size, int columns)
    #pragma warning restore IDE1006
    {
        var settings = VitalReadMod.Settings;
        if (!settings.Enabled || !settings.ShowText || VitalReadMod.Running is not { } system) return;

        float scroll = (float)s_scroll.GetValue(__instance)!;
        float scale = (float)s_uiScale.GetValue(s_player.GetValue(__instance))!;
        var facts = system.Facts;
        for (int i = 0; i < members.Count; i++)
        {
            var cell = new UiRect(page.X + i % columns * size, page.Y + i / columns * size - scroll, size, size);
            if (cell.Y + size <= page.Y || cell.Y >= page.Y + page.Height) continue;

            if (StateOf(members[i], facts) is var state and not VitalState.None) controls.Add(Badge(i, state, cell, page, scale, settings));
        }
    }

    static VitalState StateOf(CharacterSnapshot member, IReadOnlyDictionary<CharacterId, VitalFacts> facts) =>
        member.Body is { } body ? VitalRules.Of(body, member.InBed, facts.GetValueOrDefault(member.Id)) : VitalState.None;

    static UiControl Badge(int i, VitalState state, UiRect cell, UiRect page, float scale, VitalReadSettings settings)
    {
        float text = settings.TextSize * scale;
        float margin = settings.Margin * scale;
        var box = new Vector2(cell.Width * 0.6f, text * 1.4f);
        var properties = new Dictionary<string, string>
        {
            ["FontName"] = "Kenshi_StandardFont_Medium",
            ["TextAlign"] = Align(settings.Anchor),
            ["TextShadow"] = "true",
        };

        var colour = settings.Coloured ? Colour(state) : Vector4.One;
        return new UiControl($"vitalread-{i}", "TextBox", Place(cell, box, margin, settings.Anchor), Caption(state), "", "", true, properties,
            Vector4.Zero, colour, Pickable: false, TextScale: text / 16, Clip: page);
    }

    static UiRect Place(UiRect cell, Vector2 box, float margin, Corner corner)
    {
        bool right = corner is Corner.TopRight or Corner.BottomRight;
        bool bottom = corner is Corner.BottomLeft or Corner.BottomRight;
        float x = right ? cell.X + cell.Width - box.X - margin : cell.X + margin;
        float y = bottom ? cell.Y + cell.Height - box.Y - margin : cell.Y + margin;
        return new UiRect(x, y, box.X, box.Y);
    }

    static string Align(Corner corner) => corner switch
    {
        Corner.TopLeft => "Left Top",
        Corner.TopRight => "Right Top",
        Corner.BottomLeft => "Left Bottom",
        _ => "Right Bottom",
    };

    /// <summary>The original's text badges; the fork's bed states, icons only there, as a plus and RP.</summary>
    static string Caption(VitalState state) => state switch
    {
        VitalState.PlayingDead => "PD",
        VitalState.Dying => "DY",
        VitalState.RecoveryComa => "RC",
        VitalState.Unconscious => "ZZ",
        VitalState.CrippledLeg => "CrL",
        VitalState.CrippledArm => "CrA",
        VitalState.Starving => "ST",
        VitalState.Repairing => "RP",
        VitalState.Resting => "+",
        _ => "",
    };

    /// <summary>Colours standing in for the original's icons (engine choice): red dying, blue for the bed states.</summary>
    static Vector4 Colour(VitalState state) => state switch
    {
        VitalState.PlayingDead => new(0.69f, 0.65f, 0.55f, 1),
        VitalState.Dying => new(0.95f, 0.2f, 0.15f, 1),
        VitalState.RecoveryComa => new(0.95f, 0.45f, 0.5f, 1),
        VitalState.Unconscious => new(0.8f, 0.82f, 0.9f, 1),
        VitalState.CrippledLeg or VitalState.CrippledArm => new(0.95f, 0.6f, 0.2f, 1),
        VitalState.Starving => new(0.95f, 0.85f, 0.3f, 1),
        VitalState.Repairing or VitalState.Resting => new(0.35f, 0.6f, 1, 1),
        _ => Vector4.One,
    };
}
