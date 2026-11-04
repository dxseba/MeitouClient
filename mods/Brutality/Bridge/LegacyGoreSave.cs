using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meitou.Data.Fcs;
using Meitou.Data.Save;

namespace Meitou.Brutality;

/// <summary>One-way import of the integrated mod's pinned save extensions; new saves use the DLL sidecar.</summary>
internal static class LegacyGoreSave
{
    internal const string ModId = "dxseba.brutality";
    const string ExperienceKey = "meitou brutality xp", HeadKey = "meitou head cut", LimbKey = "meitou limb cuts", TorsoKey = "meitou torso cut";
    const int MaximumFileBytes = 8 * 1024 * 1024;
    sealed record Files(string[] Head, string[] Torso, string[] Wounds);
    static readonly ConditionalWeakTable<SaveGame, Files> extra = new();

    internal static void Loaded(SaveFolder folder, SaveGame game)
    {
        if (game.ModData.ContainsKey(ModId)) return;
        extra.GetValue(game, _ => new(ReadLines(folder, "head-wounds.meitou"), ReadLines(folder, "torso-wounds.meitou"), ReadLines(folder, "wounds.meitou")));
    }
    internal static void Copied(SaveGame source, SaveGame copy)
    {
        if (extra.TryGetValue(source, out var files)) extra.GetValue(copy, _ => files);
    }
    static string[] ReadLines(SaveFolder folder, string name)
    {
        if (folder.Resolve(name) is not { } path) return [];
        try
        {
            if (new FileInfo(path).Length > MaximumFileBytes) return [];
            return Encoding.UTF8.GetString(folder.ReadAllBytes(name)).Split('\n');
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    internal static BrutalityDurableState? Character(SaveCharacter source)
    {
        if (source.GameState is not { } state || SaveIdentityAdapter.KeyFor(source.Handle) is not { } key) return null;
        float experience = BrutalityProgress.ValidExperience(state.Floats.GetValueOrDefault(ExperienceKey));
        HeadCutState? head = null;
        if (Tokens(state, HeadKey) is { Length: 6 } h && h[0] == "H1" && Integer(h[1], out int mode)
            && Plane(h, 2, out var headPlane))
        {
            var cut = new HeadCutState(headPlane, (HeadCutMode)mode);
            if (cut.IsValid) head = cut;
        }
        TorsoBisectionState? torso = null;
        if (Tokens(state, TorsoKey) is { Length: 5 } t && Plane(t, 0, out var torsoPlane) && t[4] is "0" or "1")
        {
            var cut = new TorsoBisectionState(torsoPlane, t[4] == "1");
            if (cut.IsValid) torso = cut;
        }
        if (torso is null && state.Strings.GetValueOrDefault("meitou ragdoll")?.Split('|')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 6 } header
            && header[0] == "T2" && header[5] is "0" or "1" && Plane(header, 1, out var compositePlane))
        {
            var cut = new TorsoBisectionState(compositePlane, header[5] == "1");
            if (cut.IsValid) torso = cut;
        }
        var limbs = Limbs(Tokens(state, LimbKey));
        return state.Floats.ContainsKey(ExperienceKey) || head is not null || torso is not null || limbs.Length > 0
            ? new(key, experience, new(0), head, torso, limbs) : null;
    }

    internal static IReadOnlyList<BrutalityDurableState> Read(SaveGame save)
    {
        var characters = save.Characters.Where(c => !c.Handle.IsNull).ToArray();
        var result = new Dictionary<string, BrutalityDurableState>(StringComparer.Ordinal);
        foreach (var character in characters)
            if (Character(character) is { } state) result[state.Key] = state;
        if (!extra.TryGetValue(save, out var files)) return result.Values.ToArray();
        // The old files stored only C/I. Resolve only a unique character in this exact save, then immediately promote to its full handle.
        var unique = characters.GroupBy(c => (c.Handle.C, c.Handle.I)).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => SaveIdentityAdapter.KeyFor(group.Single().Handle)!);
        BrutalityDurableState Existing(string key) => result.GetValueOrDefault(key) ?? new(key, 0, new(0), null, null, []);
        foreach (var row in Rows(files.Head, "MEITOU-HEAD-WOUNDS", 3))
            if (HeadRow(row.Fields, row.Version, out var handle, out var state) && unique.TryGetValue(handle, out var key))
                result[key] = Existing(key) with { SevereHead = state };
        foreach (var row in Rows(files.Torso, "MEITOU-TORSO-WOUNDS", 2))
            if (TorsoRow(row.Fields, row.Version, out var handle, out var state) && unique.TryGetValue(handle, out var key))
                result[key] = Existing(key) with { Torso = Existing(key).Torso ?? state };
        var ordinary = new Dictionary<string, WoundBody>(StringComparer.Ordinal);
        foreach (var line in files.Wounds)
            if (OrdinaryRow(line, out var handle, out var wound) && unique.TryGetValue(handle, out var key))
            {
                if (!ordinary.TryGetValue(key, out var body)) ordinary[key] = body = new();
                body.Restore(wound.Region, wound.Mark);
            }
        foreach (var (key, body) in ordinary) result[key] = Existing(key) with { Wounds = body.Snapshot() };
        return result.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
    }
    static bool OrdinaryRow(string text, out (int C, int I) handle, out BodyWoundEntry wound)
    {
        handle = default; wound = default;
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (17 or 18 or 19) || !Integer(fields[0], out int c) || !Integer(fields[1], out int i) || c < 0 || i < 0
            || !Integer(fields[2], out int region) || !Integer(fields[3], out int segment)
            || !Integer(fields[13], out int hits) || !Integer(fields[14], out int age)
            || !long.TryParse(fields[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out long born)) return false;
        Span<float> values = stackalloc float[9];
        for (int n = 0; n < values.Length; n++) if (!Number(fields[4 + n], out values[n])) return false;
        if (!Number(fields[15], out float size)) return false;
        int kind = 0, category = -1;
        if (fields.Length >= 18 && !Integer(fields[17], out kind) || fields.Length >= 19 && !Integer(fields[18], out category)) return false;
        var shape = new WoundShape(values[4], values[5], values[6], values[7], (WoundKind)kind, category);
        wound = new(region, new(segment, values[0], new(values[1], values[2], values[3]), shape, values[8], hits, age, size, born));
        handle = (c, i);
        return new BodyWoundState([wound]).IsValid;
    }
    static IEnumerable<(int Version, string[] Fields)> Rows(string[] lines, string header, int maximumVersion)
    {
        int version = 0;
        foreach (string line in lines)
        {
            var fields = line.Trim().TrimStart('\uFEFF').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || fields[0].StartsWith('#')) continue;
            if (version == 0)
            {
                if (fields.Length != 2 || fields[0] != header || !Integer(fields[1], out version) || version < 1 || version > maximumVersion) yield break;
                continue;
            }
            yield return (version, fields);
        }
    }
    static bool HeadRow(string[] fields, int version, out (int C, int I) handle, out SevereHeadWoundState state)
    {
        handle = default; state = default;
        int fixedCount = version == 1 ? 6 : 7, placedCount = version == 3 ? 11 : 9;
        if (fields.Length != fixedCount && fields.Length != fixedCount + placedCount) return false;
        if (fields[0] != "H" || !Integer(fields[1], out int c) || !Integer(fields[2], out int i) || c < 0 || i < 0
            || !Integer(fields[3], out int hits) || !Integer(fields[4], out int kind) || !Integer(fields[5], out int profile)) return false;
        int mechanism = (int)CombatWoundMechanism.Slash;
        if (version >= 2 && !Integer(fields[6], out mechanism)) return false;
        HeadWoundPlacement? placement = null;
        if (fields.Length > fixedCount)
        {
            Span<float> values = stackalloc float[11];
            values[9] = values[10] = 1;
            for (int n = 0; n < placedCount; n++) if (!Number(fields[fixedCount + n], out values[n])) return false;
            placement = new(new(values[0], values[1], values[2]), new(values[3], values[4], values[5]),
                new(values[6], values[7], values[8]), values[9], values[10]);
        }
        state = new(hits, (SevereHeadWoundKind)kind, profile, placement, (CombatWoundMechanism)mechanism);
        handle = (c, i);
        return state.IsValid;
    }
    static bool TorsoRow(string[] fields, int version, out (int C, int I) handle, out TorsoBisectionState state)
    {
        handle = default; state = default;
        if (fields.Length != (version == 1 ? 7 : 8) || fields[0] != "T"
            || !Integer(fields[1], out int c) || !Integer(fields[2], out int i) || c < 0 || i < 0 || !Plane(fields, 3, out var plane)) return false;
        if (version == 2 && fields[7] is not ("0" or "1")) return false;
        state = new(plane, version == 2 && fields[7] == "1"); handle = (c, i);
        return state.IsValid;
    }
    static LimbCutState[] Limbs(string[]? fields)
    {
        if (fields is null || fields.Length is < 5 or > 17 || fields[0] != "L1" || (fields.Length - 1) % 4 != 0) return [];
        var cuts = new List<LimbCutState>(4);
        for (int i = 1; i < fields.Length; i += 4)
        {
            if (!Integer(fields[i], out int slot) || !Number(fields[i + 1], out float fraction)
                || !Number(fields[i + 2], out float x) || !Number(fields[i + 3], out float y)) return [];
            var cut = new LimbCutState(slot, fraction, new(x, y));
            if (!cut.IsValid || cuts.Any(existing => existing.LimbSlot == slot)) return [];
            cuts.Add(cut);
        }
        return cuts.OrderBy(cut => cut.LimbSlot).ToArray();
    }
    static string[]? Tokens(FcsRecord record, string key) => record.Strings.TryGetValue(key, out var text)
        ? text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : null;
    static bool Integer(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    static bool Number(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    static bool Plane(string[] fields, int start, out System.Numerics.Vector4 plane)
    {
        plane = default;
        if (!Number(fields[start], out float x) || !Number(fields[start + 1], out float y)
            || !Number(fields[start + 2], out float z) || !Number(fields[start + 3], out float w)) return false;
        plane = new(x, y, z, w); return true;
    }
}

