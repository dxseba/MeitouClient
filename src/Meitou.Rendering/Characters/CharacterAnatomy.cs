using System.Numerics;
using Meitou.Content;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

/// <summary>CPU anatomy preparation shared by uploads and deformation checks; no source assets are shipped.</summary>
static class CharacterAnatomy
{
    /// <summary>World units kept between anatomy and the skin above it (1 unit = 100 mm: 20 mm, the deep end of the GTA V FLESH fit).</summary>
    const float SkullMargin = 0.5f, AnatomyGrid = 0.04f, AnatomyClearance = 0.2f;
    /// <summary>What a stump shows, as Gore Lab's SeverBoneExposure: the limb's bone a little past the skin's cut (LimbHide heights), muscle up to it.</summary>
    static bool StaysOnStump(Meitou.Data.Characters.EcorcheBatch batch, Vector3 p, bool female)
    {
        int group = WoundMasks.BodyGroup(batch.Bone);
        if (group == 0 || batch.Layer > 1 || !(batch.Bone.Contains("UpperArm") || batch.Bone.Contains("Thigh"))) return false;   // what stays on the body
        float cut = group < 3 ? (female ? 14.1f : 13.83f) : 8.6f;
        return p.Y > cut - (batch.Layer == 0 ? 1.3f : -0.2f);
    }

    /// <summary>Each organ's own colour (Anatomy.hlsl; the organ is the tens of the file's u): an opened body reads as organs, not one red mass.</summary>
    static Vector4 OrganColour(int organ) => organ switch
    {
        1 => new(0.45f, 0.07f, 0.09f, 1), 2 => new(0.34f, 0.12f, 0.14f, 1), 3 => new(0.3f, 0.045f, 0.055f, 1), 4 => new(0.4f, 0.15f, 0.13f, 1), 5 => new(0.42f, 0.19f, 0.17f, 1), 6 => new(0.22f, 0.035f, 0.035f, 1),
        _ => new(0.4f, 0.1f, 0.1f, 1),
    };

