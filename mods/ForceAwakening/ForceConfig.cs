using System.Globalization;

namespace Meitou.ForceAwakening;

/// <summary>One of the four paths (the original's class keys in <c>TheForceAwakening.cfg</c>).</summary>
enum ForcePath : byte
{
    None = 0,
    Jedi = 1,
    Sith = 2,
    Hunter = 3,
    Grey = 4,
}

/// <summary>
/// One <c>spell&lt;N&gt;=kind|name|cost (or "tudo")|cast time|cooldown|power|level to learn|class|strike animation</c> line of the original's
/// <c>TheForceAwakening.cfg</c>. <see cref="Full"/>: "tudo", the whole pool, which must be full.
/// </summary>
sealed record Spell(int Number, string Kind, string Name, float Cost, bool Full, float CastTime, float Cooldown, float Power, float Level,
    ForcePath Path, string? Strike);

/// <summary>
/// The original's settings, read at start from its <c>TheForceAwakening.cfg</c> (the paths: spells, passives, items, start, elements) and
/// <c>ForceBase.cfg</c> (the engine's balance: ranges, pools, sounds) and its <c>sounds.cfg</c>; the defaults below are those files' values, used when a
/// line is missing.
/// </summary>
sealed class ForceConfig
{
    public List<Spell> Spells { get; } = [];

    /// <summary><c>passive=&lt;class&gt;|&lt;path 1-2&gt;|&lt;passive 1-3&gt;|&lt;value&gt;</c>.</summary>
    public Dictionary<(ForcePath Path, int Tree, int Index), float> Passives { get; } = [];

    /// <summary><c>class_item=&lt;item&gt;|&lt;class&gt;</c>: the item that teaches a path when a pathless player character carries it.</summary>
    public Dictionary<string, ForcePath> ClassItems { get; } = new(StringComparer.Ordinal);

    /// <summary><c>start_class=&lt;character&gt;|&lt;class&gt;</c>: a character of that record is born on the path.</summary>
    public Dictionary<string, ForcePath> StartClasses { get; } = new(StringComparer.Ordinal);

    /// <summary><c>bounty=&lt;character&gt;|&lt;cats&gt;|&lt;where he is usually&gt;</c>: the Veteran Hunter's contracts, in file order.</summary>
    public List<(string Character, long Cats, string Where)> Bounties { get; } = [];

    /// <summary><c>brainwash_faction=</c>: the factions the grimoire's Brainwash button offers after "your faction", in order.</summary>
    public List<string> BrainwashFactions { get; } = [];

