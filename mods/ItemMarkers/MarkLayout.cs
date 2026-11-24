using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.ItemMarkers;

/// <summary>
/// The marks of one cell as controls, placed as the original places its widgets on the icon (DLL <c>FUN_18000b3c0</c>), in pixels of the
/// cell (fixed sizes times the interface scale). <see cref="Under"/> go beneath the item's picture (the original's depth 10000: frame, fill,
/// gradient), <see cref="Over"/> above it (depth -10000: the star and the badges).
/// </summary>
sealed class MarkLayout(MarkerSettings settings, MarkPictures pictures)
{
    public readonly List<UiControl> Under = [], Over = [];

    string _name = "";
    UiRect _area, _clip;
    float _scale = 1;

    /// <summary>Lays out <paramref name="item"/>'s marks for the cell <paramref name="name"/> at <paramref name="area"/>.</summary>
    public void Build(string name, UiRect area, UiRect? clip, float scale, in ItemFacts item, ItemGrade? grade, bool eye)
    {
        Under.Clear();
        Over.Clear();
        (_name, _area, _clip, _scale) = (name, area, clip ?? area, scale);
        if (area.Width < 1 || area.Height < 1) return;

        bool graded = settings.Rarity && grade is { } g && g.Rank >= settings.MinGrade;
        if (graded) AddGrade(grade!.Value.Colour);

        float left = graded && IsStar && settings.CornerIndex == 0 ? StarSize + 4 * scale : 2 * scale;
        float right = graded && IsStar && settings.CornerIndex == 1 ? StarSize + 4 * scale : 2 * scale;
        left = AddBadges(item, left);
        if (settings.TradeBags && item.Trade != TradeChange.None) AddBag(item.Trade, right);
        if (eye) AddEye();
    }

    bool IsStar => settings.StyleIndex == 3;

    float StarSize => (_area.Width < 80 * _scale ? 16 : 20) * _scale;

    float Strength => settings.RarityStrength / 10f;

    void AddGrade(Vector4 colour)
    {
        switch (settings.StyleIndex)
        {
            case 0:
                AddPicture(Under, "fill", MarkPictures.Fill, 0, 0, _area.Width, _area.Height, colour, Strength * 0.5f, solid: true);
                break;
            case 1:
                AddFrame(colour);
                break;
            case 2:
                if (!AddPicture(Under, "gradient", MarkPictures.Gradient, 0, 0, _area.Width, _area.Height, colour, Strength))
                    AddPicture(Under, "fill", null, 0, 0, _area.Width, _area.Height, colour, Strength * 0.5f, solid: true);
                break;
            default:
                AddStar(colour);
                break;
        }
    }

    /// <summary>Four glow strips along the edges, each a quarter of the cell's short side, 6 to 14 pixels.</summary>
    void AddFrame(Vector4 colour)
    {
        float w = _area.Width, h = _area.Height;
        float t = Math.Clamp(MathF.Floor(Math.Min(w, h) / 4), 6 * _scale, 14 * _scale);
        AddPicture(Under, "glow-top", MarkPictures.GlowTop, 0, 0, w, t, colour, Strength, solid: true);
        AddPicture(Under, "glow-bottom", MarkPictures.GlowBottom, 0, h - t, w, t, colour, Strength, solid: true);
        AddPicture(Under, "glow-left", MarkPictures.GlowLeft, 0, 0, t, h, colour, Strength, solid: true);
        AddPicture(Under, "glow-right", MarkPictures.GlowRight, w - t, 0, t, h, colour, Strength, solid: true);
    }

    /// <summary>The star in its corner, 2 pixels in: 16 pixels on a cell narrower than 80, else 20; not faded.</summary>
    void AddStar(Vector4 colour)
    {
        float size = StarSize, pad = 2 * _scale;
        int corner = settings.CornerIndex;
        float x = corner is 1 or 3 ? _area.Width - size - pad : pad;
        float y = corner < 2 ? pad : _area.Height - size - pad;
        AddPicture(Over, "star", MarkPictures.Star, x, y, size, size, colour, 1, solid: true);
    }

