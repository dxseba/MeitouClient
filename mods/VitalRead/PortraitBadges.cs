using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// Draws the badges: a postfix on the portrait bar's private <c>PortraitBar.AddSquadCells</c> adds, per marked member, an icon and a text box in
/// corners of its portrait image (the original parents both to the cell's <c>portraitimage</c>), clipped to the page as the cell is (the cell at
/// <c>page.X + i % columns x size</c>, <c>page.Y + i / columns x size - SquadBarScroll</c>).
/// </summary>
static class PortraitBadges
{
    /// <summary>
    /// The portrait image inside a cell of the 136 px layout (<c>PortraitBar.AddSquadCells</c>: x 6, y 6, 124 by 123).
    /// </summary>
    const float LayoutSize = 136, ImageX = 6, ImageY = 6, ImageWidth = 124, ImageHeight = 123;

    /// <summary>
    /// An automatic icon's share of the portrait image's width (Meitou's choice; the original's automatic size is not known).
    /// </summary>
    const float AutoIconShare = 0.3f;

    static FieldInfo s_scroll = null!, s_player = null!;
    static PropertyInfo s_uiScale = null!;
    static BadgeIcons s_icons = null!;

    public static void Install(ModContext context)
    {
        var bar = Required(AccessTools.TypeByName("Meitou.Game.Ui.PortraitBar"), "PortraitBar");
        var player = Required(AccessTools.TypeByName("Meitou.Game.PlayerInterface"), "PlayerInterface");

        s_scroll = Required(AccessTools.Field(bar, "SquadBarScroll"), "PortraitBar.SquadBarScroll");
        s_player = Required(AccessTools.GetDeclaredFields(bar).FirstOrDefault(f => f.FieldType == player), "PortraitBar.player");
        s_uiScale = Required(AccessTools.Property(player, "UiScale"), "PlayerInterface.UiScale");
        s_icons = new BadgeIcons(context.Folder);
        BadgeIcons.Bind(player);

        var cells = Required(AccessTools.Method(bar, "AddSquadCells"), "PortraitBar.AddSquadCells");
        context.Harmony.Patch(cells, postfix: new HarmonyMethod(typeof(PortraitBadges), nameof(Added)));
    }

    static T Required<T>(T? found, string name) where T : class => found ?? throw new MissingMemberException("Vital Read badges: " + name);

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void Added(object __instance, List<UiControl> controls, IReadOnlyList<CharacterSnapshot> members, UiRect page, float size, int columns)
    #pragma warning restore IDE1006
    {
        var settings = VitalReadMod.Settings;
        if (!settings.Enabled || !settings.ShowText && !settings.ShowIcons || VitalReadMod.Running is not { } system) return;

        float scroll = (float)s_scroll.GetValue(__instance)!;
        object player = s_player.GetValue(__instance)!;
        float scale = (float)s_uiScale.GetValue(player)!;
        var facts = system.Facts;
        for (int i = 0; i < members.Count; i++)
        {
            float top = page.Y + i / columns * size - scroll;
            if (top + size <= page.Y || top >= page.Y + page.Height) continue;

            var image = ImageOf(page.X + i % columns * size, top, size);
            if (StateOf(members[i], facts) is var state and not VitalState.None) AddBadge(controls, player, i, state, image, page, scale);
        }
    }

    static UiRect ImageOf(float left, float top, float size)
    {
        float k = size / LayoutSize;
        return new UiRect(left + ImageX * k, top + ImageY * k, ImageWidth * k, ImageHeight * k);
    }

    static VitalState StateOf(CharacterSnapshot member, IReadOnlyDictionary<CharacterId, VitalFacts> facts) =>
        member.Body is { } body ? VitalRules.Of(body, facts.GetValueOrDefault(member.Id)) : VitalState.None;

    static void AddBadge(List<UiControl> controls, object player, int i, VitalState state, UiRect image, UiRect page, float scale)
    {
        var settings = VitalReadMod.Settings;
        var margin = new Vector2(settings.MarginX, settings.MarginY) * scale;
        if (settings.ShowIcons && Icon(player, i, state, image, page, scale, margin) is { } icon) controls.Add(icon);
        if (settings.ShowText) controls.Add(Text(i, state, image, page, scale, margin));
    }

