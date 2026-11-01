using System.Globalization;
using System.Numerics;

namespace Meitou.Simulation.Wounds;

/// <summary>A fixed plane in the original body bind space: dot(Plane.xyz, position) + Plane.W = 0; positive and negative halves follow the plane. FullBody permits cuts through limbs.</summary>
public readonly record struct TorsoBisectionState(Vector4 Plane, bool FullBody = false)
{
    public bool IsValid
    {
        get
        {
            if (!float.IsFinite(Plane.X) || !float.IsFinite(Plane.Y) || !float.IsFinite(Plane.Z) || !float.IsFinite(Plane.W)) return false;
            float length = new Vector3(Plane.X, Plane.Y, Plane.Z).LengthSquared();
            // Transverse cuts retain the legacy upward limit; full-body planes may be vertical or steep.
            return float.IsFinite(length) && MathF.Abs(length - 1) <= 0.001f && (FullBody || Plane.Y / MathF.Sqrt(length) >= 0.5f);
        }
    }
}

public readonly record struct TorsoBisectionReadResult(bool SupportedVersion, int Restored, int Pending, int Rejected);

/// <summary>Main-thread torso separation state, keyed by stable saved character handles while NPCs are not spawned.</summary>
public sealed class TorsoBisections
{
    public const string FileName = "torso-wounds.meitou";
    public const string Header = "MEITOU-TORSO-WOUNDS 2";
    const string LegacyHeader = "MEITOU-TORSO-WOUNDS 1";
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    readonly Dictionary<CharacterId, TorsoBisectionState> live = [];
    readonly Dictionary<(int C, int I), TorsoBisectionState> pending = [];
    readonly Dictionary<CharacterId, (int C, int I)> bindings = [];

    public IEnumerable<KeyValuePair<CharacterId, TorsoBisectionState>> All => live;
    public int PendingCount => pending.Count;
    public TorsoBisectionState? Of(CharacterId id) => live.TryGetValue(id, out var state) ? state : null;
    public bool Set(CharacterId id, TorsoBisectionState state)
    {
        if (id.IsNone || !state.IsValid) return false;
        live[id] = state;
        return true;
    }
    public void Remove(CharacterId id)
    {
        live.Remove(id);
        if (bindings.Remove(id, out var saved)) pending.Remove(saved);
    }
    public void Clear() { live.Clear(); pending.Clear(); bindings.Clear(); }

    /// <summary>Associates an existing live state with its stable save handle. The caller's current runtime identity is authoritative.</summary>
    public bool Bind(CharacterId id, (int C, int I) handle)
    {
        if (id.IsNone || handle.C < 0 || handle.I < 0 || !live.ContainsKey(id)) return false;
        if (bindings.TryGetValue(id, out var existing) && existing == handle)
        {
            pending.Remove(handle);
            return true;
        }
        foreach (var (other, saved) in bindings.ToArray())
            if (other != id && saved == handle)
            {
                bindings.Remove(other);
                live.Remove(other);
            }
        bindings[id] = handle;
        pending.Remove(handle);
        return true;
    }

    /// <summary>
    /// Refreshes saved identities after NPC streaming. An unloaded character waits by handle; a re-spawned one receives the old state under its new generation.
    /// The optional live check keeps a freshly saved, still-active character whose new handle is not in the load resolver yet.
    /// Returns identities newly bound/rebound, so visual one-shot effects can be suppressed after restoration.
    /// </summary>
    public IReadOnlyList<CharacterId> Refresh(Func<(int C, int I), CharacterId?> character, Func<CharacterId, bool>? isAlive = null)
    {
        var restored = new HashSet<CharacterId>();
        foreach (var (id, handle) in bindings.ToArray())
        {
            if (!live.TryGetValue(id, out var state)) { bindings.Remove(id); continue; }
            var current = character(handle);
            if (current == id) continue;
            if (current is not { IsNone: false } && isAlive?.Invoke(id) == true) continue;
            bindings.Remove(id);
            live.Remove(id);
            if (current is { IsNone: false } target)
            {
                live.TryAdd(target, state); // A newer impact on the replacement generation wins.
                bindings[target] = handle;
                restored.Add(target);
            }
            else pending[handle] = state;
        }
        foreach (var (handle, state) in pending.ToArray())
            if (character(handle) is { IsNone: false } id)
            {
                live.TryAdd(id, state);
                bindings[id] = handle;
                pending.Remove(handle);
                restored.Add(id);
            }
        return restored.ToArray();
    }

