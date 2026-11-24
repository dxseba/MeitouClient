using System.Globalization;
using System.Numerics;

namespace Meitou.KathunLightsabers;

/// <summary>A saber colour: its name (the halo is <c>KSG_halo_&lt;name&gt;</c>), its LIGHT record and its glow colour.</summary>
sealed record SaberColour(string Name, string LightId, Vector3 Glow);

/// <summary>A texture keyword: a saber whose textures hold it; its colour, or null when the model's entry says it.</summary>
sealed record SaberTexture(string Keyword, float Tint, string? Colour);

/// <summary>A saber model by file name: its colour (null: its texture's) and where its blades run along the model's y.</summary>
sealed record SaberMesh(string File, string? Colour, IReadOnlyList<(float From, float To)> Blades);

/// <summary>
/// The original's settings file (<c>KathunSaberGlow.cfg</c>, README "Facts"): <c>key=value</c> lines, <c>#</c> comments; the colour, texture and
/// mesh lines add entries, the rest set one value each. A value that does not read leaves the default.
/// </summary>
sealed class SaberConfig
{
    public List<SaberColour> Colours { get; } = [];
    public List<SaberTexture> Textures { get; } = [];
    public Dictionary<string, SaberMesh> Meshes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public float GlowBrightness { get; private set; } = 0.6f;
    public float LightAlong { get; private set; } = 5;
    public bool Light { get; private set; } = true;
    public bool Halo { get; private set; } = true;
    public float HaloStep { get; private set; } = 1.2f;
    public float Range { get; private set; } = 1500;
    public int MaxLights { get; private set; } = 12;

    public SaberColour? Colour(string? name) => name is null ? null : Colours.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static SaberConfig Read(IEnumerable<string> lines, Action<string> log)
    {
        var config = new SaberConfig();
        int bad = 0;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int eq = line.IndexOf('=');
            if (eq <= 0 || !config.Apply(line[..eq].Trim(), line[(eq + 1)..].Trim())) bad++;
        }

        if (bad > 0) log($"{bad} settings lines not read");
        return config;
    }

    bool Apply(string key, string value) => key switch
    {
        "colour" => AddColour(value),
        "texture" => AddTexture(value),
        "mesh" => AddMesh(value),
        "glow_brightness" => SetFloat(value, v => GlowBrightness = v),
        "light_along" => SetFloat(value, v => LightAlong = v),
        "halo_step" => SetFloat(value, v => HaloStep = v, above: 0.2f),
        "range" => SetFloat(value, v => Range = v, above: 0),
        "light" => SetInt(value, v => Light = v != 0),
        "halo" => SetInt(value, v => Halo = v != 0),
        "max_lights" => SetInt(value, v => MaxLights = v, notNegative: true),
        _ => false,
    };

    bool AddColour(string value)
    {
        string[] parts = value.Split('|');
        if (parts.Length != 3 || Floats(parts[2]) is not [var r, var g, var b]) return false;

        Colours.Add(new SaberColour(parts[0].Trim(), parts[1].Trim(), new Vector3(r, g, b)));
        return true;
    }

    bool AddTexture(string value)
    {
        string[] parts = value.Split('|');
        if (parts.Length != 4 || !TryFloat(parts[2], out float tint)) return false;

        Textures.Add(new SaberTexture(parts[0].Trim(), tint, NamedColour(parts[3])));
        return true;
    }

    bool AddMesh(string value)
    {
        string[] parts = value.Split('|');
        if (parts.Length < 3) return false;

        var blades = new List<(float, float)>();
        foreach (string range in parts.Skip(2))
        {
            if (Floats(range) is not [var from, var to]) return false;
            blades.Add((from, to));
        }

        string file = parts[0].Trim();
        Meshes[file] = new SaberMesh(file, NamedColour(parts[1]), blades);
        return true;
    }

    static string? NamedColour(string value) => value.Trim() is "-" or "" ? null : value.Trim();

    static float[]? Floats(string value)
    {
        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new float[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            if (!TryFloat(words[i], out result[i])) return null;
        }

        return result;
    }

    static bool TryFloat(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    static bool SetFloat(string value, Action<float> set, float above = float.NegativeInfinity)
    {
        if (!TryFloat(value, out float v) || v <= above) return false;

        set(v);
        return true;
    }

    static bool SetInt(string value, Action<int> set, bool notNegative = false)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || notNegative && v < 0) return false;

        set(v);
        return true;
    }
}