    static UiControl? Icon(object player, int i, VitalState state, UiRect image, UiRect page, float scale, Vector2 margin)
    {
        if (s_icons.Image(player, state) is not { } properties) return null;

        var settings = VitalReadMod.Settings;
        float side = settings.IconSize > 0 ? settings.IconSize * scale : image.Width * AutoIconShare;
        var box = Clamp(Place(image, new Vector2(side), margin, settings.IconAnchor), image);
        return new UiControl($"vitalread-icon-{i}", "ImageBox", box, "", "", "", true, properties, Vector4.Zero, Pickable: false, Clip: page);
    }

    static UiControl Text(int i, VitalState state, UiRect image, UiRect page, float scale, Vector2 margin)
    {
        var settings = VitalReadMod.Settings;
        float text = settings.TextSize * scale;
        var box = new Vector2(image.Width * 0.6f, text * 1.4f);
        var properties = new Dictionary<string, string>
        {
            ["FontName"] = "Kenshi_StandardFont_Medium",
            ["TextAlign"] = Align(settings.TextAnchor),
            ["TextShadow"] = "true",
        };

        var colour = settings.Coloured ? Colour(state) : Vector4.One;
        return new UiControl($"vitalread-{i}", "TextBox", Place(image, box, margin, settings.TextAnchor), Caption(state), "", "", true, properties,
            Vector4.Zero, colour, Pickable: false, TextScale: text / 16, Clip: page);
    }

    /// <summary>
    /// <paramref name="box"/> in <paramref name="corner"/> of <paramref name="area"/>, moved inward by <paramref name="margin"/> (negative: outward).
    /// </summary>
    static UiRect Place(UiRect area, Vector2 box, Vector2 margin, Corner corner)
    {
        bool right = corner is Corner.TopRight or Corner.BottomRight;
        bool bottom = corner is Corner.BottomLeft or Corner.BottomRight;
        float x = right ? area.X + area.Width - box.X - margin.X : area.X + margin.X;
        float y = bottom ? area.Y + area.Height - box.Y - margin.Y : area.Y + margin.Y;
        return new UiRect(x, y, box.X, box.Y);
    }

    /// <summary>
    /// The fork's v3: an icon's place is held inside the portrait image.
    /// </summary>
    static UiRect Clamp(UiRect box, UiRect area)
    {
        float w = Math.Min(box.Width, area.Width);
        float h = Math.Min(box.Height, area.Height);
        float x = Math.Clamp(box.X, area.X, area.X + area.Width - w);
        float y = Math.Clamp(box.Y, area.Y, area.Y + area.Height - h);
        return new UiRect(x, y, w, h);
    }

    static string Align(Corner corner) => corner switch
    {
        Corner.TopLeft => "Left Top",
        Corner.TopRight => "Right Top",
        Corner.BottomLeft => "Left Bottom",
        _ => "Right Bottom",
    };

    /// <summary>
    /// The original's text badges (<b>Verified</b>, original DLL strings).
    /// </summary>
    static string Caption(VitalState state) => state switch
    {
        VitalState.Dying => "DY",
        VitalState.RecoveryComa => "RC",
        VitalState.Unconscious => "ZZ",
        VitalState.PlayingDead => "PD",
        VitalState.Starving => "ST",
        VitalState.CrippledArm => "CrA",
        VitalState.CrippledLeg => "CrL",
        VitalState.Repairing => "RP",
        VitalState.InBed => "BD",
        _ => "",
    };

    /// <summary>
    /// Meitou's optional tints: red dying, blue for the bed states (the fork's one colour for "being treated").
    /// </summary>
    static Vector4 Colour(VitalState state) => state switch
    {
        VitalState.PlayingDead => new(0.69f, 0.65f, 0.55f, 1),
        VitalState.Dying => new(0.95f, 0.2f, 0.15f, 1),
        VitalState.RecoveryComa => new(0.95f, 0.45f, 0.5f, 1),
        VitalState.Unconscious => new(0.8f, 0.82f, 0.9f, 1),
        VitalState.CrippledLeg or VitalState.CrippledArm => new(0.95f, 0.6f, 0.2f, 1),
        VitalState.Starving => new(0.95f, 0.85f, 0.3f, 1),
        VitalState.Repairing or VitalState.InBed => new(0.35f, 0.6f, 1, 1),
        _ => Vector4.One,
    };
}