    /// <summary>The bowel's group: with the torso's until it spills, then gone (<see cref="DebrisRenderer.Bowel"/>).</summary>
    const int LooseBowelGroup = 5;
    public static List<Vertex>? Build(SkeletonRig rig, WoundPoint[] skin, Vertex[] headSource, bool female, MeshMorphs? morphs,
        bool headless, bool headOnly, string path)
    {
        path = GameFiles.Resolve(path);
        if (skin.Length == 0 || !File.Exists(path) || rig.Handle("Bip01 Pelvis") is not { } pelvis || rig.Handle("Bip01 Neck") is not { } neck || Math.Max(pelvis, neck) >= rig.BindPosition.Length) return null;
        Vector3 hip = rig.BindPosition[pelvis];
        float scale = (rig.BindPosition[neck] - hip).Length() / 6.1649f;
        if (scale is < 0.5f or > 2f) return null;
        using var stream = File.OpenRead(path);
        // The skin vertices by unit cell: each anatomy vertex takes its nearest skin UV, so it can read the wound mask.
        var cells = new Dictionary<(int, int, int), List<WoundPoint>>();
        static (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        foreach (var point in skin)
        {
            if (!cells.TryGetValue(Cell(point.Position), out var list)) cells[Cell(point.Position)] = list = [];
            list.Add(point);
        }
        // Of the same body group: in the bind pose a hand lies beside the hip, and must not read the hip's wounds.
        var groups = new int[rig.BoneCount];
        foreach (var boneRecord in rig.Skeleton.Bones) if (boneRecord.Handle < groups.Length) groups[boneRecord.Handle] = WoundMasks.BodyGroup(boneRecord.Name);
        WoundPoint Nearest(Vector3 p, int group, int fallbackBone = 0)
        {
            var (x, y, z) = Cell(p);
            float best = float.MaxValue;
            var found = skin[0];
            for (int r = 1; r <= 4 && best == float.MaxValue; r++)
                for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) for (int k = -r; k <= r; k++)
                    if (cells.TryGetValue((x + i, y + j, z + k), out var list))
                        foreach (var q in list)
                        {
                            if (groups[Math.Min(q.Bone, groups.Length - 1)] != group) continue;
                            float d = Vector3.DistanceSquared(q.Position, p);
                            if (d < best) (best, found) = (d, q);
                        }
            // An unsupported local region must not inherit the first body vertex (often a foot or the other arm).
            if (best == float.MaxValue)
                foreach (var q in skin)
                {
                    if (groups[Math.Min(q.Bone, groups.Length - 1)] != group) continue;
                    float distance = Vector3.DistanceSquared(q.Position, p);
                    if (distance < best) (best, found) = (distance, q);
                }
            return best < float.MaxValue ? found : new WoundPoint(p, Vector3.UnitZ, default, fallbackBone, (uint)fallbackBone, Vector4.UnitX);
        }
        var vertices = new List<Vertex>();
        var batches = Meitou.Data.Characters.EcorcheAsset.Read(stream);
        // Negative V also includes Shek torso spikes. Head/jaw skin influence and horn-pose slots identify the actual head volume.
        bool headSupported = HeadWoundSurface.TryAnatomyBounds(headSource,
            rig.Handle("Bip01 Head") ?? -1, rig.Handle("Bip01 Jaw") ?? -1, morphs, out var headMin, out var headMax);
        var (skullMin, skullMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var b in batches)
            if (b.Layer == 0 && b.Bone is "Bip01 Head" or "Bip01 Jaw")
                foreach (var v in b.Vertices)
                {
                    var p = (v.Position - new Vector3(-0.0446148f, 9.956298f, -0.227702f)) * scale + hip;
                    (skullMin, skullMax) = (Vector3.Min(skullMin, p), Vector3.Max(skullMax, p));
                }
        var skullScale = headSupported ? Vector3.Max(headMax - headMin - new Vector3(SkullMargin * 2), new Vector3(0.05f)) / Vector3.Max(skullMax - skullMin, new Vector3(0.01f)) : Vector3.One;
        var (skullMiddle, headMiddle) = ((skullMin + skullMax) / 2, (headMin + headMax) / 2);
        foreach (var batch in batches)
        {
            bool isHead = batch.Bone is "Bip01 Head" or "Bip01 Jaw";
            if ((headless && isHead) || (headOnly && !isHead)) continue;
            if (batch.Layer is 2 or 3) continue;   // tendon and fat: the cut's own rim stands for them
            if (!headSupported && batch.Bone is "Bip01 Head" or "Bip01 Jaw") continue;
            if ((rig.Handle(batch.Bone) ?? (batch.Bone == "Bip01 Jaw" ? rig.Handle("Bip01 Head") : null)) is not { } bone) continue;
            var colour = batch.Layer switch { 0 => new Vector4(0.61f, 0.49f, 0.32f, 1), 1 => new Vector4(0.36f, 0.075f, 0.07f, 1), 2 => new Vector4(0.64f, 0.54f, 0.36f, 1), 3 => new Vector4(0.55f, 0.40f, 0.23f, 1), _ => new Vector4(0.55f, 0.12f, 0.12f, 1) };
            // The face keeps skull/teeth and its dark inside; remove whole soft-tissue triangles to preserve later layer/bone boundaries.
            IEnumerable<EcorcheVertex> sourceVertices = batch.Layer == 0 ? batch.Vertices : AnatomyTriangles.BelowHeight(batch.Vertices, (16.9f - hip.Y) / scale + 9.956298f);
            foreach (var v in sourceVertices)
            {
                var fitted = (v.Position - new Vector3(-0.0446148f, 9.956298f, -0.227702f)) * scale + hip;
                // The file's rib cage against this body: a narrower sternum, stouter ribs.
                if (batch.Layer == 0 && WoundMasks.BodyGroup(batch.Bone) == 0 && fitted.Y is > 11f and < 16f && fitted.Z > 0)
                {
                    if (MathF.Abs(fitted.X) < 0.75f) fitted = new Vector3(fitted.X * 0.68f, fitted.Y, fitted.Z - 0.1f);
                    else fitted += v.Normal * 0.05f;
                }
                if (batch.Layer == 0 && batch.Bone is "Bip01 Head" or "Bip01 Jaw") fitted = headMiddle + (fitted - skullMiddle) * skullScale;
                // Kept under the skin: anything above the clearance moves down along the skin's normal.
                var over = Nearest(fitted, WoundMasks.BodyGroup(batch.Bone), bone);
                float height = Vector3.Dot(fitted - over.Position, over.Normal);
                if ((batch.Layer != 0 || WoundMasks.BodyGroup(batch.Bone) != 0) && height > -AnatomyClearance) fitted -= over.Normal * (height + AnatomyClearance);   // bone keeps its true shape: the depth push hides what stands proud
                vertices.Add(AnatomyTriangles.FollowBone(new Vertex { Position = fitted, Normal = v.Normal, Uv = over.Uv, Tangent = new Vector4(fitted, batch.Layer), Colour = (batch.Layer == 4 ? OrganColour((int)MathF.Floor(v.Uv.X / 10)) : colour) with { W = batch.Layer == 4 && (int)MathF.Floor(v.Uv.X / 10) == 5 ? LooseBowelGroup : WoundMasks.BodyGroup(batch.Bone) + (StaysOnStump(batch, fitted, female) ? 0.25f : 0) } }, bone));
            }
        }
        // The body's back wall (Gore Lab's cavity lining): what a line of sight through the torso ends on. Layer 7, the torso's group.
        float[] wallX = female ? CavityLiningData.FemaleX : CavityLiningData.MaleX, wallZ = female ? CavityLiningData.FemaleZ : CavityLiningData.MaleZ;
        Vertex Wall(int row, int column)
        {
            var p = new Vector3(wallX[row * CavityLiningData.Columns + column], CavityLiningData.Y0 + (CavityLiningData.Y1 - CavityLiningData.Y0) * row / (CavityLiningData.Rows - 1), wallZ[row * CavityLiningData.Columns + column]);
            var over = Nearest(p, 0);
            return new Vertex { Position = p, Normal = Vector3.Normalize(new Vector3(-p.X, 0, 0.5f - p.Z)), Uv = over.Uv, Tangent = new Vector4(0, 0, 0, 7), Colour = new Vector4(0.2f, 0.03f, 0.035f, 0), Bones = over.Bones, Weights = over.Weights };
        }
        var axial = new[] { "Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck" }
            .Select(name => rig.Handle(name)).Where(handle => handle.HasValue).Select(handle => (int)handle!.Value).ToArray();
        if (!headOnly)
            for (int row = 0; row + 1 < CavityLiningData.Rows; row++)
                for (int column = 0; column + 1 < CavityLiningData.Columns; column++)
                {
                    Vertex a = Wall(row, column), b = Wall(row, column + 1), c = Wall(row + 1, column), d = Wall(row + 1, column + 1);
                    var centre = (a.Position + b.Position + c.Position + d.Position) * 0.25f;
                    int wallBone = pelvis;
                    float nearest = float.PositiveInfinity;
                    foreach (int candidate in axial)
                    {
                        float distance = Vector3.DistanceSquared(centre, rig.BindPosition[candidate]);
                        if (distance < nearest) (wallBone, nearest) = (candidate, distance);
                    }
                    // One local quad follows one axial body; a spine/arm split cannot pull its corners into different fragments.
                    foreach (var point in new[] { a, b, c, b, d, c }) vertices.Add(AnatomyTriangles.FollowBone(point, wallBone));
                }
        // The mouth behind a lost jaw (Z-Anatomy, the user's Mouth.anatomy): placed as one piece, box to box, in the head's lower part. Rigid on the head bone.
        if (!headless && headSupported && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Mouth.anatomy")) && rig.Handle("Bip01 Head") is { } mouthBone)
        {
            using var mouthStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!, "Mouth.anatomy"));
            var mouth = Meitou.Data.Characters.EcorcheAsset.Read(mouthStream);
            var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var part in mouth) foreach (var v in part.Vertices) (min, max) = (Vector3.Min(min, v.Position), Vector3.Max(max, v.Position));
            var head = headMax - headMin;
            float mouthScale = MathF.Min(head.X * 0.62f / (max.X - min.X), head.Y * 0.5f / (max.Y - min.Y));
            var place = new Vector3(headMiddle.X, headMin.Y + head.Y * 0.32f, headMiddle.Z + head.Z * 0.05f);
            // The lower arch of the gums left with the jaw: only the upper half stays.
            float gumMiddle = mouth.Where(b => b.Bone == "Gingiva").SelectMany(b => b.Vertices).Select(v => v.Position.Y).DefaultIfEmpty(0).Average();
            foreach (var part in mouth)
            {
                var colour = part.Bone.Contains("constrictor") ? new Vector4(0.26f, 0.05f, 0.05f, 0) : part.Bone == "Gingiva" ? new Vector4(0.55f, 0.2f, 0.2f, 0) : new Vector4(0.5f, 0.17f, 0.18f, 0);
                for (int t = 0; t < part.Vertices.Length; t++)
                {
                    var v = part.Vertices[t];
                    if (part.Bone == "Gingiva" && part.Vertices[t - t % 3].Position.Y < gumMiddle) continue;
                    // The throat stops at the palate's height, or it fills the lost jaw's place like a beard.
                    if (part.Bone.Contains("constrictor") && part.Vertices[t - t % 3].Position.Y < (min.Y + max.Y) / 2) continue;
                    var p = (v.Position - (min + max) / 2) * mouthScale + place;
                    if (part.Bone == "Tongue") p = place + new Vector3(0, -head.Y * 0.03f, head.Z * 0.02f) + (p - place) * 0.7f;   // short, hanging in the open behind the upper teeth
                    vertices.Add(new Vertex { Position = p, Normal = v.Normal, Uv = skin[0].Uv, Tangent = new Vector4(p, 5), Colour = colour, Bones = (uint)mouthBone, Weights = new Vector4(1, 0, 0, 0) });
                }
            }
        }
        // The brain behind a broken skull (Z-Anatomy, the user's Brain.anatomy): box to box inside the skull. Rigid on the head bone.
        if (!headless && headSupported && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Brain.anatomy")) && rig.Handle("Bip01 Head") is { } brainBone)
        {
            using var brainStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!, "Brain.anatomy"));
            var brain = Meitou.Data.Characters.EcorcheAsset.Read(brainStream);
            var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var part in brain) foreach (var v in part.Vertices) (min, max) = (Vector3.Min(min, v.Position), Vector3.Max(max, v.Position));
            var fit = (headMax - headMin) * new Vector3(0.8f, 0.5f, 0.8f) / (max - min);
            var place = new Vector3(headMiddle.X, headMin.Y + (headMax.Y - headMin.Y) * 0.68f, headMiddle.Z);
            foreach (var part in brain)
                foreach (var v in part.Vertices)
                {
                    var p = (v.Position - (min + max) / 2) * fit + place;
                    vertices.Add(new Vertex { Position = p, Normal = v.Normal, Uv = skin[0].Uv, Tangent = new Vector4(p, 6), Colour = new Vector4(0.6f, 0.4f, 0.4f, 0), Bones = (uint)brainBone, Weights = new Vector4(1, 0, 0, 0) });
                }
        }
        // Clustered on a fine grid: seen only through cuts, the file's detail is lost, and triangles that collapse go.
        for (int i = 0; i + 2 < vertices.Count;)
        {
            static Vector3 Snap(Vector3 p) => new(MathF.Round(p.X / AnatomyGrid) * AnatomyGrid, MathF.Round(p.Y / AnatomyGrid) * AnatomyGrid, MathF.Round(p.Z / AnatomyGrid) * AnatomyGrid);
            if (vertices[i].Position.Y > 16.9f && vertices[i].Tangent.W is 0 or 6) { i += 3; continue; }   // the skull keeps its detail: teeth do not survive the grid
            Vector3 a = Snap(vertices[i].Position), b = Snap(vertices[i + 1].Position), c = Snap(vertices[i + 2].Position);
            if (a == b || b == c || a == c) { vertices[i] = vertices[^3]; vertices[i + 1] = vertices[^2]; vertices[i + 2] = vertices[^1]; vertices.RemoveRange(vertices.Count - 3, 3); continue; }
            vertices[i] = vertices[i] with { Position = a };
            vertices[i + 1] = vertices[i + 1] with { Position = b };
            vertices[i + 2] = vertices[i + 2] with { Position = c };
            i += 3;
        }
        return vertices.Count == 0 ? null : vertices;
    }
}
