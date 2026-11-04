using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Textures;
using Meitou.Simulation.Items;

namespace Meitou.Brutality.Rendering;

/// <summary>
/// Bakes the pinned distal blade/splatter mask into immutable vertex colours. The stock material multiplies diffuse by this tint;
/// it cannot reproduce the fork's separate wet gloss/metal blend. UVs, skin weights, morphs and original meshes remain intact.
/// </summary>
public sealed class WeaponBloodGeometry(RgbaImage? splatter = null)
{
    sealed class MeshCache { public readonly ConditionalWeakTable<ItemInstance, ItemCache> Items = new(); }
    sealed class ItemCache { public readonly Dictionary<long, OgreMesh> Revisions = []; }
    sealed class VariantCache { public RuntimeCharacterGeometry? Plain; public readonly ConditionalWeakTable<RuntimeCharacterGeometry, RuntimeCharacterGeometry> Variants = new(); }
    sealed class LookCache { public readonly ConditionalWeakTable<object, VariantCache> Stains = new(); }
    sealed class Original(RuntimeCharacterGeometry? geometry) { public RuntimeCharacterGeometry? Geometry = geometry; }
    readonly ConditionalWeakTable<OgreMesh, MeshCache> meshes = new();
    readonly ConditionalWeakTable<OgreMesh, OgreMesh> originalMeshes = new();
    readonly ConditionalWeakTable<CharacterAppearance, LookCache> looks = new();
    readonly ConditionalWeakTable<RuntimeCharacterGeometry, Original> originals = new();
    public bool UsesSplatterTexture => splatter is not null;
    public bool IsOwned(RuntimeCharacterGeometry geometry) => originals.TryGetValue(geometry, out _);

    public RuntimeCharacterGeometry? Apply(CharacterAppearance look, RuntimeCharacterGeometry? existing,
        IReadOnlyList<WeaponStainAttachment> stains, Func<string, OgreMesh?> loadMesh)
    {
        if (stains.Count == 0) return existing;
        if (existing is not null && originals.TryGetValue(existing, out var original)) existing = original.Geometry;
        var cache = looks.GetOrCreateValue(look).Stains.GetOrCreateValue(stains);
        if (existing is null && cache.Plain is { IsRetired: false } plain) return plain;
        if (existing is not null && cache.Variants.TryGetValue(existing, out var known))
        {
            if (!known.IsRetired) return known;
            cache.Variants.Remove(existing);
        }
        var byPart = new Dictionary<CharacterPart, WeaponStainAttachment>(ReferenceEqualityComparer.Instance);
        foreach (var stain in stains) byPart[stain.SourcePart] = stain;
        var source = existing?.Parts ?? look.Parts.Select(part => new RuntimeCharacterPart(part)).ToArray();
        var result = new List<RuntimeCharacterPart>(source.Count); bool changed = false;
        foreach (var supplied in source)
        {
            var part = supplied.Part;
            if (!byPart.TryGetValue(part, out var stain) || part.Field == "sheath" || part.Point == "back2"
                || part.Field != "bare sword" && part.Record.GetPath("bare sword").Length > 0
                || (supplied.Mesh ?? loadMesh(part.Mesh)) is not { } mesh)
            { result.Add(supplied); continue; }
            var painted = Paint(mesh, stain);
            result.Add(supplied with { Mesh = painted, VertexColours = true }); changed = true;
        }
        if (!changed) return existing;
        var geometry = new RuntimeCharacterGeometry(existing?.BodyMesh, result);
        originals.Add(geometry, new(existing));
        if (existing is null) cache.Plain = geometry; else cache.Variants.Add(existing, geometry);
        return geometry;
    }

