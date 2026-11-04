using System.Numerics;
using System.Text;

namespace Meitou.Data.Characters;

/// <summary>An anatomy vertex in the Ecorche file's bind space.</summary>
public readonly record struct EcorcheVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

/// <summary>Triangles that follow one bone rigidly. Layer: bone 0, muscle 1, tendon 2, fat 3, organ 4.</summary>
public sealed record EcorcheBatch(string Bone, int Layer, EcorcheVertex[] Vertices);

/// <summary>
/// Reads <c>Ecorche.anatomy</c> and its companion meshes (magic <c>ECR1</c>; per batch: name length, layer, vertex count, name, then 8 floats a vertex).
/// </summary>
public static class EcorcheAsset
{
    const int MaxBatches = 90, MaxNameLength = 80, MaxLayer = 6, MaxBatchVertices = 200000, MaxVertices = 1500000;

    public static IReadOnlyList<EcorcheBatch> Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "ECR1") throw new InvalidDataException("Not an Ecorche anatomy file");
        int batches = reader.ReadInt32();
        if (batches is <= 0 or > MaxBatches) throw new InvalidDataException("Invalid anatomy batch count");
        var result = new List<EcorcheBatch>(batches);
        int total = 0;
        for (int b = 0; b < batches; b++)
        {
            int length = reader.ReadInt32(), layer = reader.ReadInt32(), count = reader.ReadInt32();
            if (length is <= 0 or > MaxNameLength || layer is < 0 or > MaxLayer || count is <= 0 or > MaxBatchVertices || count % 3 != 0) throw new InvalidDataException("Invalid anatomy batch");
            string bone = Encoding.ASCII.GetString(reader.ReadBytes(length));
            var vertices = new EcorcheVertex[count];
            for (int i = 0; i < count; i++)
                vertices[i] = new EcorcheVertex(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            total += count;
            if (total > MaxVertices) throw new InvalidDataException("Anatomy budget exceeded");
            result.Add(new EcorcheBatch(bone, layer, vertices));
        }
        return result;
    }
}
