using System.Numerics;
using System.Text;

namespace Meitou.Brutality.Anatomy;

/// <summary>An anatomy vertex in the Ecorche file's bind space.</summary>
public readonly record struct AnatomyVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

/// <summary>Triangles that follow one bone rigidly. Layer: bone 0, muscle 1, tendon 2, fat 3, organ 4.</summary>
public sealed record AnatomyBatch(string Bone, int Layer, AnatomyVertex[] Vertices);

/// <summary>
/// Reads Kenshi Gore Lab's <c>Ecorche.anatomy</c> (magic <c>ECR1</c>; per batch: name length, layer, vertex count, name, then 8 floats a vertex). The user installs the data locally; its redistribution license is recorded separately from this reader.
/// </summary>
public static class AnatomyFile
{
    const int MaxBatches = 90, MaxNameLength = 80, MaxLayer = 6, MaxBatchVertices = 200000, MaxVertices = 1500000;

    public static IReadOnlyList<AnatomyBatch> Read(Stream stream) => Read(stream, skullOnly: false);

    internal static IReadOnlyList<AnatomyBatch> ReadSkull(Stream stream) => Read(stream, skullOnly: true);

    static IReadOnlyList<AnatomyBatch> Read(Stream stream, bool skullOnly)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        byte[] magic = reader.ReadBytes(4);
        if (magic.Length != 4) throw new EndOfStreamException("Truncated anatomy header");
        if (Encoding.ASCII.GetString(magic) != "ECR1") throw new InvalidDataException("Not an Ecorche anatomy file");
        int batches = reader.ReadInt32();
        if (batches is <= 0 or > MaxBatches) throw new InvalidDataException("Invalid anatomy batch count");
        var result = new List<AnatomyBatch>(batches);
        int total = 0;
        for (int b = 0; b < batches; b++)
        {
            int length = reader.ReadInt32(), layer = reader.ReadInt32(), count = reader.ReadInt32();
            if (length is <= 0 or > MaxNameLength || layer is < 0 or > MaxLayer || count is <= 0 or > MaxBatchVertices || count % 3 != 0) throw new InvalidDataException("Invalid anatomy batch");
            if (total > MaxVertices - count) throw new InvalidDataException("Anatomy budget exceeded");
            byte[] name = reader.ReadBytes(length);
            if (name.Length != length) throw new EndOfStreamException("Truncated anatomy bone name");
            if (name.Any(c => c is < 32 or > 126)) throw new InvalidDataException("Invalid anatomy bone name");
            string bone = Encoding.ASCII.GetString(name);
            total += count;
            bool keep = !skullOnly || layer == 0 && (bone is "Bip01 Head" or "Bip01 Jaw");
            if (!keep && stream.CanSeek)
            {
                long bytes = count * 8L * sizeof(float);
                if (bytes > stream.Length - stream.Position) throw new EndOfStreamException("Truncated anatomy vertices");
                stream.Seek(bytes, SeekOrigin.Current);
                continue;
            }
            var vertices = keep ? new AnatomyVertex[count] : null;
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var n = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var uv = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                if (!Finite(p) || !Finite(n) || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
                    throw new InvalidDataException("Non-finite anatomy vertex");
                if (vertices is not null) vertices[i] = new AnatomyVertex(p, n, uv);
            }
            if (vertices is not null) result.Add(new AnatomyBatch(bone, layer, vertices));
        }
        return result.AsReadOnly();
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
