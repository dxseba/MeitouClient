using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Fits the bundled skull and brain to the morphed head before the fracture planes are cut.</summary>
internal static partial class StockHeadAnatomy
{
    static readonly object cacheLock = new();
    static AssetCache? cached;

    sealed record Source(IReadOnlyList<AnatomyBatch> Batches, Vector3 Centre, Vector3 Size, int VertexCount);

    sealed class AssetCache(string folder)
    {
        public string Folder { get; } = folder;
        public readonly Lazy<Source?> Skull = new(() => Read(folder, skull: true));
        public readonly Lazy<Source?> Brain = new(() => Read(folder, skull: false));
    }

    internal static (OgreMesh? Skull, OgreMesh? Brain) Load(string modFolder, SkeletonRig rig, OgreMesh body,
        CharacterAppearance look, string skeleton, Vector3 headMin, Vector3 headMax, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (rig.Handle("Bip01 Head") is not { } headBone || (uint)headBone >= Math.Min(rig.BoneCount, 256))
            return default;

        var size = headMax - headMin;
        if (!Finite(headMin) || !Finite(headMax) || !UsableSize(size)) return default;

        string folder = Path.GetFullPath(modFolder);
        AssetCache cache;
        lock (cacheLock)
        {
            if (cached is null || !string.Equals(cached.Folder, folder, StringComparison.Ordinal))
                cached = new(folder);
            cache = cached;
        }

        int jawBone = rig.Handle("Bip01 Jaw") ?? headBone;
        if ((uint)jawBone >= Math.Min(rig.BoneCount, 256)) jawBone = headBone;
        var centre = headMin + size * .5f;
        var margin = Vector3.Min(new Vector3(.12f), size * .06f);
        var palette = StockTissuePalette.For(look);
        var envelope = StockCranialEnvelope.Build(body, rig, look, headMin, headMax);
        var skull = palette.Hiver ? HiverCranium(envelope, headMin, size, skeleton, headBone, palette)
            : Fit(cache.Skull.Value, skeleton, centre, size - margin * 2, headBone, jawBone,
                palette.Bone, envelope, .015f, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var brainCentre = centre with { Y = headMin.Y + size.Y * .68f };
        var brain = Fit(cache.Brain.Value, skeleton, brainCentre, size * new Vector3(.72f, .48f, .72f), headBone, headBone,
            palette.Brain, envelope, .085f, cancellation);
        return (skull, brain);
    }

    static Source? Read(string folder, bool skull)
    {
        try
        {
            var assets = new AnatomyAssets(folder);
            string? path = skull ? assets.Ecorche : assets.Brain;
            if (path is null) return null;
            using var stream = File.OpenRead(path);
            var batches = skull ? AnatomyFile.ReadSkull(stream)
                : AnatomyFile.Read(stream).Where(batch => batch.Layer == 6).ToArray();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            int count = 0;
            foreach (var batch in batches)
            {
                count += batch.Vertices.Length;
                foreach (var vertex in batch.Vertices)
                {
                    min = Vector3.Min(min, vertex.Position);
                    max = Vector3.Max(max, vertex.Position);
                }
            }
            var size = max - min;
            return count >= 3 && UsableSize(size) ? new(batches, min + size * .5f, size, count) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static OgreMesh? Fit(Source? source, string skeleton, Vector3 centre, Vector3 size,
        int headBone, int jawBone, Vector4 colour, StockCranialEnvelope envelope, float clearance, CancellationToken cancellation)
    {
        if (source is null) return null;
        var scale = size / source.Size;
        if (!UsableSize(scale)) return null;
        var vertices = new List<Vertex>(source.VertexCount);
        var fitted = new Dictionary<Vector3, Vector3>();
        foreach (var batch in source.Batches)
        {
            uint bone = (uint)(batch.Bone == "Bip01 Jaw" ? jawBone : headBone);
            for (int i = 0; i < batch.Vertices.Length; i++)
            {
                if ((i & 2047) == 0) cancellation.ThrowIfCancellationRequested();
                var vertex = batch.Vertices[i];
                if (!fitted.TryGetValue(vertex.Position, out var position))
                {
                    position = envelope.Contain(centre + (vertex.Position - source.Centre) * scale, clearance);
                    fitted.Add(vertex.Position, position);
                }
                var normal = vertex.Normal / scale;
                float length = normal.LengthSquared();
                vertices.Add(new Vertex
                {
                    Position = position,
                    Normal = float.IsFinite(length) && length > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY,
                    Uv = vertex.Uv,
                    Colour = colour,
                    Bones = bone,
                    Weights = Vector4.UnitX,
                });
            }
        }
        cancellation.ThrowIfCancellationRequested();
        var mesh = MeshCut.FromTriangles(Simplify(vertices, size, clearance > .05f ? 6500 : 8000), skeleton);
        OgreMeshWeld.Apply(mesh);
        return mesh;
    }

    static bool UsableSize(Vector3 size) => Finite(size) && size.X > 1e-5f && size.Y > 1e-5f && size.Z > 1e-5f;

    static bool Finite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
