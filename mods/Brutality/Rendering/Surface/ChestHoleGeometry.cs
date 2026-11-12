using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>
/// The punched tunnel's wall: a tube around the hole's axis from the front skin to the back skin. Each ring meets the (morphed) skin where
/// a line along the axis crosses it, and takes that skin's bone weights, so the wall bends with the body as the skin does.
/// </summary>
internal static class ChestHoleGeometry
{
    const int Around = 28, Along = 6;
#if BRUTALITY_STOCK_MASTER
    const float LayerTag = 1;
#else
    const float LayerTag = NativeSurfaceShaders.ChestHoleLayer;
#endif
    /// <summary>How far each end stays inside the skin (bind units).</summary>
    const float Inset = .03f;

    readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Vertex VA, Vertex VB, Vertex VC);
    readonly record struct Crossing(Vector3 Position, uint Bones, Vector4 Weights);

    public static OgreMesh? Lining(Vertex[] body, Vector4[]? morphs, SkeletonRig rig, in ChestHoleFrame frame, uint blood, string? skeleton)
    {
#if BRUTALITY_STOCK_MASTER
        var colour = AnatomyGeometry.Recolour(new Vector4(.30f, .02f, .025f, 1), blood);
#else
        // The colour's alpha tells the wall shader the region (0 chest, 1 stomach).
        var colour = NativeFluidPalette.Recolour(new Vector4(.30f, .02f, .025f, 0), blood) with { W = frame.Region };
#endif
        return Lining(body, morphs, rig, frame, colour, skeleton, out _);
    }

    internal static OgreMesh? Lining(Vertex[] body, Vector4[]? morphs, SkeletonRig rig, in ChestHoleFrame frame, Vector4 colour,
        string? skeleton, out Vertex[] exit)
    {
        return Lining(TorsoFaces(body, morphs, rig), frame, colour, skeleton, out exit);
    }

