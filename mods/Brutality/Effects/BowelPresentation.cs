using Meitou.Game.Rendering.Frame;
using System.Collections.Immutable;
using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Ui;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Effects;

internal sealed record BowelAsset(Vertex[] Source, BowelSkinning Skinning, Vector3 HalfExtent);
internal sealed record BowelAssetSet(BowelAsset? Whole, BowelAsset[] Halves);
internal sealed record SavedBowelPiece(int Half, bool Whole, uint BloodColour, double Age, bool KeepAttached, float Depth, SavedBowelRig Rig)
{
    internal bool IsValid => Half is 0 or 1 && BloodColour <= 0xFFFFFF && double.IsFinite(Age) && Age is >= 0 and < BowelPresentation.Lifetime
        && float.IsFinite(Depth) && Depth is >= 0 and <= 100 && Rig is { IsValid: true, PinNode: -1 or 0 }
        && (!Whole || Half == 0 && !KeepAttached && Rig.PinNode < 0);
}

internal sealed record SavedBowelOwner(ImmutableArray<SavedBowelPiece> Pieces)
{
    // A spent spill cannot create another prop when its wound or cut is rendered after loading.
    internal static readonly SavedBowelOwner Spent = new([]);
    internal bool IsValid => !Pieces.IsDefault && Pieces.Length <= 2 && Pieces.All(piece => piece is { IsValid: true })
        && (Pieces.Length < 2 || !Pieces[0].Whole && !Pieces[1].Whole && Pieces[0].Half != Pieces[1].Half);
}

internal sealed record BowelRenderPiece(long Key, Vector3 Centre, Vertex[] Vertices, uint BloodColour, long Version)
{
    public bool Mechanical { get; init; }
    internal BowelSkinning? Skinning { get; init; }
    internal Matrix4x4[]? Pose { get; init; }
#if BRUTALITY_STOCK_MASTER
    public Vector4? Albedo { get; init; }
#endif

    internal Vector4 ColourOf(Vector4 source)
    {
        if (Mechanical) return source;
#if BRUTALITY_STOCK_MASTER
        if (Albedo is { } organ)
        {
            // The asset's cut faces store darkness relative to its exterior surface.
            float shade = Math.Clamp(source.X / BowelPresentation.OrganBaseRed, 0, 1);
            return new(organ.X * shade, organ.Y * shade, organ.Z * shade, source.W);
        }
#endif
        return StockTissueColour.Recolour(source, BloodColour);
    }
}

/// <summary>Two anatomical bowel sections feed from independently posed cut faces and retain the pin's attachment/sleep policy.</summary>
internal sealed class BowelPresentation(AnatomyLibrary anatomy, BowelAssetPreparation? preparation = null, bool gpuSkinning = false) : IDisposable
{
    public const float Lifetime = 600;
    internal const float OrganBaseRed = .46f;
    static readonly Vector4 baseColour = new(OrganBaseRed, .20f, .20f, 1);
    sealed class Piece
    {
        public required CharacterId Owner;
        public required int Half;
        public required long Key;
        public required BowelAsset Asset;
        public required Vertex[] Deformed;
        public required uint BloodColour;
#if BRUTALITY_STOCK_MASTER
        public required Vector4 Albedo;
#endif
        public required double Born;
        public bool Whole, KeepAttached, Broken, Physical;
        public bool InitialReport, MotionReport, SettledReport;
        public float Depth;
        public BowelPose? LastPose;
        public Matrix4x4[] LastTransforms = [];
        public long Version;
    }
    readonly BowelRagdollScene physics = new();
    readonly Dictionary<(CharacterId, int), Piece> pieces = [];
    readonly HashSet<CharacterId> spilled = [];
    readonly Queue<CharacterId> history = [];
    readonly BowelAssetPreparation prepared = preparation ?? BowelAssetPreparation.Shared(anatomy);
    readonly List<BowelRenderPiece> rendered = [];
    readonly List<BrutalityPartLabel> labels = [];
    static long nextKey;
    double clock;
    bool clockSet, disposed;
    public int Count => pieces.Count;
    public int Moving => physics.Moving;
    public int Attached => pieces.Values.Count(p => physics.IsAttached(p.Key));
    public IReadOnlyList<BowelRenderPiece> Rendered => rendered;
    public IReadOnlyList<BrutalityPartLabel> Labels => labels;
    public bool HasOwner(CharacterId owner) => pieces.ContainsKey((owner, 0)) || pieces.ContainsKey((owner, 1));
    public bool HasSpilled(CharacterId owner) => spilled.Contains(owner);