    /// <summary>The stolen badge, then the uniform badge, in a row along the top from <paramref name="x"/>; returns where the row ends.</summary>
    float AddBadges(in ItemFacts item, float x)
    {
        float pad = 2 * _scale;
        if (settings.Stolen && item.Stolen)
        {
            float size = settings.StolenSize * _scale;
            AddPointer("stolen", "pic_PointerSteal", x, pad, size, Vector4.One, settings.StolenStrength / 10f);
            x += 1 * _scale + size;
        }

        if (settings.Faction && item.Uniform)
            AddPointer("uniform", "pic_PointerHand", x, pad, settings.FactionSize * _scale, Vector4.One, settings.FactionStrength / 10f);

        return x;
    }

    /// <summary>The trade bag in the top right corner, the stolen badge's size, tinted sold or bought; the steal pointer without the picture.</summary>
    void AddBag(TradeChange change, float right)
    {
        float size = settings.StolenSize * _scale;
        var colour = (change == TradeChange.Bought ? Grades.Hex(settings.TradeBoughtColour) : Grades.Hex(settings.TradeSoldColour)) ?? Vector4.One;
        float x = _area.Width - right - size, y = 2 * _scale;
        if (!AddPicture(Over, "bag", MarkPictures.Bag, x, y, size, size, colour, 1)) AddPointer("bag", "pic_PointerSteal", x, y, size, colour, 1);
    }

    /// <summary>The eye in the lower left corner: two fifths of the cell's short side, 14 to 28 pixels; not tinted.</summary>
    void AddEye()
    {
        float size = Math.Clamp(MathF.Floor(Math.Min(_area.Width, _area.Height) * 2 / 5), 14 * _scale, 28 * _scale), pad = 2 * _scale;
        AddPicture(Over, "known", MarkPictures.Known, pad, _area.Height - size - pad, size, size, Vector4.One, 1);
    }

    /// <summary>
    /// One of the original's pictures, tinted and faded; without it (the original not installed) a plain rectangle when <paramref name="solid"/>,
    /// else nothing. False when nothing was added.
    /// </summary>
    bool AddPicture(List<UiControl> into, string part, string? file, float x, float y, float w, float h, Vector4 colour, float alpha,
        bool solid = false)
    {
        var bounds = new UiRect(_area.X + x, _area.Y + y, w, h);
        if (file is not null && pictures.Texture(file) is { } texture)
        {
            var properties = new Dictionary<string, string> { ["ImageTexture"] = texture, ["Colour"] = Rgb(colour), ["Alpha"] = Number(alpha) };
            into.Add(new($"{_name}-im-{part}", "ImageBox", bounds, "", "", "", true, properties, Vector4.Zero, Pickable: false, Clip: _clip));
            return true;
        }

        if (!solid) return false;

        into.Add(new($"{_name}-im-{part}", "Widget", bounds, "", "", "", true, new Dictionary<string, string>(), colour with { W = alpha },
            Pickable: false, Clip: _clip));
        return true;
    }

    /// <summary>A pointer picture of the game's image sets (<c>kenshi_images.xml</c>: group and index <c>Pointer</c>).</summary>
    void AddPointer(string part, string imageSet, float x, float y, float size, Vector4 colour, float alpha)
    {
        var properties = new Dictionary<string, string>
        {
            ["ImageResource"] = imageSet,
            ["ImageGroup"] = "Pointer",
            ["ImageName"] = "Pointer",
            ["Colour"] = Rgb(colour),
            ["Alpha"] = Number(alpha),
        };
        Over.Add(new($"{_name}-im-{part}", "ImageBox", new(_area.X + x, _area.Y + y, size, size), "", "", "", true, properties, Vector4.Zero,
            Pickable: false, Clip: _clip));
    }

    static string Rgb(Vector4 c) => string.Create(CultureInfo.InvariantCulture, $"{c.X:0.###} {c.Y:0.###} {c.Z:0.###}");

    static string Number(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