#if BRUTALITY_STOCK_MASTER
    /// <summary>Fit the requested wound to the morphed shell. Use the same fitted frame for clipping skin and clothing.</summary>
    internal static OgreMesh? FitLining(Vertex[] body, SkeletonRig rig, in ChestHoleState hole, StockTissuePalette palette,
        bool mechanical, string? skeleton, out ChestHoleFrame fitted, out Vertex[] exit, out Vertex[] entry)
    {
        fitted = default;
        exit = [];
        entry = [];
        if (ChestHoleFrame.Resolve(rig, hole) is not { } frame) return null;
        var faces = TorsoFaces(body, null, rig);
        if (StockLining(faces, frame, palette, mechanical, skeleton, out exit, out entry) is { } exact)
        {
            fitted = frame;
            return exact;
        }

        // Torso widths differ much more than the pelvis-to-neck scale. Prefer the same impact height,
        // then move within the struck region. Preserve the side of entry and the character's actual shell.
        ReadOnlySpan<float> widths = [.82f, .64f, .50f, .36f, .24f, .18f];
        ReadOnlySpan<float> offsets = [0, .055f, -.055f, .105f, -.105f];
        float low = hole.Region == ChestHoleState.Abdomen ? .2f : .5f;
        float high = hole.Region == ChestHoleState.Abdomen ? .5f : .8f;
        foreach (float offset in offsets)
            foreach (float width in widths)
            {
                var smaller = hole with
                {
                    Height = Math.Clamp(hole.Height + offset, low, high),
                    Across = hole.Across * .35f,
                    Radius = MathF.Max(ChestHoleState.MinRadius, hole.Radius * width),
                    Yaw = hole.Yaw * .5f,
                };
                if (ChestHoleFrame.Resolve(rig, smaller) is not { } candidate
                    || StockLining(faces, candidate, palette, mechanical, skeleton, out exit, out entry) is not { } fit) continue;
                fitted = candidate;
                return fit;
            }
        return null;
    }

    // CutVolume is convex. Intersect uneven support planes before sampling the edge, so flattened
    // stretches and shoulders stay asymmetric without leaving islands inside the opening.
    internal static float[]? OpeningProfile(in ChestHoleFrame frame, bool mechanical)
    {
        if (mechanical) return null;
        Span<Vector2> normals = stackalloc Vector2[Around];
        Span<float> limits = stackalloc float[Around];
        float roughness = .82f + .15f * frame.Tear;
        float phase = frame.Seed * .39f;
        for (int k = 0; k < Around; k++)
        {
            float angle = k * MathF.Tau / Around;
            normals[k] = new(MathF.Cos(angle), MathF.Sin(angle));
            limits[k] = 1 + roughness * (.085f * MathF.Cos(angle + phase) + .105f * MathF.Sin(2 * angle - phase)
                + .17f * MathF.Cos(3 * angle + phase * 1.7f) + .045f * MathF.Sin(5 * angle - phase * .73f));
        }
        var profile = new float[Around];
        for (int k = 0; k < Around; k++)
        {
            float radius = limits[k];
            for (int plane = 0; plane < Around; plane++)
            {
                float facing = Vector2.Dot(normals[k], normals[plane]);
                if (facing > .01f) radius = MathF.Min(radius, limits[plane] / facing);
            }
            profile[k] = radius;
        }
        return profile;
    }

    static OgreMesh? StockLining(List<Face> faces, in ChestHoleFrame frame, StockTissuePalette palette, bool mechanical,
        string? skeleton, out Vertex[] exit, out Vertex[] entry)
    {
        // Follow the skin cutter's flared sides. Seat the outer row under intact skin around each mouth.
        float span = MathF.Max(frame.Radius * 2, 1.8f);
        float near = MathF.Max(.02f, frame.LiningRadius(-span) - .025f) + .035f;
        float far = MathF.Max(.02f, frame.LiningRadius(span) - .025f) + .035f;
        var sides = new (Crossing Entry, Crossing Exit)?[Around];
        var profile = OpeningProfile(frame, mechanical);
        for (int k = 0; k < Around; k++)
        {
            float angle = k * MathF.Tau / Around;
            var side = frame.Across(angle, profile?[k] ?? 1);
            var origin = frame.Centre + side * ((near + far) * .5f);
            var ray = frame.Axis + side * ((far - near) / (2 * span));
            sides[k] = Cross(faces, origin, ray, inset: 0);
        }
        exit = [];
        entry = [];
        if (!FillStockGaps(sides, frame, near, far, span, profile)) return null;

        // Stagger the rolls at the lips, then narrow into an irregular throat with no plane closing it off.
        ReadOnlySpan<float> stations = [0, .035f, .08f, .14f, .22f, .34f, .47f, .60f, .72f, .81f, .89f, .95f, 1];
        ReadOnlySpan<float> widths = [1, .87f, .78f, .73f, .69f, .65f, .62f, .63f, .67f, .72f, .79f, .85f, 1];
        var lateral = Vector3.Normalize(Vector3.Cross(frame.Axis, Vector3.UnitY));
        int rows = stations.Length;
        var grid = new Vertex[Around, rows];
        for (int k = 0; k < Around; k++)
        {
            var (front, back) = sides[k]!.Value;
            float angle = k * MathF.Tau / Around;
            var side = frame.Across(angle, profile?[k] ?? 1);
            float f1 = MathF.Sin(5 * angle + frame.Seed);
            float f2 = MathF.Sin(11 * angle - frame.Seed * 1.7f);
            for (int j = 0; j < rows; j++)
            {
                float t = stations[j];
                float taper = MathF.Sin(MathF.PI * t);
                var point = Vector3.Lerp(front.Position, back.Position, t);
                float depth = Vector3.Dot(point - frame.Centre, frame.Axis);
                float radius = (near + far) * .5f + (far - near) * depth / (2 * span);
                bool seat = j == 0 || j == rows - 1;
                float tear = seat ? 0 : frame.Tear * (.049f * f1 + .032f * f2);
                float fibres = seat ? 0 : .028f * MathF.Sin(7 * angle + 5 * t + frame.Seed);
                var offset = frame.Across(frame.Seed + 2.2f, .10f * radius * taper);
                var (bones, weights) = Blend([(AsVertex(front), 1 - t), (AsVertex(back), t)]);
                var (colour, relief) = StockWall(palette, frame.Region, mechanical, t, angle, side,
                    lateral, frame.Seed, f1, f2);
                float width = widths[j] + tear + fibres - relief;
                if (!mechanical && !seat)
                {
                    float mouth = MathF.Max(0, 1 - 4.5f * MathF.Min(t, 1 - t));
                    float fold = MathF.Sin(2 * angle + frame.Seed + (t < .5f ? .2f : 1.8f));
                    float lip = mouth * (.075f * fold + .032f * f2);
                    width = MathF.Min(.98f, width + lip + .035f * taper);
                }
                grid[k, j] = new()
                {
                    Position = frame.Centre + frame.Axis * depth + side * (radius * width) + offset,
                    Uv = new(t, k / (float)Around), Colour = colour,
                    Bones = bones, Weights = weights,
                };
            }
        }

        for (int k = 0; k < Around; k++)
        {
            int next = (k + 1) % Around, previous = (k + Around - 1) % Around;
            for (int j = 0; j < rows; j++)
            {
                var around = grid[next, j].Position - grid[previous, j].Position;
                var along = grid[k, Math.Min(j + 1, rows - 1)].Position - grid[k, Math.Max(j - 1, 0)].Position;
                var normal = Vector3.Cross(around, along);
                grid[k, j].Normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal)
                    : -Vector3.Normalize(frame.Across(k * MathF.Tau / Around, 1));
                grid[k, j].Tangent = new(Vector3.Normalize(around), 1);
            }
        }
        var vertices = new List<Vertex>(Around * (rows - 1) * 6);
        for (int k = 0; k < Around; k++)
        {
            int next = (k + 1) % Around;
            for (int j = 0; j + 1 < rows; j++)
            {
                var a = grid[k, j]; var b = grid[next, j]; var c = grid[k, j + 1]; var d = grid[next, j + 1];
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                vertices.Add(b); vertices.Add(d); vertices.Add(c);
            }
        }
        if (!mechanical) AddStockInterior(vertices, grid, frame, palette);
        var direction = frame.Axis;
        exit = Rim(rows - 1, direction);
        entry = Rim(0, -direction);
        // The stock item material reads packed byte colours. Float4 is reserved for the integrated custom shader.
        return MeshCut.FromTriangles(vertices, skeleton);

        Vertex[] Rim(int row, Vector3 normal) => [grid[0, row] with { Normal = normal },
            grid[Around / 3, row] with { Normal = normal }, grid[Around * 2 / 3, row] with { Normal = normal }];
    }

    // The wall has skinned cross-sections on both sides. Root the remaining tissue in those sections,
    // leaving the middle of the tunnel open so both entrances still read as a perforation.
    static void AddStockInterior(List<Vertex> output, Vertex[,] wall, ChestHoleFrame frame, StockTissuePalette palette)
    {
        var (major, minor) = frame.Basis();
        var lateral = Vector3.Normalize(Vector3.Cross(frame.Axis, Vector3.UnitY));
        var up = Vector3.UnitY;
        float tilt = .12f * MathF.Sin(frame.Seed * .73f);
        if (frame.Region == ChestHoleState.Abdomen)
        {
            var fold = Vector4.Lerp(palette.Organ(5), palette.Organ(4), .16f);
            Add(lateral - up * (.46f + tilt), 5, .41f, .29f, .25f, fold, bone: false);
            Add(-lateral - up * (.62f - tilt), 7, .37f, .31f, .21f, fold, bone: false);
            Add(-up - lateral * .20f, 5, .27f, .27f, .28f, palette.Organ(4), bone: false);
            return;
        }

        var ivory = Vector4.Lerp(palette.Bone, Vector4.One, .23f);
        Add(lateral - up * (.20f + tilt), 4, .52f, .17f, .12f, ivory, bone: true);
        Add(-lateral + up * (.16f - tilt), 8, .49f, .16f, .13f, ivory, bone: true);
        Add(lateral + up * .43f, 8, .33f, .14f, .11f, ivory, bone: true);
        var lung = Vector4.Lerp(palette.Organ(2), palette.Organ(6), .18f);
        Add(-lateral - up * .69f, 5, .36f, .29f, .27f, lung, bone: false);
        Add(lateral - up * .72f, 7, .31f, .29f, .23f, lung, bone: false);

        void Add(Vector3 wallDirection, int station, float reach, float width, float depth, Vector4 colour, bool bone)
        {
            float angle = MathF.Atan2(Vector3.Dot(wallDirection, minor) * frame.Aspect,
                Vector3.Dot(wallDirection, major) / frame.Aspect);
            int column = ((int)MathF.Round(angle * Around / MathF.Tau) + Around) % Around;
            var anchor = wall[column, station];
            float along = Vector3.Dot(anchor.Position - frame.Centre, frame.Axis);
            var radial = anchor.Position - frame.Centre - frame.Axis * along;
            float radius = radial.Length();
            if (radius < .035f) return;
            var inward = -radial / radius;
            var tangent = Vector3.Cross(frame.Axis, -inward);
            // Cross-sections stay parallel to the local tunnel wall. The thinnest part of a
            // shallow torso controls depth so tips do not emerge in front of the skin.
            float front = Vector3.Dot(anchor.Position - wall[column, 0].Position, frame.Axis);
            float back = Vector3.Dot(wall[column, wall.GetLength(1) - 1].Position - anchor.Position, frame.Axis);
            float axial = MathF.Min(radius * depth, .42f * MathF.Min(front, back));
            if (axial < .008f) return;
            float breadth = radius * width;
            float curve = radius * (bone ? .13f : -.19f);
            const int sides = 7, rows = 3;
            Span<Vertex> rings = stackalloc Vertex[rows * sides];
            for (int row = 0; row < rows; row++)
            {
                float t = row * .5f;
                var centre = Centre(t);
                float size = row == 1 ? 1 : row == 0 ? .84f : bone ? .62f : .45f;
                for (int point = 0; point < sides; point++)
                {
                    float turn = point * MathF.Tau / sides;
                    float c = MathF.Cos(turn), s = MathF.Sin(turn);
                    float uneven = .94f + .13f * MathF.Sin(3 * turn + frame.Seed + station * .8f);
                    var direction = Vector3.Normalize(tangent * c + frame.Axis * s + inward * (.37f * t));
                    var position = centre + (tangent * (breadth * c) + frame.Axis * (axial * s)) * (size * uneven);
                    if (bone && row == rows - 1) position += inward * (radius * .045f * MathF.Sin(3 * turn + station));
                    rings[row * sides + point] = Shaped(position, direction, colour * (.74f + .16f * t + .10f * uneven));
                }
            }

            for (int point = 0; point < sides; point++)
            {
                int next = (point + 1) % sides;
                for (int row = 0; row < rows - 1; row++)
                {
                    var a = rings[row * sides + point]; var b = rings[row * sides + next];
                    var c = rings[(row + 1) * sides + point]; var d = rings[(row + 1) * sides + next];
                    output.Add(a); output.Add(c); output.Add(b);
                    output.Add(b); output.Add(c); output.Add(d);
                }
                var end = rings[(rows - 1) * sides + point];
                var following = rings[(rows - 1) * sides + next];
                if (bone) { end.Normal = inward; following.Normal = inward; }
                var tip = Shaped(Centre(1) + inward * (radius * (bone ? .012f : .045f)), inward,
                    bone ? Vector4.Lerp(colour, palette.Marrow, .33f) : colour);
                output.Add(end); output.Add(tip); output.Add(following);
            }

            Vector3 Centre(float t) => anchor.Position + inward * (radius * (reach * t - .075f * (1 - t)))
                + tangent * (curve * t * t) + frame.Axis * (axial * .28f * t);

            Vertex Shaped(Vector3 position, Vector3 normal, Vector4 tint) => anchor with
            {
                Position = position, Normal = normal, Tangent = new(tangent, 1), Colour = tint with { W = 1 },
            };
        }
    }

    static bool FillStockGaps((Crossing Entry, Crossing Exit)?[] sides, in ChestHoleFrame frame,
        float near, float far, float span, float[]? profile)
    {
        if (sides.Count(side => side is null) > Around / 4) return false;
        var measured = sides.ToArray();
        for (int k = 0; k < Around; k++)
        {
            if (measured[k] is not null) continue;
            int before = 1, after = 1;
            while (measured[(k + Around - before) % Around] is null) before++;
            while (measured[(k + after) % Around] is null) after++;
            var a = measured[(k + Around - before) % Around]!.Value;
            var b = measured[(k + after) % Around]!.Value;
            float t = before / (float)(before + after);
            var side = frame.Across(k * MathF.Tau / Around, profile?[k] ?? 1);
            var centre = frame.Centre;
            var axis = frame.Axis;
            sides[k] = (Interpolate(a.Entry, b.Entry), Interpolate(a.Exit, b.Exit));

            Crossing Interpolate(Crossing from, Crossing to)
            {
                float depth = Vector3.Dot(Vector3.Lerp(from.Position, to.Position, t) - centre, axis);
                float radius = (near + far) * .5f + (far - near) * depth / (2 * span);
                var (bones, weights) = Blend([(AsVertex(from), 1 - t), (AsVertex(to), t)]);
                return new(centre + axis * depth + side * radius, bones, weights);
            }
        }
        return true;
    }

    static (Vector4 Colour, float Relief) StockWall(StockTissuePalette palette, int region, bool mechanical,
        float t, float angle, Vector3 side, Vector3 lateral, float seed, float tear, float fibre)
    {
        float edge = 2 * MathF.Min(t, 1 - t);
        float inside = Math.Clamp((edge - .10f) * 4, 0, 1);
        if (mechanical)
        {
            var steel = new Vector4(.40f, .43f, .45f, 1);
            var dark = new Vector4(.09f, .12f, .14f, 1);
            var copper = new Vector4(.48f, .27f, .12f, 1);
            var circuit = new Vector4(.16f, .32f, .29f, 1);
            float rail = Crest(MathF.Cos(angle * 6 + seed), .36f);
            float seam = Crest(MathF.Cos(t * 7 * MathF.Tau), .32f);
            var panel = Vector4.Lerp(dark, circuit, .22f + .20f * tear);
            panel = Vector4.Lerp(panel, copper, rail * (.68f + .18f * fibre));
            panel = Vector4.Lerp(panel, steel, seam * (.25f + .29f * rail));
            return (Vector4.Lerp(steel, panel, inside), inside * (.047f * rail + .018f * seam));
        }

        float shoulder = Crest(MathF.Sin(angle * 3 + seed + t * 2), -.38f);
        var tornEdge = Vector4.Lerp(palette.Flesh, palette.Wet, .14f + .18f * tear) * (.40f + .21f * shoulder);
        var muscle = palette.Muscle * (.62f + .16f * fibre);
        var lip = Vector4.Lerp(tornEdge, muscle, Math.Clamp(edge * 6.2f, 0, 1));
        float strands = Crest(MathF.Sin(angle * 7 + seed) * MathF.Cos(angle * 2 - seed), .12f);
        float fatBand = MathF.Max(0, 1 - MathF.Abs(edge - (.14f + .07f * tear)) * 6.5f);
        lip = Vector4.Lerp(lip, palette.Fat, strands * fatBand * .48f);
        float shade = (1 - .42f * MathF.Pow(edge, .78f)) * (.92f + .08f * fibre);
        if (region == ChestHoleState.Abdomen)
        {
            float fold = Crest(MathF.Cos(angle * 4 + MathF.Sin(t * 13 + seed) * 1.8f + t * 11), -.38f);
            float fat = Crest(MathF.Cos(angle * 3 - seed + t * 4), .32f) * (1 - Math.Clamp((edge - .27f) * 3, 0, 1));
            var gut = Vector4.Lerp(palette.Organ(5), palette.Organ(4), .30f + fold * .58f);
            gut = Vector4.Lerp(gut, palette.Fat, fat * .70f);
            return ((Vector4.Lerp(lip, gut, inside) * shade) with { W = 1 }, inside * (.078f * fold + .027f * fat));
        }

        // Bone splinters lie across the side walls. The dark, lobed lung sits deeper between them.
        float sideWall = Crest(MathF.Abs(Vector3.Dot(Vector3.Normalize(side), lateral)), .48f);
        float nearRib = 1 - Math.Clamp(MathF.Abs(edge - (.42f + .085f * tear)) * 7, 0, 1);
        float farRib = 1 - Math.Clamp(MathF.Abs(edge - (.66f + .06f * fibre)) * 9, 0, 1);
        float rib = sideWall * MathF.Max(nearRib, .70f * farRib) * (.72f + .27f * fibre);
        float lobe = Crest(MathF.Sin(angle * 3 + t * 7 + seed) * MathF.Cos(t * 8 - angle * 2), -.45f);
        var lung = Vector4.Lerp(palette.Organ(2), palette.Organ(6), .65f - .49f * lobe);
        var chest = (Vector4.Lerp(lip, lung, inside) * shade) with { W = 1 };
        return (Vector4.Lerp(chest, palette.Bone, rib * .89f), inside * (.069f * rib + .050f * lobe));

        static float Crest(float wave, float cutoff) => Math.Clamp((wave - cutoff) / (1 - cutoff), 0, 1);
    }
