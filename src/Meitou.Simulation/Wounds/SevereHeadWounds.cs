using System.Globalization;
using System.Numerics;

namespace Meitou.Simulation.Wounds;

public enum SevereHeadWoundKind { None, JawLost, Scalp, BrokenSkull }

/// <summary>The fixed wound frame in the body's bind space, independent of its later world position or pose.</summary>
public readonly record struct HeadWoundPlacement(Vector3 Point, Vector3 Normal, Vector3 Tangent)
{
    public bool IsValid
    {
        get
        {
            static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
            if (!Finite(Point) || !Finite(Normal) || !Finite(Tangent)) return false;
            float normal = Normal.LengthSquared(), tangent = Tangent.LengthSquared();
            if (!float.IsFinite(normal) || !float.IsFinite(tangent) || normal < 1e-8f || tangent < 1e-8f) return false;
            // A parallel tangent cannot define the surface frame.
            float cross = Vector3.Cross(Normal / MathF.Sqrt(normal), Tangent / MathF.Sqrt(tangent)).LengthSquared();
            return float.IsFinite(cross) && cross > 1e-6f;
        }
    }
}

/// <summary>The severe visual head state, including heavy blows before an opening exists. Profile 0..5 selects the blade axis and force size.</summary>
public readonly record struct SevereHeadWoundState(int HeavyHits, SevereHeadWoundKind Kind = SevereHeadWoundKind.None, int Profile = 0, HeadWoundPlacement? Placement = null)
{
    public bool IsValid => HeavyHits >= 0 && Kind is >= SevereHeadWoundKind.None and <= SevereHeadWoundKind.BrokenSkull
        && Profile is >= 0 and < 6 && (Kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull || Profile == 0)
        && (Placement is not { } placement || placement.IsValid);

    /// <summary>The renderer's jaw bit 6, scalp/skull bits 7/8 and profile bits 9..11.</summary>
    public uint RendererFlags => Kind switch
    {
        SevereHeadWoundKind.JawLost => 1u << 6,
        SevereHeadWoundKind.Scalp => 1u << 7 | (uint)Profile << 9,
        SevereHeadWoundKind.BrokenSkull => 3u << 7 | (uint)Profile << 9,
        _ => 0,
    };

    public static SevereHeadWoundState FromRendererFlags(int heavyHits, uint flags, HeadWoundPlacement? placement = null)
    {
        var kind = (flags & (1u << 6)) != 0 ? SevereHeadWoundKind.JawLost
            : (flags >> 7 & 3) >= 2 ? SevereHeadWoundKind.BrokenSkull
            : (flags & (1u << 7)) != 0 ? SevereHeadWoundKind.Scalp : SevereHeadWoundKind.None;
        int profile = kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull ? (int)(flags >> 9 & 7) : 0;
        return new(heavyHits, kind, profile, placement);
    }
}

public readonly record struct HeadWoundReadResult(bool SupportedVersion, int Restored, int Pending, int Rejected);

/// <summary>
/// Main-thread severe head state. The versioned sidecar uses save character handles, never runtime table slots.
/// Unspawned saved characters remain pending until <see cref="Resolve"/> can bind their handles.
/// </summary>
public sealed class SevereHeadWounds
{
    public const string FileName = "head-wounds.meitou";
    public const string Header = "MEITOU-HEAD-WOUNDS 1";
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    readonly Dictionary<CharacterId, SevereHeadWoundState> live = [];
    readonly Dictionary<(int C, int I), SevereHeadWoundState> pending = [];
    readonly Dictionary<CharacterId, (int C, int I)> bindings = [];

    public IEnumerable<KeyValuePair<CharacterId, SevereHeadWoundState>> All => live;
    public int PendingCount => pending.Count;
    public SevereHeadWoundState? Of(CharacterId id) => live.TryGetValue(id, out var state) ? state : null;
    public bool Set(CharacterId id, SevereHeadWoundState state)
    {
        if (id.IsNone || !state.IsValid) return false;
        if (state is { HeavyHits: 0, Kind: SevereHeadWoundKind.None, Placement: null }) Remove(id);
        else live[id] = state;
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

    /// <summary>
    /// Snapshot this enumeration before background save writing. Capture mappings take priority over remembered or unresolved handles.
    /// Pass the captured save's handle-existence predicate to omit permanently deleted characters while retaining unloaded ones.
    /// Additional cold-state records supersede remembered/pending data; current live capture mappings remain authoritative.
    /// </summary>
    public IEnumerable<string> Write(Func<CharacterId, (int C, int I)?> handle, Func<(int C, int I), bool>? exists = null,
        IEnumerable<((int C, int I) Handle, SevereHeadWoundState State)>? additionalRecords = null)
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
            string line = string.Create(Culture, $"H {saved.C} {saved.I} {state.HeavyHits} {(int)state.Kind} {state.Profile}");
            if (state.Placement is { } p)
                line += string.Create(Culture, $" {p.Point.X:R} {p.Point.Y:R} {p.Point.Z:R} {p.Normal.X:R} {p.Normal.Y:R} {p.Normal.Z:R} {p.Tangent.X:R} {p.Tangent.Y:R} {p.Tangent.Z:R}");
            yield return line;
        }
    }

    /// <summary>
    /// Replaces this store from a supported sidecar. Missing/old generic-wound data and unknown versions produce no severe states.
    /// Malformed rows are rejected independently so one damaged record cannot lose other characters' wounds.
    /// </summary>
    public HeadWoundReadResult Read(IEnumerable<string> lines, Func<(int C, int I), CharacterId?> character, Func<(int C, int I), bool>? exists = null)
    {
        Clear();
        bool header = false;
        int rejected = 0;
        foreach (var line in lines)
        {
            string text = line.Trim().TrimStart('\uFEFF');
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (!header)
            {
                if (text != Header) return new(false, 0, 0, 0);
                header = true;
                continue;
            }
            if (!TryRow(text, out var handle, out var state)) { rejected++; continue; }
            if (exists?.Invoke(handle) == false) continue; // An import can intentionally leave saved NPCs out of the world.
            pending[handle] = state;
        }
        if (!header) return new(false, 0, 0, 0);
        int restored = Resolve(character);
        return new(true, restored, pending.Count, rejected);
    }

    static bool TryRow(string line, out (int C, int I) handle, out SevereHeadWoundState state)
    {
        handle = default;
        state = default;
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (6 or 15) || fields[0] != "H") return false;
        bool Integer(int i, out int value) => int.TryParse(fields[i], NumberStyles.Integer, Culture, out value);
        if (!Integer(1, out int container) || !Integer(2, out int instance) || container < 0 || instance < 0
            || !Integer(3, out int hits) || !Integer(4, out int kind) || !Integer(5, out int profile)) return false;
        HeadWoundPlacement? placement = null;
        if (fields.Length == 15)
        {
            Span<float> values = stackalloc float[9];
            for (int i = 0; i < values.Length; i++)
                if (!float.TryParse(fields[i + 6], NumberStyles.Float, Culture, out values[i]) || !float.IsFinite(values[i])) return false;
            placement = new(new(values[0], values[1], values[2]), new(values[3], values[4], values[5]), new(values[6], values[7], values[8]));
        }
        state = new(hits, (SevereHeadWoundKind)kind, profile, placement);
        if (!state.IsValid) return false;
        handle = (container, instance);
        return true;
    }
}