    public bool Begin(CharacterId owner, CharacterAppearance appearance, SkeletonRig rig, double born, double now,
        Func<int, CutFluidContact?> contacts, Vector2 travel, uint bloodColour, IRagdollEnvironment environment)
    {
        if (disposed || owner.IsNone || spilled.Contains(owner) || !double.IsFinite(born) || !double.IsFinite(now) || now - born >= Lifetime) return false;
        if (contacts(0) is null || contacts(1) is null) return false; // An unseen cut never creates an organ at an inferred world position.
        if (appearance.Race.GetBool("is robot")) return false;
        var source = Assets(rig)?.Halves;
        if (source is null || source.Length == 0) return false;
        while (pieces.Count + source.Length > BowelRagdollScene.Capacity)
        {
            var oldest = pieces.MinBy(p => p.Value.Born);
            Remove(oldest.Key);
        }
        bool keep = GutEmergence.KeepAttached(owner);
        bool spawned = false;
        for (int half = 0; half < source.Length; half++)
        {
            var contact = contacts(half)!.Value;
            var asset = source[half];
            var pin = asset.Skinning.Geometry.RestNodes[0];
            var tangent = new Vector3(travel.X, 0, travel.Y);
            tangent -= contact.Normal * Vector3.Dot(tangent, contact.Normal);
            if (tangent.LengthSquared() < .01f) tangent = Vector3.Cross(contact.Normal, MathF.Abs(contact.Normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            tangent = Vector3.Normalize(tangent);
            var rotation = Turn(-pin, Vector3.Normalize(contact.Normal * .35f + tangent));
            float depth = Math.Clamp(asset.HalfExtent.Length() * .3f, .4f, .8f);
            // Feed the whole rest mesh from inside, rather than merely putting one edge below the skin.
            depth = Math.Max(depth, asset.Source.Max(v => Vector3.Dot(Vector3.Transform(v.Position - pin, rotation), contact.Normal)) + .1f);
            float age = Math.Max(0, (float)(now - born));
            bool attached = GutEmergence.AttachedAt(keep, age);
            var position = GutEmergence.Anchor(contact, depth, age) - Vector3.Transform(pin, rotation);
            var velocity = age >= GutEmergence.FeedAge ? Vector3.Zero : tangent * .8f + contact.Normal * .35f;
            if (!attached)
            {
                rotation = RestingRotation(asset.Skinning.Geometry);
                float floor = environment.GroundHeight(contact.Position.X, contact.Position.Z, contact.Position.Y);
                position = contact.Position with { Y = float.IsFinite(floor) ? floor + asset.Skinning.Geometry.Radius : contact.Position.Y };
            }
            var piece = new Piece { Owner = owner, Half = half, Key = Interlocked.Increment(ref nextKey), Asset = asset, Deformed = DeformationBuffer(asset),
                BloodColour = bloodColour, Born = born, KeepAttached = keep, Depth = depth, Broken = !attached,
#if BRUTALITY_STOCK_MASTER
                Albedo = StockTissuePalette.For(appearance).Organ(5),
#endif
            };
            var spin = age < GutEmergence.FeedAge ? Vector3.Cross(contact.Normal, tangent) * .9f : Vector3.Zero;
            piece.Physical = physics.Add(piece.Key, asset.Skinning.Geometry, position, rotation, velocity, resting: !attached,
                pinNode: attached ? 0 : -1, preserveAttachment: keep, launchSpin: spin);
            if (!piece.Physical) continue;
            pieces.Add((owner, half), piece);
            spawned = true;
        }
        if (spawned) Remember(owner);
        return spawned;
    }

    /// <summary>The pin's ordinary deep-abdomen spill is one complete loose organ, limited to its two human races.</summary>
    public bool BeginWound(CharacterId owner, CharacterAppearance appearance, SkeletonRig rig, double born, double now,
        CutFluidContact contact, uint bloodColour, IRagdollEnvironment environment)
    {
        if (disposed || owner.IsNone || spilled.Contains(owner) || appearance.Race.GetBool("is robot")
            || appearance.Race.Name is not ("Greenlander" or "Scorchlander")
            || !double.IsFinite(born) || !double.IsFinite(now) || now < born || now - born >= Lifetime
            || !float.IsFinite(contact.Position.LengthSquared()) || !float.IsFinite(contact.Normal.LengthSquared())
            || contact.Normal.LengthSquared() < 1e-8f || Assets(rig)?.Whole is not { } asset) return false;
        while (pieces.Count >= BowelRagdollScene.Capacity) Remove(pieces.MinBy(p => p.Value.Born).Key);
        var normal = Vector3.Normalize(contact.Normal);
        var sourceNormal = Vector3.Cross(asset.Skinning.Geometry.RestNodes[1] - asset.Skinning.Geometry.RestNodes[0],
            asset.Skinning.Geometry.RestNodes[3] - asset.Skinning.Geometry.RestNodes[0]);
        var rotation = Turn(sourceNormal, normal);
        var position = contact.Position - normal * .1f;
        bool resting = now - born >= 1;
        if (resting)
        {
            rotation = RestingRotation(asset.Skinning.Geometry);
            float floor = environment.GroundHeight(contact.Position.X, contact.Position.Z, contact.Position.Y);
            float support = -asset.Source.Min(v => Vector3.Transform(v.Position, rotation).Y);
            position.Y = float.IsFinite(floor) ? floor + MathF.Max(asset.Skinning.Geometry.Radius, support) : position.Y;
        }
        var piece = new Piece { Owner = owner, Half = 0, Whole = true, Key = Interlocked.Increment(ref nextKey), Asset = asset,
            Deformed = DeformationBuffer(asset), BloodColour = bloodColour, Born = born, Broken = true,
#if BRUTALITY_STOCK_MASTER
            Albedo = StockTissuePalette.For(appearance).Organ(5),
#endif
        };
        var tumbleAxis = Vector3.Cross(normal, MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
        var tumble = Vector3.Normalize(tumbleAxis) * 2.6f;
        piece.Physical = physics.Add(piece.Key, asset.Skinning.Geometry, position, rotation,
            resting ? Vector3.Zero : normal * 1.5f, resting: resting, launchSpin: tumble);
        if (!piece.Physical) return false;
        pieces.Add((owner, 0), piece); Remember(owner); return true;
    }

    internal Dictionary<CharacterId, SavedBowelOwner> Capture(double now)
    {
        var saved = new Dictionary<CharacterId, SavedBowelOwner>(spilled.Count);
        foreach (var owner in spilled) saved.Add(owner, SavedBowelOwner.Spent);
        foreach (var piece in pieces.Values)
        {
            if (physics.Capture(piece.Key) is not { } rig) continue;
            var row = new SavedBowelPiece(piece.Half, piece.Whole, piece.BloodColour, Math.Max(0, now - piece.Born),
                piece.KeepAttached, piece.Depth, rig);
            if (row.IsValid)
                saved[piece.Owner] = new((saved.GetValueOrDefault(piece.Owner) ?? SavedBowelOwner.Spent).Pieces.Add(row));
        }
        return saved;
    }

    // A visible owner can arrive before the asynchronous mesh preparation finishes. The caller retries then.
    internal bool TryRestore(CharacterId owner, CharacterAppearance appearance, SkeletonRig rig, double now, SavedBowelOwner saved)
    {
        if (disposed || owner.IsNone || HasSpilled(owner) || HasOwner(owner) || !saved.IsValid || !double.IsFinite(now)) return true;
        if (saved.Pieces.IsEmpty || appearance.Race.GetBool("is robot"))
        {
            Remember(owner);
            return true;
        }
        if (Assets(rig) is not { } assets) return false;
        while (pieces.Count + saved.Pieces.Length > BowelRagdollScene.Capacity)
            Remove(pieces.MinBy(pair => pair.Value.Born).Key);
        foreach (var row in saved.Pieces)
        {
            var asset = row.Whole ? assets.Whole : row.Half < assets.Halves.Length ? assets.Halves[row.Half] : null;
            if (asset is null) continue;
            long key = Interlocked.Increment(ref nextKey);
            if (!physics.Restore(key, asset.Skinning.Geometry, row.Rig, row.KeepAttached)) continue;
            pieces.Add((owner, row.Half), new Piece { Owner = owner, Half = row.Half, Whole = row.Whole, Key = key, Asset = asset,
                Deformed = DeformationBuffer(asset), BloodColour = row.BloodColour, Born = now - row.Age, Physical = true,
                KeepAttached = row.KeepAttached, Broken = row.Rig.PinNode < 0, Depth = row.Depth,
#if BRUTALITY_STOCK_MASTER
                Albedo = StockTissuePalette.For(appearance).Organ(5),
#endif
            });
        }
        Remember(owner);
        return true;
    }

    void Remember(CharacterId owner)
    {
        if (history.Count >= 128) spilled.Remove(history.Dequeue());
        spilled.Add(owner); history.Enqueue(owner);
    }

    internal static void Prepare(AnatomyLibrary anatomy, SkeletonRig rig) => BowelAssetPreparation.Shared(anatomy).TryGet(Scale(rig), out _);
    BowelAssetSet? Assets(SkeletonRig rig) => prepared.TryGet(Scale(rig), out var source) ? source : null;
    static float Scale(SkeletonRig rig)
    {
        float scale = rig.Handle("Bip01 Pelvis") is { } hip && rig.Handle("Bip01 Neck") is { } neck
            ? Vector3.Distance(rig.BindPosition[hip], rig.BindPosition[neck]) / 6.1649f : 1;
        if (!float.IsFinite(scale) || scale is < .5f or > 2) scale = 1;
        return scale;
    }

    public void Advance(double now, Func<CharacterId, int, CutFluidContact?> contacts, Func<CharacterId, bool> validOwner,
        IRagdollEnvironment environment, long tick, float physicsSpeed = 1)
    {
        if (disposed || !double.IsFinite(now)) return;
        float dt = clockSet ? (float)Math.Clamp(now - clock, 0, .5) : 0;
        clock = now; clockSet = true;
        foreach (var key in pieces.Where(p => now - p.Value.Born >= Lifetime || !validOwner(p.Key.Item1)).Select(p => p.Key).ToArray()) Remove(key);
        foreach (var piece in pieces.Values)
        {
            float age = Math.Max(0, (float)(now - piece.Born));
            if (!GutEmergence.AttachedAt(piece.KeepAttached, age)) { physics.ReleaseAttachment(piece.Key); piece.Broken = true; }
            if (piece.Broken || contacts(piece.Owner, piece.Half) is not { } contact) continue;
            if (!physics.MoveAttachment(piece.Key, GutEmergence.Anchor(contact, piece.Depth, age))) piece.Broken = true;
        }
        physics.Step(dt / MathF.Max(1, physicsSpeed), environment, tick);
        rendered.Clear(); labels.Clear();
        foreach (var piece in pieces.Values)
        {
            if (physics.PoseOf(piece.Key) is not { } pose) continue;
            if (!gpuSkinning && !ReferenceEquals(piece.LastPose, pose))
            {
                if (!piece.LastTransforms.AsSpan().SequenceEqual(pose.Transforms))
                {
                    if (!piece.Asset.Skinning.Deform(pose.Transforms, piece.Deformed)) continue;
                    piece.LastTransforms = (Matrix4x4[])pose.Transforms.Clone(); piece.Version++;
                }
                piece.LastPose = pose;
            }
            double age = now - piece.Born;
            if (BowelDiagnostics.Trace && (!piece.InitialReport || !piece.MotionReport && age >= .2 || !piece.SettledReport && age >= 3))
            {
                if (gpuSkinning && !piece.Asset.Skinning.Deform(pose.Transforms, piece.Deformed)) continue;
                BowelDiagnostics.Piece(piece.Owner, piece.Half, age, physics.IsAttached(piece.Key), piece.Asset, piece.Deformed, pose);
                piece.InitialReport = true;
                piece.MotionReport |= age >= .2;
                piece.SettledReport |= age >= 3;
            }
            rendered.Add(new(piece.Key, pose.Centre, gpuSkinning ? piece.Asset.Source : piece.Deformed, piece.BloodColour, piece.Version)
            {
                Skinning = gpuSkinning ? piece.Asset.Skinning : null,
                Pose = gpuSkinning ? pose.Transforms : null,
#if BRUTALITY_STOCK_MASTER
                Albedo = piece.Albedo,
#endif
            });
            labels.Add(new(piece.Owner, 100 + piece.Half, "Intestines", pose.Centre));
        }
    }

    internal static BowelAssetSet BuildBowelAssets(AnatomyLibrary anatomy, float scale)
    {
        var source = anatomy.Get(AnatomyKind.Body).Where(batch => batch.Layer == 4).SelectMany(batch => batch.Vertices.Chunk(3))
            .Where(triangle => triangle.Length == 3 && triangle.All(v => (int)MathF.Floor(v.Uv.X / 10) == 5)).SelectMany(v => v).ToArray();
        if (source.Length == 0) return new(null, []);
        var (min, max) = StockTissueColour.Bounds(source.Select(v => v.Position));
        var centre = (min + max) * .5f;
        var vertices = source.Select(v => new Vertex { Position = (v.Position - centre) * scale, Normal = v.Normal, Uv = v.Uv,
            Tangent = new(1, 0, 0, 1), Colour = baseColour, Weights = Vector4.UnitX }).ToArray();
        var size = max - min;
        int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        var normal = Vector3.Zero; normal[axis] = 1;
        var skeleton = new OgreSkeleton(); skeleton.Bones.Add(new() { Name = "Bip01", Handle = 0 });
        var rig = new SkeletonRig(skeleton);
        var mesh = MeshCut.FromTriangles(vertices);
        var result = new List<BowelAsset>();
        foreach (int side in new[] { 1, -1 })
        {
            var clipped = MeshCut.Clip(mesh, rig, [new(new(normal, 0), side)], capColour: baseColour);
            var cut = Model.Build(clipped.Skin, 1).Parts.SelectMany(p => p.Vertices).Concat(clipped.Wound is { } cap
                ? Model.Build(cap, 1).Parts.SelectMany(p => p.Vertices) : []).ToArray();
            if (cut.Length == 0) continue;
            var (a, b) = StockTissueColour.Bounds(cut.Select(v => v.Position));
            var middle = (a + b) * .5f;
            cut = cut.Select(v => v with { Position = v.Position - middle }).ToArray();
            result.Add(new(cut, new(cut), (b - a) * .5f));
        }
        var assets = new BowelAssetSet(new(vertices, new(vertices), (max - min) * (.5f * scale)), result.ToArray());
        if (BowelDiagnostics.Trace) BowelDiagnostics.Assets(scale, assets);
        return assets;
    }

    Vertex[] DeformationBuffer(BowelAsset asset) => gpuSkinning && !BowelDiagnostics.Trace ? [] : new Vertex[asset.Source.Length];

    void Remove((CharacterId, int) key) { if (pieces.Remove(key, out var piece)) physics.Remove(piece.Key); }
    static Quaternion Turn(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from); to = Vector3.Normalize(to); float dot = Vector3.Dot(from, to);
        if (dot > .9999f) return Quaternion.Identity;
        if (dot < -.9999f)
        {
            var axis = Vector3.Cross(from, Vector3.UnitX); if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(from, Vector3.UnitZ);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }
    static Quaternion RestingRotation(BowelRigGeometry geometry) => Turn(Vector3.Cross(geometry.RestNodes[1] - geometry.RestNodes[0], geometry.RestNodes[3] - geometry.RestNodes[0]), Vector3.UnitY);
    public void Clear() { physics.Clear(); pieces.Clear(); spilled.Clear(); history.Clear(); rendered.Clear(); labels.Clear(); clockSet = false; }
    public void Dispose() { if (disposed) return; Clear(); physics.Dispose(); preparation?.Dispose(); disposed = true; }
}

#if !BRUTALITY_STOCK_MASTER
/// <summary>Immutable render-published organ labels; this system never steps cosmetic physics on the simulation thread.</summary>
internal sealed class BowelLabelSystem : ITickSystem, IBrutalityPartLabels, IDisposable
{
    sealed record Snapshot(long Tick, IReadOnlyList<BrutalityPartLabel> Labels);
    volatile Snapshot latest = new(-1, []);
    World? world;
    public void Schedule(World value) => world = value;
    public void Publish(long tick, IReadOnlyList<BrutalityPartLabel> labels) => latest = new(tick, Array.AsReadOnly(labels.ToArray()));
    public IReadOnlyList<BrutalityPartLabel> LabelsAt(long tick)
    {
        var snapshot = latest;
        return snapshot.Tick >= 0 && snapshot.Tick <= tick && tick - snapshot.Tick <= 2 ? snapshot.Labels : [];
    }
    public void Dispose() { if (world is not null) EffectsBridge.Release(world); }
}

#endif