#endif

    static OgreMesh? Lining(List<Face> faces, in ChestHoleFrame frame, Vector4 colour,
        string? skeleton, out Vertex[] exit)
    {
        exit = [];
        var ends = new (Crossing Entry, Crossing Exit)?[Around];
        for (int k = 0; k < Around; k++) ends[k] = Ends(faces, frame, k * MathF.Tau / Around);
        if (!FillGaps(ends)) return null;
#if BRUTALITY_STOCK_MASTER
        var direction = frame.Axis;
        exit = new[] { 0, Around / 3, Around * 2 / 3 }.Select(index =>
        {
            var sample = ends[index]!.Value.Exit;
            return new Vertex { Position = sample.Position, Normal = direction, Bones = sample.Bones, Weights = sample.Weights };
        }).ToArray();
#endif

        var vertices = new List<Vertex>(Around * (Along - 1) * 6);
        for (int k = 0; k < Around; k++)
            for (int j = 0; j + 1 < Along; j++)
                Quad(vertices, ends, frame, k, j, colour);
        return MeshCut.FromTriangles(vertices, skeleton, preserveRawVertexChannels: true);
    }

    /// <summary>
    /// A first line at the entry's radius finds the skin's depth there; the exit then sits on the lining radius of its own depth, so a
    /// flared exit stays outside its wider edge.
    /// </summary>
    static (Crossing, Crossing)? Ends(List<Face> faces, in ChestHoleFrame frame, float turn)
    {
        float entryRadius = frame.LiningRadius(-2 * frame.Radius);
        if (Cross(faces, frame.Centre + frame.Across(turn, entryRadius), frame.Axis) is not { } first) return null;
        float exitRadius = frame.LiningRadius(Vector3.Dot(first.Item2.Position - frame.Centre, frame.Axis));
        if (exitRadius - entryRadius < 1e-3f) return first;
        return Cross(faces, frame.Centre + frame.Across(turn, exitRadius), frame.Axis) is { } wide ? (first.Item1, wide.Item2) : first;
    }

    /// <summary>
    /// The body's torso triangles with the look's morphs applied; limbs hang beside the chest in the bind pose and must not stop a ray.
    /// </summary>
    static List<Face> TorsoFaces(Vertex[] body, Vector4[]? morphs, SkeletonRig rig)
    {
        var groups = MeshCut.BoneGroups(rig);
        var faces = new List<Face>(body.Length / 6);
        for (int i = 0; i + 2 < body.Length; i += 3)
        {
#if BRUTALITY_STOCK_MASTER
            if (MeshCut.TriangleGroup(body[i], body[i + 1], body[i + 2], groups) != 0) continue;
#else
            if (!Torso(body[i], groups) || !Torso(body[i + 1], groups) || !Torso(body[i + 2], groups)) continue;
#endif
            faces.Add(new(Morphed(body[i], morphs), Morphed(body[i + 1], morphs), Morphed(body[i + 2], morphs), body[i], body[i + 1], body[i + 2]));
        }
        return faces;
    }

    static bool Torso(in Vertex vertex, int[] groups)
    {
        int at = 0;
        for (int k = 1; k < 4; k++) if (vertex.Weights[k] > vertex.Weights[at]) at = k;
        int bone = (int)(vertex.Bones >> (at * 8) & 255);
        return bone < groups.Length && groups[bone] == 0;
    }

    static Vector3 Morphed(in Vertex vertex, Vector4[]? morphs)
    {
        uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
        return morphs is not null && slot > 0 && slot <= morphs.Length
            ? vertex.Position + new Vector3(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : vertex.Position;
    }

    /// <summary>The first and last skin crossings of the line through <paramref name="origin"/> along the axis: entry and exit.</summary>
    static (Crossing, Crossing)? Cross(List<Face> faces, Vector3 origin, Vector3 axis, float inset = Inset)
    {
        float first = float.MaxValue, last = float.MinValue;
        Crossing entry = default, exit = default;
        foreach (var face in faces)
        {
            if (!Intersect(origin, axis, face, out float distance, out var bary)) continue;
            if (distance < first) { first = distance; entry = At(face, bary, origin + axis * (distance + inset)); }
            if (distance > last) { last = distance; exit = At(face, bary, origin + axis * (distance - inset)); }
        }
        return last - first > 4 * Inset ? (entry, exit) : null;
    }

    /// <summary>Möller–Trumbore on the whole line: the distance may be negative.</summary>
    static bool Intersect(Vector3 origin, Vector3 direction, in Face face, out float distance, out Vector3 bary)
    {
        distance = 0;
        bary = default;
        var e1 = face.B - face.A;
        var e2 = face.C - face.A;
        var p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;

        float inverse = 1 / det;
        var s = origin - face.A;
        float u = Vector3.Dot(s, p) * inverse;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1) return false;

        distance = Vector3.Dot(e2, q) * inverse;
        bary = new(1 - u - v, u, v);
        return true;
    }

    static Crossing At(in Face face, Vector3 bary, Vector3 position)
    {
        var (bones, weights) = Blend([(face.VA, bary.X), (face.VB, bary.Y), (face.VC, bary.Z)]);
        return new(position, bones, weights);
    }

    /// <summary>The four heaviest bones of a weighted mix of skin vertices, renormalized.</summary>
    static (uint Bones, Vector4 Weights) Blend(ReadOnlySpan<(Vertex Vertex, float Share)> sources)
    {
        var mix = new Dictionary<int, float>();
        foreach (var (vertex, share) in sources)
            for (int k = 0; k < 4; k++)
            {
                float weight = vertex.Weights[k] * share;
                if (weight <= 0) continue;
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                mix[bone] = mix.GetValueOrDefault(bone) + weight;
            }
        var top = mix.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(4).ToArray();
        float total = top.Sum(pair => pair.Value);
        if (top.Length == 0 || total <= 0) return (0, Vector4.UnitX);

        uint bones = 0;
        var weights = new float[4];
        for (int i = 0; i < top.Length; i++) { bones |= (uint)top[i].Key << (i * 8); weights[i] = top[i].Value / total; }
        return (bones, new(weights[0], weights[1], weights[2], weights[3]));
    }

    /// <summary>A ring that missed the skin takes its nearest found neighbours' ends. Too many misses: no lining.</summary>
    static bool FillGaps((Crossing Entry, Crossing Exit)?[] ends)
    {
        int missing = ends.Count(end => end is null);
        if (missing > Around / 4) return false;
        for (int k = 0; k < Around; k++)
        {
            if (ends[k] is not null) continue;
            for (int step = 1; step < Around && ends[k] is null; step++)
                ends[k] = ends[(k + step) % Around] ?? ends[(k - step + Around) % Around];
        }
        return true;
    }

    static void Quad(List<Vertex> output, (Crossing Entry, Crossing Exit)?[] ends, in ChestHoleFrame frame, int k, int j, Vector4 colour)
    {
        int next = (k + 1) % Around;
        var a = Point(ends, frame, k, j, colour);
        var b = Point(ends, frame, next, j, colour);
        var c = Point(ends, frame, k, j + 1, colour);
        var d = Point(ends, frame, next, j + 1, colour);
        // cross(b - a, c - a) points at the axis, as the vertex normals do: the wall's front faces the tunnel.
        output.Add(a); output.Add(b); output.Add(c);
        output.Add(b); output.Add(d); output.Add(c);
    }

    /// <summary>Ring <paramref name="j"/> of column <paramref name="k"/>, between its entry and exit; the wall faces the axis.</summary>
    static Vertex Point((Crossing Entry, Crossing Exit)?[] ends, in ChestHoleFrame frame, int k, int j, Vector4 colour)
    {
        var (entry, exit) = ends[k]!.Value;
        float along = j / (float)(Along - 1);
        var position = Vector3.Lerp(entry.Position, exit.Position, along);
        var across = position - frame.Centre;
        across -= frame.Axis * Vector3.Dot(across, frame.Axis);
        var (bones, weights) = Blend([(AsVertex(entry), 1 - along), (AsVertex(exit), along)]);
        float grain = .9f + .1f * MathF.Sin(position.X * 31 + position.Y * 17 + position.Z * 23);
        return new Vertex
        {
            Position = position, Normal = -Vector3.Normalize(across), Uv = new(along, k / (float)Around),
            Tangent = new(position, LayerTag), Colour = (colour * grain) with { W = colour.W },
            Bones = bones, Weights = weights,
        };
    }

    static Vertex AsVertex(in Crossing crossing) => new() { Bones = crossing.Bones, Weights = crossing.Weights };
}
