using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// The original's <c>StatGUI.ini</c> (<b>Verified</b>, original source): the stats window's "Level Cap" line and the stat name colours by cap kind
/// and XP rate. Without the file the label is "Level Cap: " and nothing is coloured (the original's colours are its author's and not shipped).
/// </summary>
sealed class GuiConfig
{
    /// <summary>The colour slots in the original's order: high cap (buff, plain, nerf), buff, nerf, low cap (3), special cap (3).</summary>
    static readonly string[] Slots =
        ["highcapbuff", "highcap", "highcapnerf", "buff", "nerf", "lowcapbuff", "lowcap", "lowcapnerf", "specialcapbuff", "specialcap", "specialcapnerf"];

    public string Label = "Level Cap: ";
    public bool Colours;
    public bool GoldByValue;
    public float RedAtMost = 80, GreenAtLeast = 100, GoldAtLeast = 120;
    public HashSet<StatsEnumerated> Attributes { get; } = [];
    public Vector3?[] SkillNames { get; } = new Vector3?[Slots.Length];
    public Vector3?[] AttributeNames { get; } = new Vector3?[Slots.Length];
    public Vector3? HighCap, LowCap, SpecialCap, Buff, Nerf;

    public static GuiConfig Load(string? path, Action<string> log)
    {
        var gui = new GuiConfig();
        if (path is null) return gui;

        foreach (var (_, key, value) in Ini.Read(path)) gui.Apply(key.ToLowerInvariant(), value);
        log($"StatGUI.ini read from {path}");
        return gui;
    }

    void Apply(string key, string value)
    {
        switch (key)
        {
            case "levelcaplabel": Label = value; return;
            case "enablestatcolors": Colours = Ini.Switch(value); return;
            case "goldcapbyvalue": GoldByValue = Ini.Switch(value); return;
            case "redcapthreshold": RedAtMost = Ini.Number(value); return;
            case "greencapthreshold": GreenAtLeast = Ini.Number(value); return;
            case "goldcapthreshold": GoldAtLeast = Ini.Number(value); return;
            case "attrstats": ReadAttributes(value); return;
            case "highcap": HighCap = Colour(value, HighCap); return;
            case "lowcap": LowCap = Colour(value, LowCap); return;
            case "specialcap": SpecialCap = Colour(value, SpecialCap); return;
            case "buff": Buff = Colour(value, Buff); return;
            case "nerf": Nerf = Colour(value, Nerf); return;
        }

        ApplySlot(key, value);
    }

    void ApplySlot(string key, string value)
    {
        var (prefix, table) = key.StartsWith("skill", StringComparison.Ordinal) ? ("skill", SkillNames)
            : key.StartsWith("attr", StringComparison.Ordinal) ? ("attr", AttributeNames)
            : ("", null);
        int slot = table is null ? -1 : Array.IndexOf(Slots, key[prefix.Length..]);
        if (slot >= 0) table![slot] = Colour(value, table[slot]);
    }

    void ReadAttributes(string value)
    {
        Attributes.Clear();
        foreach (string token in value.Split(','))
            if (StatNames.Find(token) is not StatsEnumerated.None and var stat) Attributes.Add(stat);
    }

    /// <summary><c>r,g,b</c> in 0 to 255: a negative part turns the colour off, a part above 255 is held there; fewer parts keep <paramref name="old"/>.</summary>
    static Vector3? Colour(string value, Vector3? old)
    {
        string[] parts = value.Split(',', 3);
        if (parts.Length < 3) return old;

        var c = new Vector3(Ini.Number(parts[0]), Ini.Number(parts[1]), Ini.Number(parts[2])) / 255;
        return c.X < 0 || c.Y < 0 || c.Z < 0 ? null : Vector3.Min(c, Vector3.One);
    }

    /// <summary>The cap kind: 3 special (by value with <see cref="GoldByValue"/>, else a talent's own cap), 1 high, 2 low, 0 plain.</summary>
    public int CapKind(float cap, bool talentCap)
    {
        if (GoldByValue ? cap >= GoldAtLeast : talentCap) return 3;
        if (cap >= GreenAtLeast) return 1;
        return cap <= RedAtMost ? 2 : 0;
    }

    /// <summary>A stat name's colour for its cap kind and XP rate (1 above, -1 below 1), else null (the stat keeps the window's colour).</summary>
    public Vector3? NameColour(StatsEnumerated stat, int capKind, int rateKind)
    {
        int slot = capKind switch
        {
            1 => rateKind > 0 ? 0 : rateKind < 0 ? 2 : 1,
            2 => rateKind > 0 ? 5 : rateKind < 0 ? 7 : 6,
            3 => rateKind > 0 ? 8 : rateKind < 0 ? 10 : 9,
            _ => rateKind > 0 ? 3 : rateKind < 0 ? 4 : -1,
        };

        return slot < 0 ? null : (Attributes.Contains(stat) ? AttributeNames : SkillNames)[slot];
    }

    /// <summary>A MyGUI colour tag (<c>#RRGGBB</c>).</summary>
    public static string Tag(Vector3 c) => string.Create(CultureInfo.InvariantCulture,
        $"#{Byte(c.X):X2}{Byte(c.Y):X2}{Byte(c.Z):X2}");

    /// <summary>A layout colour property ("r g b").</summary>
    public static string Property(Vector3 c) => string.Create(CultureInfo.InvariantCulture, $"{c.X:0.######} {c.Y:0.######} {c.Z:0.######}");

    static int Byte(float v) => Math.Clamp((int)(v * 255 + 0.5f), 0, 255);
}