    /// <summary><c>cast_animations=</c>: the clips a cast-time skill plays while it is cast (the first the character has).</summary>
    public string[] CastAnimations => Text("cast_animations", "aimshoot").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary><c>skill_damage</c> (all skill damage x) and <c>dodge_max</c> (the share of it a target's Dodge 100 takes off).</summary>
    public float SkillDamage => Number("skill_damage", 0.8f);
    public float DodgeMax => Number("dodge_max", 0.3f);

    /// <summary><c>sounds.cfg</c>: <c>&lt;skill&gt;.&lt;cast|release|impact&gt;</c> to its files under <c>sounds\</c> (one drawn each time).</summary>
    public Dictionary<string, string[]> Sounds { get; } = new(StringComparer.Ordinal);

    /// <summary>The <c>ForceBase.cfg</c> numbers by key.</summary>
    readonly Dictionary<string, string> _base = new(StringComparer.Ordinal);

    public float Number(string key, float fallback) =>
        _base.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    public string Text(string key, string fallback) => _base.TryGetValue(key, out var text) && text.Length > 0 ? text : fallback;

    public float CastRange => Number("cast_range", 450);
    public float ManaBase => Number("mana_base", 50);
    public float ManaPerWisdom => Number("mana_per_int", 3);
    public float RegenBase => Number("regen_base", 1.5f);
    public float RegenPerWisdom => Number("regen_per_int", 0.05f);
    public float PowerPerLevel => Number("power_per_arcane", 0.025f);
    public float CostCutPerLevel => Number("cost_cut_per_arcane", 0.004f);
    public float XpMult => Number("xp_mult", 1);
    public float FuryHit => Number("fury_hit", 6);
    public float FuryHurt => Number("fury_hurt", 4);
    public float FuryDecay => Number("fury_decay", 3);
    public float FuryDecayDelay => Number("fury_decay_delay", 8);
    public float FuryFloor => Number("fury_floor", 20);
    public float FuryFloorRegen => Number("fury_floor_regen", 2);
    public float FocusRegen => Number("focus_regen", 6);
    public float HuntDuration => Number("hunt_duration", 15);
    public float EmpRadius => Number("emp_radius", 45);
    public float RushRange => Number("rush_range", 120);
    public float SoundVolume => Number("sound_volume", 1);
    public float SoundRange => Number("sound_range", 900);
    public float HudBottom => Number("hud_bottom", 230);
    public float AutoGapPlayer => Number("auto_gap_player", 0.5f);
    public float AutoGapNpc => Number("auto_gap_npc", 2);

    /// <summary><c>passive_levels</c>: the path level at which each of a tree's three passives opens.</summary>
    public float[] PassiveLevels
    {
        get
        {
            var parts = Text("passive_levels", "20|45|70").Split('|');
            var levels = parts.Select(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 999).ToArray();
            return levels.Length >= 3 ? levels : [20, 45, 70];
        }
    }

    public static ForcePath PathOf(string key) => key switch
    {
        "jedi" => ForcePath.Jedi,
        "sith" => ForcePath.Sith,
        "cacador" => ForcePath.Hunter,
        "cinzento" => ForcePath.Grey,
        _ => ForcePath.None,
    };

    public static string KeyOf(ForcePath path) => path switch
    {
        ForcePath.Jedi => "jedi",
        ForcePath.Sith => "sith",
        ForcePath.Hunter => "cacador",
        ForcePath.Grey => "cinzento",
        _ => "",
    };

    public IEnumerable<Spell> SpellsOf(ForcePath path) => Spells.Where(s => s.Path == path).OrderBy(s => s.Level).ThenBy(s => s.Number);

    public Spell? SpellByNumber(int number) => Spells.Find(s => s.Number == number);

    /// <summary>Reads the three files from the installed original (or this mod's folder); logs what is missing.</summary>
    public static ForceConfig Read(string folder, Action<string> log)
    {
        var config = new ForceConfig();
        foreach (var (key, value) in Lines(OriginalFiles.Find(folder, "ForceBase.cfg", log), log)) config._base[key] = value;
        foreach (var (key, value) in Lines(OriginalFiles.Find(folder, "TheForceAwakening.cfg", log), log)) config.Take(key, value);
        foreach (var (key, value) in Lines(OriginalFiles.Find(folder, "sounds.cfg", log), log)) config.Sounds[key] = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (config.Spells.Count == 0) log($"the original's TheForceAwakening.cfg was not found (Workshop {OriginalFiles.OriginalItem}): no skills");
        return config;
    }

    void Take(string key, string value)
    {
        var f = value.Split('|');
        if (key.StartsWith("spell", StringComparison.Ordinal) && int.TryParse(key[5..], CultureInfo.InvariantCulture, out int number))
        {
            if (ParseSpell(number, f) is { } spell) Spells.Add(spell);
        }
        else if (key == "passive" && f.Length >= 4 && int.TryParse(f[1], out int tree) && int.TryParse(f[2], out int index))
        {
            Passives[(PathOf(f[0]), tree, index)] = Float(f[3]);
        }
        else if (key == "class_item" && f.Length >= 2) ClassItems[f[0]] = PathOf(f[1]);
        else if (key == "start_class" && f.Length >= 2) StartClasses[f[0]] = PathOf(f[1]);
        else if (key == "bounty" && f.Length >= 2 && long.TryParse(f[1], CultureInfo.InvariantCulture, out long cats))
            Bounties.Add((f[0], cats, f.Length > 2 ? f[2] : ""));
        else if (key == "brainwash_faction" && value.Length > 0) BrainwashFactions.Add(value);
    }

    static Spell? ParseSpell(int number, string[] f)
    {
        if (f.Length < 8 || PathOf(f[7]) is var path && path == ForcePath.None) return null;

        bool full = f[2] == "tudo";
        return new Spell(number, f[0], f[1], full ? 0 : Float(f[2]), full, Float(f[3]), Float(f[4]), Float(f[5]), Float(f[6]), path,
            f.Length > 8 && f[8].Length > 0 ? f[8] : null);
    }

    static float Float(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;

    /// <summary>The <c>key=value</c> lines of a cfg file (# starts a comment line).</summary>
    static IEnumerable<(string Key, string Value)> Lines(string? path, Action<string> log)
    {
        if (path is null) yield break;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"{Path.GetFileName(path)} not read: {e.Message}");
            yield break;
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;

            yield return (line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
    }
}
