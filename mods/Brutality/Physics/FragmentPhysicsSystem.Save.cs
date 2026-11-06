using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality.Physics;

internal sealed record SavedFragmentOwner(string Key, string Mesh, string Cut, Vector3 Anchor, SavedFragmentPiece[] Pieces);
internal sealed record LegacyFragmentBody(bool Frozen, float Age, RigidTransform[] Pose)
{
    public int[]? Anchors { get; init; }
    public RigidTransform[]? Offsets { get; init; }
}
internal sealed record LegacyFragmentOwner(string Key, Vector3 Anchor, TorsoBisectionState Cut, LegacyFragmentBody Positive, LegacyFragmentBody Negative);
internal sealed record FragmentSavePayload(int Version, SavedFragmentOwner[] Owners, LegacyFragmentOwner[] Legacy);

internal static class FragmentSaveCodec
{
    internal const int MaximumBytes = 16 * 1024 * 1024, MaximumOwners = 4096, MaximumBodies = 32768;
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    internal static bool Key(string key)
    {
        if (key is not { Length: > 0 and <= 256 }) return false;
        var fields = key.Split(':');
        if (fields.Length != 7 || fields[0] != "hand" || fields[1] != "v1") return false;
        Span<int> values = stackalloc int[5];
        for (int i = 0; i < values.Length; i++)
            if (!int.TryParse(fields[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return false;
        return SaveIdentityAdapter.KeyFor(new(values[0], values[1], values[2], values[3], values[4])) == key;
    }
    static bool Point(Vector3 p) => float.IsFinite(p.LengthSquared()) && MathF.Abs(p.X) <= 10_000_000 && MathF.Abs(p.Y) <= 10_000_000 && MathF.Abs(p.Z) <= 10_000_000;
    internal static bool Valid(SavedFragmentOwner? owner) => owner is { Pieces: not null } && Key(owner.Key)
        && owner.Mesh is { Length: > 0 and <= 1024 } && owner.Cut is { Length: 16 } && Point(owner.Anchor)
        && owner.Pieces.Length is > 0 and <= 8 && owner.Pieces.All(AdvancedFragmentScene.Valid)
        && owner.Pieces.Select(piece => piece.Key).Distinct().Count() == owner.Pieces.Length;
    internal static bool Valid(LegacyFragmentOwner? owner) => owner is not null && Key(owner.Key) && Point(owner.Anchor) && owner.Cut.IsValid
        && Body(owner.Positive) && Body(owner.Negative);
    static bool Body(LegacyFragmentBody? body) => body is { Pose: not null } && body.Pose.Length is > 0 and <= 128
        && float.IsFinite(body.Age) && body.Age is >= 0 and <= 1_000_000_000
        && body.Pose.All(p => Point(p.Position) && float.IsFinite(p.Rotation.LengthSquared()) && MathF.Abs(p.Rotation.LengthSquared() - 1) <= .01f)
        && (body.Anchors is null && body.Offsets is null || body.Anchors is { Length: > 0 and <= 1024 } anchors
            && body.Offsets is { } offsets && anchors.Length == offsets.Length && anchors.All(actor => actor >= 0 && actor < body.Pose.Length)
            && offsets.All(p => Point(p.Position) && float.IsFinite(p.Rotation.LengthSquared()) && MathF.Abs(p.Rotation.LengthSquared() - 1) <= .01f));
    internal static FragmentSavePayload Read(Stream stream)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            int count = stream.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("Brutality fragment save exceeds its size budget.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        var saved = JsonSerializer.Deserialize<FragmentSavePayload>(bytes, Json) ?? throw new InvalidDataException("Empty fragment save.");
        if (saved.Version != 1) throw new InvalidDataException($"Unsupported fragment save version {saved.Version}.");
        if (saved.Owners is null || saved.Legacy is null || saved.Owners.Length + saved.Legacy.Length > MaximumOwners)
            throw new InvalidDataException("Invalid fragment save entry count.");
        var owners = saved.Owners.Where(Valid).GroupBy(owner => owner.Key, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        var legacy = saved.Legacy.Where(Valid).GroupBy(owner => owner.Key, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        if (owners.Sum(owner => owner.Pieces.Sum(piece => piece.Pose.Length)) + legacy.Sum(owner => owner.Positive.Pose.Length + owner.Negative.Pose.Length) > MaximumBodies)
            throw new InvalidDataException("Brutality fragment save exceeds its body budget.");
        return new(1, owners, legacy);
    }
    internal static void Write(Stream stream, IEnumerable<SavedFragmentOwner> owners, IEnumerable<LegacyFragmentOwner> legacy)
    {
        var payload = new FragmentSavePayload(1, owners.Where(Valid).OrderBy(owner => owner.Key, StringComparer.Ordinal).ToArray(), legacy.Where(Valid).OrderBy(owner => owner.Key, StringComparer.Ordinal).ToArray());
        using var bytes = new MemoryStream(); JsonSerializer.Serialize(bytes, payload, Json);
        if (payload.Owners.Length + payload.Legacy.Length > MaximumOwners || payload.Owners.Sum(owner => owner.Pieces.Sum(piece => piece.Pose.Length))
            + payload.Legacy.Sum(owner => owner.Positive.Pose.Length + owner.Negative.Pose.Length) > MaximumBodies || bytes.Length > MaximumBytes)
            throw new InvalidDataException("Brutality fragment save exceeds its budget.");
        bytes.Position = 0; bytes.CopyTo(stream);
    }
}

public sealed partial class FragmentPhysicsSystem : IModSaveData
{
    sealed class SaveTrack
    {
        public Owner? Owner;
        public SavedFragmentOwner? Offscreen;
        public Vector3 Anchor;
        public string? Key;
        public int RestoredRevision = -1;
        public string Attempt = "";
    }
    sealed record PendingOwner(SavedFragmentOwner Value, ulong Hash);
    sealed record PendingLegacy(LegacyFragmentOwner Value, ulong Hash);
    readonly ConditionalWeakTable<CharacterCold, SaveTrack> saveTracks = new();
    readonly Dictionary<string, PendingOwner> savedOwners = new(StringComparer.Ordinal);
    readonly Dictionary<string, PendingLegacy> legacyOwners = new(StringComparer.Ordinal);
    World? saveWorld;
    int saveRevision;
    bool savedDataRead, unreadableSave;

    public void BindSaveWorld(World world)
    {
        if (saveWorld is not null && !ReferenceEquals(saveWorld, world)) throw new InvalidOperationException("A fragment scene belongs to one world.");
        saveWorld = world;
    }
    internal void ImportLegacySave(World world, SaveGame save)
    {
        BindSaveWorld(world);
        if (savedDataRead) return;
        foreach (var character in save.Characters)
            if (LegacyPose(character) is { } legacy) Remember(legacy);
    }
    internal void CapturedSaveIdentity(CharacterCold cold)
    {
        if (gameplay.SaveKeyOf(cold) is not { } key) return;
        saveTracks.TryGetValue(cold, out var track);
        string? old = track?.Key ?? (cold.Save is { } source ? SaveIdentityAdapter.KeyFor(source.Source.Handle) : null);
        if (old is not null && old != key)
        {
            if (legacyOwners.Remove(old, out var legacy)) Remember(legacy.Value with { Key = key });
            if (savedOwners.Remove(old, out var saved)) Remember(saved.Value with { Key = key });
        }
        track ??= saveTracks.GetValue(cold, static _ => new());
        track.Key = key;
        if (track.Offscreen is { } snapshot)
        { track.Offscreen = snapshot with { Key = key }; Remember(track.Offscreen); }
    }
    void RestoreSavedOwner(World world, CharacterId id, Owner owner, BrutalityActorView state, Vector3 anchor)
    {
        BindSaveWorld(world);
        var track = saveTracks.GetValue(owner.Cold, static _ => new());
        bool newOwner = !ReferenceEquals(track.Owner, owner);
        if (newOwner) owner.NegativePoseRestored = false;
        track.Owner = owner; track.Anchor = anchor; track.Key = gameplay.SaveKeyOf(owner.Cold);
        string cut = owner.Topology.Get(state).Cut;
        if (!newOwner && track.RestoredRevision == saveRevision && track.Attempt == cut) return;
        track.RestoredRevision = saveRevision; track.Attempt = cut;
        if (track.Key is not { } key) return;
        bool positiveRestored = false;
        if (savedOwners.TryGetValue(key, out var pending) && pending.Value is { } saved && saved.Cut == cut
            && string.Equals(saved.Mesh, owner.Appearance.BodyMesh, StringComparison.OrdinalIgnoreCase))
        {
            var delta = anchor - saved.Anchor;
            foreach (var piece in saved.Pieces)
                if (owner.Pieces.TryGetValue(piece.Key, out var current) && piece.Stamp == PieceKey(cut, piece.Key)
                    && scene.Restore(current.Piece, piece, delta))
                { if (piece.Key == 1) owner.NegativePoseRestored = true; if (piece.Key == 0) positiveRestored = true; }
            savedOwners.Remove(key); legacyOwners.Remove(key); track.Offscreen = null;
            reactionOwners.Add(id);
        }
        else if (legacyOwners.TryGetValue(key, out var old) && state.Torso == old.Value.Cut)
        {
            var delta = owner.Cold.CarriedBy.IsNone ? anchor - old.Value.Anchor : Vector3.Zero;
            foreach (var (pieceKey, body) in new[] { (0, old.Value.Positive), (1, old.Value.Negative) })
                if (owner.Pieces.TryGetValue(pieceKey, out var entry) && entry.Piece.Definition.Bodies.Length == body.Pose.Length)
                {
                    var basis = scene.Capture(entry.Piece, pieceKey, PieceKey(cut, pieceKey), owner.Setup.Rig);
                    bool restored = scene.Restore(entry.Piece, basis with { Pose = body.Pose.ToArray(), Frozen = body.Frozen, Age = body.Age,
                        Linear = new Vector3[body.Pose.Length], Angular = new Vector3[body.Pose.Length], Carried = false, Dynamic = Enumerable.Repeat(true, body.Pose.Length).ToArray(),
                        Anchors = body.Anchors?.Length == entry.Piece.Definition.BodyOfBone.Length ? body.Anchors : basis.Anchors,
                        Offsets = body.Anchors?.Length == entry.Piece.Definition.BodyOfBone.Length ? body.Offsets : basis.Offsets }, delta);
                    if (restored && pieceKey == 1) owner.NegativePoseRestored = true;
                    if (restored && pieceKey == 0) positiveRestored = true;
                }
            legacyOwners.Remove(key);
        }
        if (CorpseSaveAdapter.TryTakePositive(owner.Cold, out var placement, out bool released) && (!positiveRestored || released)
            && owner.Pieces.TryGetValue(0, out var positive) && world.Characters.TryResolveNext(id, out int slot))
        {
            float yaw = world.Characters.Next[slot].Yaw;
            RestoreLogicalPlacement(owner, positive.Piece, 0, released ? placement with { Yaw = yaw } : placement, yaw, released);
        }
    }
    void TrackSavedPosition(Owner owner, Vector3 anchor)
    {
        if (saveTracks.TryGetValue(owner.Cold, out var track)) track.Anchor = anchor;
    }
    void RememberRemovedOwner(CharacterId id, Owner owner)
    {
        var track = saveTracks.GetValue(owner.Cold, static _ => new());
        track.Offscreen = Snapshot(owner, gameplay.ActorViews.FirstOrDefault(view => view.Character == id) ?? Latest?.Characters.GetValueOrDefault(id)?.State, track.Anchor, gameplay.SaveKeyOf(owner.Cold) ?? "");
        track.Owner = null;
        if (track.Offscreen is { } saved && FragmentSaveCodec.Valid(saved)) { track.Key = saved.Key; Remember(saved); }
    }
    SavedFragmentOwner? Snapshot(Owner owner, BrutalityActorView? state, Vector3 anchor, string key)
    {
        if (state is null || owner.Pieces.Count == 0) return null;
        string cut = owner.Topology.Get(state).Cut;
        return new(key, owner.Appearance.BodyMesh, cut, anchor, owner.Pieces.OrderBy(pair => pair.Key)
            .Select(pair => scene.Capture(pair.Value.Piece, pair.Key, PieceKey(cut, pair.Key), owner.Setup.Rig)).ToArray());
    }
    static string PieceKey(string cut, int piece) => cut + ":" + piece.ToString(CultureInfo.InvariantCulture);
    internal static string CutKey(BrutalityActorView state)
    {
        var hash = new StateHasher();
        void Plane(Vector4 plane) { hash.Add(plane.X); hash.Add(plane.Y); hash.Add(plane.Z); hash.Add(plane.W); }
        hash.Add(state.Head is not null); if (state.Head is { } head) { Plane(head.Plane); hash.Add((int)head.Mode); }
        hash.Add(state.Torso is not null); if (state.Torso is { } torso) { Plane(torso.Plane); hash.Add(torso.FullBody); }
        hash.Add(state.Limbs.Count); foreach (var limb in state.Limbs.OrderBy(limb => limb.LimbSlot)) { hash.Add(limb.LimbSlot); hash.Add(limb.Fraction); hash.Add(limb.Slant); }
        return hash.Value.ToString("x16", CultureInfo.InvariantCulture);
    }
    void Remember(SavedFragmentOwner owner) => savedOwners[owner.Key] = new(owner, Rng.StableHash(JsonSerializer.Serialize(owner, FragmentSaveCodec.Json)));
    void Remember(LegacyFragmentOwner owner) => legacyOwners[owner.Key] = new(owner, Rng.StableHash(JsonSerializer.Serialize(owner, FragmentSaveCodec.Json)));

    public void Write(Stream stream)
    {
        if (unreadableSave) throw new InvalidDataException("The previous fragment save could not be read; preserve its original sidecar.");
        if (saveWorld is not { } world) throw new InvalidOperationException("Bind the fragment scene before saving.");
        var result = savedOwners.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
        var states = gameplay.ActorViews.ToDictionary(view => view.Character);
        foreach (var (id, owner) in owners)
            if (gameplay.SaveKeyOf(owner.Cold) is { } key && states.TryGetValue(id, out var state))
            {
                var track = saveTracks.GetValue(owner.Cold, static _ => new());
                Vector3 anchor = world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Next[slot].Position : track.Anchor;
                if (Snapshot(owner, state, anchor, key) is { } saved) result[key] = saved;
            }
        FragmentSaveCodec.Write(stream, result.Values, legacyOwners.Where(pair => !result.ContainsKey(pair.Key)).Select(pair => pair.Value.Value));
        savedDataRead = true;
    }
    public void Read(Stream stream)
    {
        FragmentSavePayload saved;
        try { saved = FragmentSaveCodec.Read(stream); }
        catch { unreadableSave = true; throw; }
        savedOwners.Clear(); legacyOwners.Clear();
        foreach (var owner in saved.Owners) Remember(owner);
        foreach (var owner in saved.Legacy) Remember(owner);
        savedDataRead = true; unreadableSave = false; saveRevision++;
    }
    bool HasSavedOwner(World world, CharacterId id) => world.Characters.TryResolveNext(id, out int slot)
        && world.Characters.Cold(slot) is { } cold && gameplay.SaveKeyOf(cold) is { } key
        && (savedOwners.ContainsKey(key) || legacyOwners.ContainsKey(key));
    void ClearSaved()
    {
        savedOwners.Clear(); legacyOwners.Clear(); saveTracks.Clear(); saveWorld = null;
    }
    void HashSaved(ref StateHasher hash)
    {
        hash.Add(savedDataRead); hash.Add(unreadableSave); hash.Add(savedOwners.Count); hash.Add(legacyOwners.Count);
        foreach (var row in savedOwners.OrderBy(pair => pair.Key, StringComparer.Ordinal)) hash.Add(row.Value.Hash);
        foreach (var row in legacyOwners.OrderBy(pair => pair.Key, StringComparer.Ordinal)) hash.Add(row.Value.Hash);
    }
    static LegacyFragmentOwner? LegacyPose(SaveCharacter source)
    {
        if (SaveIdentityAdapter.KeyFor(source.Handle) is not { } key || source.GameState?.Strings.GetValueOrDefault("meitou ragdoll") is not { Length: < 262144 } text
            || !text.StartsWith("T2 ", StringComparison.Ordinal)) return null;
        var parts = text.Split('|', StringSplitOptions.TrimEntries); if (parts.Length != 3) return null;
        var head = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length != 6 || head[5] is not ("0" or "1") || !Number(head[1], out float x) || !Number(head[2], out float y) || !Number(head[3], out float z) || !Number(head[4], out float w)) return null;
        var cut = new TorsoBisectionState(new(x, y, z, w), head[5] == "1");
        if (!cut.IsValid || LegacyBody(parts[1]) is not { } positive || LegacyBody(parts[2]) is not { } negative) return null;
        if (source.GameState.Strings.GetValueOrDefault("meitou torso anchors") is { Length: < 262144 } anchorText)
        {
            var sections = anchorText.Split('|', StringSplitOptions.TrimEntries);
            if (sections.Length == 3 && sections[0] == "A1")
            { positive = LegacyAnchors(sections[1], positive); negative = LegacyAnchors(sections[2], negative); }
        }
        var saved = new LegacyFragmentOwner(key, source.Position, cut, positive, negative);
        return FragmentSaveCodec.Valid(saved) ? saved : null;
    }
    static LegacyFragmentBody LegacyAnchors(string text, LegacyFragmentBody body)
    {
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            || count is <= 0 or > 1024 || fields.Length != 1 + count * 8) return body;
        var anchors = new int[count]; var offsets = new RigidTransform[count]; Span<float> values = stackalloc float[7];
        for (int bone = 0; bone < count; bone++)
        {
            int start = 1 + bone * 8;
            if (!int.TryParse(fields[start], NumberStyles.Integer, CultureInfo.InvariantCulture, out int actor) || actor < 0 || actor >= body.Pose.Length) return body;
            for (int n = 0; n < values.Length; n++) if (!Number(fields[start + n + 1], out values[n])) return body;
            var rotation = new Quaternion(values[3], values[4], values[5], values[6]);
            if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < 1e-12f) return body;
            anchors[bone] = actor; offsets[bone] = new(new(values[0], values[1], values[2]), Quaternion.Normalize(rotation));
        }
        var candidate = body with { Anchors = anchors, Offsets = offsets };
        return offsets.All(p => float.IsFinite(p.Position.LengthSquared()) && MathF.Abs(p.Position.X) <= 10_000_000
            && MathF.Abs(p.Position.Y) <= 10_000_000 && MathF.Abs(p.Position.Z) <= 10_000_000) ? candidate : body;
    }
    static LegacyFragmentBody? LegacyBody(string text)
    {
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3 || fields[0] is not ("0" or "1") || !Number(fields[1], out float age)
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count is <= 0 or > 128 || fields.Length != 3 + 7 * count) return null;
        var poses = new RigidTransform[count]; Span<float> values = stackalloc float[7];
        for (int i = 0; i < count; i++)
        {
            for (int k = 0; k < 7; k++) if (!Number(fields[3 + i * 7 + k], out values[k])) return null;
            var rotation = new Quaternion(values[3], values[4], values[5], values[6]);
            if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < 1e-12f) return null;
            poses[i] = new(new(values[0], values[1], values[2]), Quaternion.Normalize(rotation));
        }
        return new(fields[0] == "1", age, poses);
    }
    static bool Number(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
