using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// Draws the badges: screen controls on each squad bar portrait (<see cref="UiScreens.Portraits"/>, anchored to its picture and clipped to it and
/// the bar's page, as the original parents both to the cell's <c>portraitimage</c>) add, per marked member, an icon and a text box in corners of
/// the picture.
/// </summary>
sealed class PortraitBadges(BadgeIcons icons) : IUiExtension
{
    /// <summary>
    /// An automatic icon's share of the portrait image's width (Meitou's choice; the original's automatic size is not known).
    /// </summary>
    const float AutoIconShare = 0.3f;

    /// <summary>
    /// The interface's scale, kept from the last frame (the badges' sizes and margins are in unscaled pixels).
    /// </summary>
    static volatile float s_uiScale = 1;

    /// <summary>
    /// The text style per corner (its alignment), as registered.
    /// </summary>
    readonly Dictionary<Corner, string> _styles = [];

    public string Id => "vitalread";

    public static void Install(ModContext context)
    {
        context.AddFrame(frame => s_uiScale = frame.UiScale);
        string[] folders = OriginalFolder.Find(context) is { } original ? [context.Folder, original] : [context.Folder];
        context.AddUi(new PortraitBadges(new BadgeIcons(folders)));
    }

    public void Register(UiRegistration registration)
    {
        foreach (var corner in Enum.GetValues<Corner>())
            _styles[corner] = registration.RegisterStyle("text-" + corner, new UiStyle { TextAlign = Align(corner) });

        registration.AddScreenControls(UiScreens.Hud, UiScreens.Portraits, UiAnchorPlacement.Inside, Build);
    }

    UiNode? Build(UiScreenContext context)
    {
        var settings = VitalReadMod.Settings;
        if (!settings.Enabled || !settings.ShowText && !settings.ShowIcons || VitalReadMod.Running is not { } system) return null;
        if (context.Character is not { } member || StateOf(member, system.Facts) is not (var state and not VitalState.None)) return null;

        var root = new UiNode("badge") { Pickable = false };
        var image = new UiRect(0, 0, context.Anchor.Width, context.Anchor.Height);
        float scale = s_uiScale;
        var margin = new Vector2(settings.MarginX, settings.MarginY) * scale;
        if (settings.ShowIcons && Icon(state, image, scale, margin) is { } icon) root.Children.Add(icon);
        if (settings.ShowText) root.Children.Add(Text(state, image, scale, margin));
        return root;
    }

    static VitalState StateOf(CharacterSnapshot member, IReadOnlyDictionary<CharacterId, VitalFacts> facts) =>
        member.Body is { } body ? VitalRules.Of(body, facts.GetValueOrDefault(member.Id)) : VitalState.None;

    UiNode? Icon(VitalState state, UiRect image, float scale, Vector2 margin)
    {
        if (icons.Image(state) is not { } source) return null;

        var settings = VitalReadMod.Settings;
        float side = settings.IconSize > 0 ? settings.IconSize * scale : image.Width * AutoIconShare;
        var box = Clamp(Place(image, new Vector2(side), margin, settings.IconAnchor), image);
        return new UiNode("icon", UiNodeKind.Image) { Placement = At(box), Image = source.File, ImageCrop = source.Crop };
    }

    UiNode Text(VitalState state, UiRect image, float scale, Vector2 margin)
    {
        var settings = VitalReadMod.Settings;
        float text = settings.TextSize * scale;
        var box = Place(image, new Vector2(image.Width * 0.6f, text * 1.4f), margin, settings.TextAnchor);
        var colour = settings.Coloured ? Colour(state) : Vector4.One;
        return new UiNode("text", UiNodeKind.Label)
        {
            Placement = At(box),
            Style = _styles[settings.TextAnchor],
            Text = Caption(state),
            TextColour = colour,
            TextScale = Math.Clamp(text / 16, 0.05f, UiHost.MaxTextScale),
        };
    }

    static UiPlacement At(UiRect box) => new(box.Width, box.Height, box.X, box.Y);

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
