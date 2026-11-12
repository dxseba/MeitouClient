using Meitou.Game.Rendering.Frame;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Globalization;
using Meitou.Brutality.Anatomy;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering;

/// <summary>Fits the packaged anatomical layers to the current race/face. The pinned Ecorche origin, skull clearance and organ colours are retained.</summary>
internal static class AnatomyGeometry
{
    const float SkullMargin = .12f, AnatomyClearance = .2f, AnatomyGrid = .04f;
    static readonly Vector3 SourceHip = new(-.0446148f, 9.956298f, -.227702f);

    readonly record struct FitKey(OgreMesh Body, SkeletonRig Rig, bool Female, uint Blood, bool Robot, bool Bowel, string Morphs);
    static readonly ConditionalWeakTable<AnatomyLibrary, AnatomyFitCache<FitKey>> sharedFits = new();

    public static OgreMesh? Build(AnatomyLibrary library, CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, bool includeBowel = true)
    {
        // Outfit and wound variants share the fit; only actual baked mesh morphs can change the skin surface.
        bool shaved = appearance.Character?.GetBool("shaved") == true;
        string morphs = string.Join(';', body.Poses.Select(pose => pose.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => (Name: name, Value: appearance.PoseWeights.TryGetValue(name, out float stored) ? CharacterShape.BakeWeight(name, stored, shaved) : null))
            .Where(pose => pose.Value is not null).Select(pose => pose.Name + "=" + pose.Value!.Value.ToString("R", CultureInfo.InvariantCulture)));
        var key = new FitKey(body, rig, appearance.Female, unchecked((uint)appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF,
            appearance.Race.GetBool("is robot"), includeBowel, morphs);
        var cache = sharedFits.GetValue(library, static _ => new());
        return cache.GetOrCreate(key, () => Fit(library, appearance, rig, body, includeBowel), morphs.Length * 2L);
    }

    static OgreMesh? Fit(AnatomyLibrary library, CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, bool includeBowel)
    {
        var model = Model.Build(body, rig.BoneCount, gameBinormals: true);
        var groups = MeshCut.BoneGroups(rig);
        bool shaved = appearance.Character?.GetBool("shaved") == true;
        var skin = model.Parts.SelectMany(part =>
        {
            var delta = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, appearance.PoseWeights, shaved);
            return part.Vertices.Select((v, i) => v with { Position = v.Position + delta[i] });
        }).ToArray();
        if (skin.Length == 0) return null;
        if (appearance.Race.GetBool("is robot"))
        {
            var mechanical = MechanicalAnatomy.Build(rig, skin, skin);
            return mechanical is { Count: > 0 } ? MeshCut.FromTriangles(mechanical, body.SkeletonName) : null;
        }
        if (rig.Handle("Bip01 Pelvis") is not { } pelvis || rig.Handle("Bip01 Neck") is not { } neck) return null;
        var hip = rig.BindPosition[pelvis];
        float scale = Vector3.Distance(rig.BindPosition[neck], hip) / 6.1649f;
        if (!float.IsFinite(scale) || scale is < .5f or > 2) return null;
        var batches = library.Get(AnatomyKind.Body);
        var output = new List<Vertex>();
        var cells = new Dictionary<(int X, int Y, int Z), List<Vertex>>();
        static (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        foreach (var point in skin)
        {
            var cell = Cell(point.Position);
            if (!cells.TryGetValue(cell, out var list)) cells.Add(cell, list = []);
            list.Add(point);
        }
        var nearest = new Dictionary<(Vector3 Position, int Group, int Fallback), Vertex>();
        Vertex Nearest(Vector3 p, int group, int fallback)
        {
            var key = (p, group, fallback);
            if (nearest.TryGetValue(key, out var cached)) return cached;
            var (x, y, z) = Cell(p);
            float best = float.MaxValue;
            var found = new Vertex { Position = p, Normal = Vector3.UnitZ, Bones = (uint)fallback, Weights = Vector4.UnitX };
            for (int r = 1; r <= 4 && best == float.MaxValue; r++)
                for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) for (int k = -r; k <= r; k++)
                    if (cells.TryGetValue((x + i, y + j, z + k), out var list))
                        foreach (var q in list)
                        {
                            if (MeshCut.Group(q, groups) != group) continue;
                            float distance = Vector3.DistanceSquared(q.Position, p);
                            if (distance < best) (best, found) = (distance, q);
                        }
            nearest.Add(key, found); return found;
        }

        var headPoints = HeadReliefGeometry.HeadPoints(body, rig, appearance);
        var (headMin, headMax) = Bounds(headPoints);
        var headExtent = headMax - headMin;
        bool supportedHead = headPoints.Length > 0 && headExtent.X > 0 && headExtent.Y > 0 && headExtent.Z > 0;
        var headMiddle = (headMin + headMax) * .5f;
        Vector3 FitSource(Vector3 p) => (p - SourceHip) * scale + hip;
        var skull = batches.Where(b => b.Layer == 0 && b.Bone is "Bip01 Head" or "Bip01 Jaw")
            .SelectMany(b => b.Vertices).Select(v => FitSource(v.Position)).ToArray();
        var (skullMin, skullMax) = Bounds(skull);
        var skullMargin = Vector3.Min(new Vector3(SkullMargin), (headMax - headMin) * .06f);
        var skullScale = Vector3.Max(headMax - headMin - skullMargin * 2, new Vector3(.05f)) / Vector3.Max(skullMax - skullMin, new Vector3(.01f));
        var skullMiddle = (skullMin + skullMax) * .5f;
        uint blood = unchecked((uint)appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        foreach (var batch in batches)
        {
            bool isHead = batch.Bone is "Bip01 Head" or "Bip01 Jaw";
            if (batch.Layer is 2 or 3 || isHead && !supportedHead) continue;
            if ((rig.Handle(batch.Bone) ?? (batch.Bone == "Bip01 Jaw" ? rig.Handle("Bip01 Head") : null)) is not { } bone) continue;
            int group = bone < groups.Length ? groups[bone] : 0;
            for (int triangle = 0; triangle + 2 < batch.Vertices.Length; triangle += 3)
            {
                if (!includeBowel && batch.Layer == 4 && Enumerable.Range(0, 3).All(k => (int)MathF.Floor(batch.Vertices[triangle + k].Uv.X / 10) == 5)) continue;
                // The face retains skull/teeth; soft layers stop below the pin's face height.
                if (batch.Layer != 0 && Enumerable.Range(0, 3).Any(k => FitSource(batch.Vertices[triangle + k].Position).Y > 16.9f)) continue;
                for (int k = 0; k < 3; k++)
                {
                    var source = batch.Vertices[triangle + k];
                    var p = FitSource(source.Position);
                    var normal = source.Normal;
                    if (batch.Layer == 0 && group == 0 && p.Y is > 11 and < 16 && p.Z > 0)
                        p = MathF.Abs(p.X) < .75f ? new(p.X * .68f, p.Y, p.Z - .1f) : p + normal * .05f;
                    if (batch.Layer == 0 && isHead && skull.Length > 0)
                    {
                        p = headMiddle + (p - skullMiddle) * skullScale;
                        normal = Unit(normal / skullScale);
                    }
                    var over = Nearest(p, group, bone);
                    float height = Vector3.Dot(p - over.Position, over.Normal);
                    if ((batch.Layer != 0 || group != 0) && height > -AnatomyClearance) p -= over.Normal * (height + AnatomyClearance);
                    var colour = batch.Layer switch
                    {
                        0 => new Vector4(.61f, .49f, .32f, 1), 1 => Recolour(new(.36f, .075f, .07f, 1), blood),
                        4 => Recolour(OrganColour((int)MathF.Floor(source.Uv.X / 10)), blood), _ => Recolour(new(.55f, .12f, .12f, 1), blood),
                    };
                    if (!(isHead && batch.Layer == 0)) p = Snap(p);
                    output.Add(Follow(p, normal, colour, bone));
                }
            }
        }
        if (supportedHead && rig.Handle("Bip01 Head") is { } headBone)
        {
            AppendFitted(library.Get(AnatomyKind.Brain), (headMax - headMin) * new Vector3(.8f, .5f, .8f),
                new(headMiddle.X, headMin.Y + (headMax.Y - headMin.Y) * .68f, headMiddle.Z), Recolour(new(.6f, .4f, .4f, 1), blood), headBone, output);
            AppendFitted(library.Get(AnatomyKind.Mouth), (headMax - headMin) * new Vector3(.62f, .5f, .65f),
                new(headMiddle.X, headMin.Y + (headMax.Y - headMin.Y) * .27f, headMiddle.Z), Recolour(new(.45f, .12f, .13f, 1), blood), headBone, output);
        }
        // Keep complete triangles; the source's fine grid removes invisible interior detail without changing articulated ownership.
        int written = 0;
        for (int i = 0; i + 2 < output.Count; i += 3)
        {
            if (Vector3.Cross(output[i + 1].Position - output[i].Position, output[i + 2].Position - output[i].Position).LengthSquared() < 1e-12f) continue;
            output[written++] = output[i]; output[written++] = output[i + 1]; output[written++] = output[i + 2];
        }
        if (written < output.Count) output.RemoveRange(written, output.Count - written);
        return output.Count == 0 ? null : MeshCut.FromTriangles(output, body.SkeletonName);
    }

    static void AppendFitted(IReadOnlyList<AnatomyBatch> batches, Vector3 size, Vector3 place, Vector4 colour, int bone, List<Vertex> output)
    {
        var points = batches.SelectMany(b => b.Vertices).Select(v => v.Position).ToArray();
        if (points.Length == 0) return;
        var (min, max) = Bounds(points);
        var scale = size / Vector3.Max(max - min, new Vector3(.001f));
        foreach (var batch in batches)
            foreach (var vertex in batch.Vertices)
                output.Add(Follow((vertex.Position - (min + max) * .5f) * scale + place, Unit(vertex.Normal / scale), colour, bone));
    }

    internal static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); bool any = false;
        foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true; }
        return any ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    static Vertex Follow(Vector3 position, Vector3 normal, Vector4 colour, int bone) => new() { Position = position, Normal = normal,
        Tangent = new(Unit(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal)), 1),
        Colour = colour with { W = 1 }, Bones = (uint)bone, Weights = Vector4.UnitX };
    static Vector3 Unit(Vector3 p) => p.LengthSquared() > 1e-12f ? Vector3.Normalize(p) : Vector3.UnitY;
    static Vector3 Snap(Vector3 p) => new(MathF.Round(p.X / AnatomyGrid) * AnatomyGrid, MathF.Round(p.Y / AnatomyGrid) * AnatomyGrid, MathF.Round(p.Z / AnatomyGrid) * AnatomyGrid);
    static Vector4 OrganColour(int organ) => organ switch
    {
        1 => new(.45f, .07f, .09f, 1), 2 => new(.34f, .12f, .14f, 1), 3 => new(.30f, .045f, .055f, 1),
        4 => new(.40f, .15f, .13f, 1), 5 => new(.42f, .19f, .17f, 1), 6 => new(.22f, .035f, .035f, 1), _ => new(.4f, .1f, .1f, 1),
    };
    internal static Vector4 Recolour(Vector4 source, uint blood)
    {
        blood &= 0xFFFFFF;
        if (blood == 0x800000) return source;
        var tint = new Vector3(blood >> 16 & 255, blood >> 8 & 255, blood & 255) / 128;
        float chroma = MathF.Max(source.X - MathF.Min(source.Y, source.Z), 0);
        return new(Vector3.Clamp(new Vector3(source.X, source.Y, source.Z) + (tint - Vector3.UnitX) * chroma, Vector3.Zero, Vector3.One), source.W);
    }
}