    public OgreMesh Paint(OgreMesh source, in WeaponStainAttachment stain)
    {
        if (originalMeshes.TryGetValue(source, out var original)) source = original;
        var cache = meshes.GetOrCreateValue(source).Items.GetOrCreateValue(stain.Item);
        if (cache.Revisions.TryGetValue(stain.Revision, out var known)) return known;
        var positions = VertexSets(source).SelectMany(vertices => vertices.ReadPositions() ?? []).Where(Finite).ToArray();
        if (positions.Length == 0) return source;
        var min = positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = positions.Aggregate(new Vector3(float.MinValue), Vector3.Max); var size = max - min;
        int index = size.Y >= size.X && size.Y >= size.Z ? 1 : size.X >= size.Z ? 0 : 2;
        float end = MathF.Abs(max[index]) >= MathF.Abs(min[index]) ? max[index] : min[index];
        if (!float.IsFinite(end) || MathF.Abs(end) < .01f) return source;
        var axis = Vector3.Zero; axis[index] = MathF.Sign(end); float length = MathF.Abs(end);
        var blood = stain.Blood;
        var output = new OgreMesh { Version = source.Version, SkeletallyAnimated = source.SkeletallyAnimated,
            SkeletonName = source.SkeletonName, SharedVertexData = source.SharedVertexData is { } shared ? Vertices(shared, axis, length, blood) : null,
            Bounds = source.Bounds, Lod = source.Lod };
        output.BoneAssignments.AddRange(source.BoneAssignments); output.Poses.AddRange(source.Poses);
        foreach (var (key, name) in source.SubMeshNames) output.SubMeshNames[key] = name;
        foreach (var (key, count) in source.SkippedChunks) output.SkippedChunks[key] = count;
        foreach (var sub in source.SubMeshes)
        {
            var copied = new OgreSubMesh { MaterialName = sub.MaterialName, UseSharedVertices = sub.UseSharedVertices, Indices = sub.Indices,
                VertexData = !sub.UseSharedVertices && sub.VertexData is { } vertices ? Vertices(vertices, axis, length, blood) : sub.VertexData, Operation = sub.Operation };
            copied.BoneAssignments.AddRange(sub.BoneAssignments);
            foreach (var (key, alias) in sub.TextureAliases) copied.TextureAliases[key] = alias;
            output.SubMeshes.Add(copied);
        }
        originalMeshes.Add(output, source); cache.Revisions[stain.Revision] = output;
        while (cache.Revisions.Count > 4) cache.Revisions.Remove(cache.Revisions.Keys.Min());
        return output;
    }
    static IEnumerable<OgreVertexData> VertexSets(OgreMesh mesh)
    {
        if (mesh.SharedVertexData is { } shared) yield return shared;
        foreach (var sub in mesh.SubMeshes) if (!sub.UseSharedVertices && sub.VertexData is { } own) yield return own;
    }
    OgreVertexData Vertices(OgreVertexData source, Vector3 axis, float length, Vector4 blood)
    {
        var positions = source.ReadPositions(); if (positions is null) return source;
        var result = new OgreVertexData { VertexCount = source.VertexCount };
        result.Elements.AddRange(source.Elements.Where(element => element.Semantic != OgreVertexSemantic.Diffuse));
        foreach (var (key, buffer) in source.Buffers) result.Buffers[key] = buffer;
        ushort stream = 0;
        while (result.Buffers.ContainsKey(stream)) stream = checked((ushort)(stream + 1));
        var bytes = new byte[positions.Length * 4];
        var element = source.Find(OgreVertexSemantic.Diffuse); var old = element is not null ? source.ReadFloats(element) : null;
        for (int i = 0; i < positions.Length; i++)
        {
            var at = positions[i]; float along = Vector3.Dot(at, axis) / length;
            float distal = Smooth(.18f, .85f, along);
            float across = MathF.Abs(axis.X) > .5f ? at.Y : at.X;
            var texture = Sample(new(along * 1.7f, across / MathF.Max(length * .10f, .03f) + .5f));
            float coverage = Math.Clamp((texture.W - 1 + Math.Clamp(blood.W, 0, 1) * distal) * 3, 0, 1);
            var tint = Vector3.Lerp(Vector3.One, new Vector3(texture.X * blood.X, texture.Y * blood.Y, texture.Z * blood.Z), coverage);
            var before = Vector4.One;
            if (old is not null && element is not null && element.Type.ComponentCount() >= 3)
            {
                int stride = element.Type.ComponentCount(); int offset = i * stride;
                float scale = element.Type is OgreVertexElementType.Float3 or OgreVertexElementType.Float4 ? 1 : 1 / 255f;
                before = new(old[offset] * scale, old[offset + 1] * scale, old[offset + 2] * scale, stride > 3 ? old[offset + 3] * scale : 1);
                if (element.Type is OgreVertexElementType.Colour or OgreVertexElementType.ColourArgb) (before.X, before.Z) = (before.Z, before.X);
            }
            var colour = before * new Vector4(tint, 1);
            for (int component = 0; component < 4; component++) bytes[i * 4 + component] = (byte)Math.Clamp((int)MathF.Round(colour[component] * 255), 0, 255);
        }
        result.Elements.Add(new(stream, OgreVertexElementType.ColourAbgr, OgreVertexSemantic.Diffuse, 0, 0));
        result.Buffers[stream] = new(4, bytes); return result;
    }
    Vector4 Sample(Vector2 uv)
    {
        if (splatter is null || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) return Vector4.One;
        float x = (uv.X - MathF.Floor(uv.X)) * splatter.Width - .5f, y = (uv.Y - MathF.Floor(uv.Y)) * splatter.Height - .5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y); float tx = x - ix, ty = y - iy;
        Vector4 Pixel(int px, int py)
        {
            var pixel = splatter[(px % splatter.Width + splatter.Width) % splatter.Width, (py % splatter.Height + splatter.Height) % splatter.Height];
            return new Vector4(pixel.R, pixel.G, pixel.B, pixel.A) / 255f;
        }
        return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), tx), Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), tx), ty);
    }
    static float Smooth(float low, float high, float value) { float x = Math.Clamp((value - low) / (high - low), 0, 1); return x * x * (3 - 2 * x); }
    static bool Finite(Vector3 at) => float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z);
}