    /// <summary>
    /// Drops obsolete runtime identities with no stable save binding. Call only after an owner has retained any streamed state elsewhere.
    /// Bound and pending states remain available for later saved-character restoration.
    /// </summary>
    public int PruneUnboundInactive(Func<CharacterId, bool> isAlive)
    {
        int removed = 0;
        foreach (var id in live.Keys.ToArray())
            if (!bindings.ContainsKey(id) && !isAlive(id))
            {
                live.Remove(id);
                removed++;
            }
        return removed;
    }
    /// <summary>A mapped live state wins over its older pending record if a new impact already changed it.</summary>
    public int Resolve(Func<(int C, int I), CharacterId?> character) => Refresh(character).Count;

    /// <summary>Snapshot before background writing. Cold records override remembered data; current captured live identities remain authoritative.</summary>
    public IEnumerable<string> Write(Func<CharacterId, (int C, int I)?> handle, Func<(int C, int I), bool>? exists = null,
        IEnumerable<((int C, int I) Handle, TorsoBisectionState State)>? additionalRecords = null)
    {
        bool Keep((int C, int I) saved) => saved.C >= 0 && saved.I >= 0 && exists?.Invoke(saved) != false;
        var records = pending.Where(p => Keep(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var current = live.Select(p => (Id: p.Key, State: p.Value, Handle: handle(p.Key))).ToArray();
        foreach (var entry in current)
            if (entry.Handle is null && bindings.TryGetValue(entry.Id, out var saved) && Keep(saved)) records[saved] = entry.State;
        if (additionalRecords is not null)
            foreach (var (saved, state) in additionalRecords)
                if (state.IsValid && Keep(saved)) records[saved] = state;
        foreach (var entry in current)
            if (entry.Handle is { } saved && Keep(saved)) records[saved] = entry.State;
        yield return Header;
        foreach (var (saved, state) in records.OrderBy(p => p.Key.C).ThenBy(p => p.Key.I))
        {
            var p = state.Plane;
            yield return string.Create(Culture, $"T {saved.C} {saved.I} {p.X:R} {p.Y:R} {p.Z:R} {p.W:R} {(state.FullBody ? 1 : 0)}");
        }
    }

    /// <summary>Missing, legacy unrelated or unknown-version data leaves no separation. Import filters discard omitted characters before pending assignment.</summary>
    public TorsoBisectionReadResult Read(IEnumerable<string> lines, Func<(int C, int I), CharacterId?> character, Func<(int C, int I), bool>? exists = null)
    {
        Clear();
        int version = 0;
        int rejected = 0;
        foreach (var line in lines)
        {
            string text = line.Trim().TrimStart('\uFEFF');
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (version == 0)
            {
                version = text switch { Header => 2, LegacyHeader => 1, _ => 0 };
                if (version == 0) return new(false, 0, 0, 0);
                continue;
            }
            if (!TryRow(text, version, out var handle, out var state)) { rejected++; continue; }
            if (exists?.Invoke(handle) == false) continue;
            pending[handle] = state;
        }
        if (version == 0) return new(false, 0, 0, 0);
        int restored = Resolve(character);
        return new(true, restored, pending.Count, rejected);
    }

    static bool TryRow(string line, int version, out (int C, int I) handle, out TorsoBisectionState state)
    {
        handle = default;
        state = default;
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != (version == 1 ? 7 : 8) || fields[0] != "T"
            || !int.TryParse(fields[1], NumberStyles.Integer, Culture, out int container) || container < 0
            || !int.TryParse(fields[2], NumberStyles.Integer, Culture, out int instance) || instance < 0) return false;
        Span<float> values = stackalloc float[4];
        for (int i = 0; i < values.Length; i++)
            if (!float.TryParse(fields[i + 3], NumberStyles.Float, Culture, out values[i]) || !float.IsFinite(values[i])) return false;
        bool fullBody = false;
        if (version == 2)
        {
            if (!int.TryParse(fields[7], NumberStyles.Integer, Culture, out int flag) || flag is < 0 or > 1) return false;
            fullBody = flag == 1;
        }
        state = new(new(values[0], values[1], values[2], values[3]), fullBody);
        if (!state.IsValid) return false;
        handle = (container, instance);
        return true;
    }
}
