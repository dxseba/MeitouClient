using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Opens the coverage of a texture-only garment without cutting the character mesh.</summary>
internal static class StockVestTears
{
    const float Marker = -6193;
    const string VestSignature = "void vest(uint d, uint nm, uint c, bool coloured, vec3 shirt, vec2 uv, inout vec3 albedo, inout vec4 normalMap, inout float gloss, inout float skin)";
    const string VestBlend = "albedo = mix(albedo, v.rgb, vn.a);";
    const string VestCoverage = """
        float brutalityVestCoverage(vec2 uv, vec4 tear)
        {
            float length2 = dot(tear.zw, tear.zw);
            if (length2 < 1e-8) return 1.0;
            vec2 delta = uv - tear.xy;
            float along = dot(delta, tear.zw) / length2;
            float across = dot(delta, vec2(-tear.w, tear.z)) / length2;
            float weave = sin(along * 29.0 + across * 21.0) * sin(along * 61.0 - across * 17.0);
            float reach = sqrt(max(0.0, 1.0 - along * along));
            float width = (tear.z < 0.0 ? 0.40 : 0.17) * reach * (1.0 + 0.29 * weave);
            float edge = max(abs(along) - 1.0, abs(across + 0.035 * sin(along * 11.0)) - width);
            float feather = clamp(fwidth(edge) * 0.6, 0.009, 0.06);
            return smoothstep(-feather, feather, edge);
        }
        """;
    static readonly string[] calls =
    [
        "vest(m.tex[9], m.tex[10], m.tex[11], flag(f, 2048u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);",
        "vest(m.tex[12], m.tex[13], m.tex[14], flag(f, 4096u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);",
        "vest(m.tex[15], m.tex[16], m.tex[17], flag(f, 8192u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);",
    ];
    static readonly string[] channels =
    [
        "m.diffuseChannel",
        "m.alphaChannel",
        "vec4(m.colour1.w, m.colour2.zw, m.shirtColour.w)",
    ];
    static readonly ConditionalWeakTable<CharacterAppearance, Payload> looks = new();
    static MaterialFields? fields;
    static volatile bool enabled;

    internal sealed record Payload(Vector4 First, Vector4 Second, Vector4 Third);
    sealed record MaterialFields(FieldInfo Threshold, FieldInfo Diffuse, FieldInfo Alpha, FieldInfo Colour1,
        FieldInfo Colour2, FieldInfo Gloss);
    readonly record struct Triangle(Vertex A, Vertex B, Vertex C, int Group)
    {
        public Vector3 Low { get; } = Vector3.Min(A.Position, Vector3.Min(B.Position, C.Position));
        public Vector3 High { get; } = Vector3.Max(A.Position, Vector3.Max(B.Position, C.Position));
    }

    internal static void Install(ModContext context)
    {
        if (enabled) return;
        try
        {
            var assembly = typeof(Model).Assembly;
            var shaders = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterShaders", throwOnError: true)!;
            var content = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterContent", throwOnError: true)!;
            var fragment = AccessTools.Method(shaders, "Fragment", [typeof(bool)]);
            var bodyMaterial = AccessTools.Method(content, "BodyMaterial", [typeof(CharacterAppearance)]);
            if (bodyMaterial is null || fragment?.ReturnType != typeof(string)
                || fragment.Invoke(null, [false]) is not string early || !TryPatchShader(early, out _)
                || fragment.Invoke(null, [true]) is not string late || !TryPatchShader(late, out _))
            {
                context.Log("Brutality texture-only garment tears unavailable for this stock shader.");
                return;
            }

            var material = bodyMaterial.ReturnType;
            fields = new MaterialFields(Field("AlphaThreshold", typeof(float)), Field("DiffuseChannel", typeof(Vector4)),
                Field("AlphaChannel", typeof(Vector4)), Field("Colour1", typeof(Vector4)), Field("Colour2", typeof(Vector4)),
                Field("GlossMult", typeof(float)));
            context.Harmony.Patch(fragment, postfix: new HarmonyMethod(typeof(StockVestTears), nameof(FragmentPostfix)));
            context.Harmony.Patch(bodyMaterial, postfix: new HarmonyMethod(typeof(StockVestTears), nameof(BodyMaterialPostfix)));
            enabled = true;

            FieldInfo Field(string name, Type type) => AccessTools.Field(material, name) is { } field && field.FieldType == type
                ? field : throw new MissingFieldException(material.Name, name);
        }
        catch (Exception error)
        {
            context.Log($"Brutality texture-only garment tears unavailable: {error.GetBaseException().Message}");
        }
    }

    internal static void Mark(CharacterAppearance look, Payload? payload)
    {
        if (enabled && payload is not null) looks.AddOrUpdate(look, payload);
    }

    static void BodyMaterialPostfix(CharacterAppearance __0, object __result)
    {
        if (fields is not { } material || !looks.TryGetValue(__0, out var payload)) return;
        // Only ShadeBody receives this payload. These channels are unused by body, hair-overlay and blood sampling.
        material.Threshold.SetValue(__result, Marker);
        material.Diffuse.SetValue(__result, payload.First);
        material.Alpha.SetValue(__result, payload.Second);
        var colour1 = (Vector4)material.Colour1.GetValue(__result)!;
        var colour2 = (Vector4)material.Colour2.GetValue(__result)!;
        material.Colour1.SetValue(__result, colour1 with { W = payload.Third.X });
        material.Colour2.SetValue(__result, colour2 with { Z = payload.Third.Y, W = payload.Third.Z });
        material.Gloss.SetValue(__result, payload.Third.W);
    }

    static void FragmentPostfix(ref string __result)
    {
        if (TryPatchShader(__result, out string patched)) __result = patched;
    }

    static bool TryPatchShader(string source, out string patched)
    {
        patched = source;
        if (source.Contains("brutalityVestCoverage", StringComparison.Ordinal)
            || !Unique(VestSignature) || !Unique(VestBlend) || !calls.All(Unique)) return false;
        patched = source.Replace(VestSignature, VestCoverage + "\n" + VestSignature.Replace("vec2 uv,", "vec2 uv, vec4 tear,"), StringComparison.Ordinal)
            .Replace(VestBlend, "vn.a *= brutalityVestCoverage(uv, tear);\n" + VestBlend, StringComparison.Ordinal);
        for (int i = 0; i < calls.Length; i++)
            patched = patched.Replace(calls[i], calls[i].Replace("uv, albedo",
                $"uv, m.alphaThreshold == -6193.0 ? {channels[i]} : vec4(0.0), albedo"), StringComparison.Ordinal);
        return true;

        bool Unique(string text)
        {
            int first = source.IndexOf(text, StringComparison.Ordinal);
            return first >= 0 && source.IndexOf(text, first + text.Length, StringComparison.Ordinal) < 0;
        }
    }

    internal static Payload? Build(OgreMesh skin, SkeletonRig rig, CharacterAppearance look, GarmentDamageActor garments)
    {
        int count = Math.Min(3, look.BodyLayers.Count);
        if (!enabled || count == 0) return null;
        var strongest = new GarmentDamageMark?[count];
        foreach (var item in garments.Items)
        {
            if (item.VestSlots is not { Length: > 0 } slots) continue;
            foreach (var mark in item.Marks)
            {
                var wound = mark.Entry.Mark;
                if (WoundSegments.IsHead(wound.Segment) || WoundSegments.IsHand(wound.Segment)
                    || wound.Shape.Kind is not (WoundKind.Slash or WoundKind.Thrust)
                    || !(mark.Material == GarmentMaterial.Cloth || mark.Material == GarmentMaterial.Leather && mark.Severity >= .25f)) continue;
                foreach (int slot in slots)
                    if ((uint)slot < count && look.BodyLayers[slot].Record.StringId == item.Record
                        && (strongest[slot] is not { } previous || mark.Severity > previous.Severity
                            || mark.Severity == previous.Severity && wound.Age > previous.Entry.Mark.Age)) strongest[slot] = mark;
            }
        }
        if (strongest.All(mark => mark is null)) return null;
        var surface = SkinTriangles(skin, rig, look);
        Span<Vector4> tears = stackalloc Vector4[3];
        tears.Clear();
        bool any = false;
        for (int i = 0; i < count; i++)
            if (strongest[i] is { } mark && Project(surface, rig, mark) is { } tear)
            {
                tears[i] = tear;
                any = true;
            }
        return any ? new(tears[0], tears[1], tears[2]) : null;
    }

    static List<Triangle> SkinTriangles(OgreMesh skin, SkeletonRig rig, CharacterAppearance look)
    {
        var triangles = new List<Triangle>();
        var groups = MeshCut.BoneGroups(rig);
        bool shaved = look.Character?.GetBool("shaved") == true;
        foreach (var part in MeshCut.SourceModel(skin, rig.BoneCount).Parts)
        {
            var vertices = part.Vertices;
            var morphs = MeshCut.MorphDeltas(skin, part.SubMeshIndex, vertices.Length, look.PoseWeights, shaved);
            var indices = part.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                if (ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length) continue;
                var a = vertices[ia] with { Position = vertices[ia].Position + morphs[ia] };
                var b = vertices[ib] with { Position = vertices[ib].Position + morphs[ib] };
                var c = vertices[ic] with { Position = vertices[ic].Position + morphs[ic] };
                if (a.Uv.Y < 0 || b.Uv.Y < 0 || c.Uv.Y < 0
                    || Vector3.Cross(b.Position - a.Position, c.Position - a.Position).LengthSquared() <= 1e-12f) continue;
                triangles.Add(new(a, b, c, MeshCut.TriangleGroup(a, b, c, groups)));
            }
        }
        return triangles;
    }

    static Vector4? Project(List<Triangle> surface, SkeletonRig rig, GarmentDamageMark damage)
    {
        var entry = damage.Entry;
        var wound = entry.Mark;
        bool torso = entry.Region >= 0 || wound.Segment == 9;
        var side = Unit(wound.Side, Vector3.UnitZ);
        float at = wound.T;
        if (entry.Region >= 0)
        {
            float off = wound.Age * 37 % 7 / 3f - 1;
            at = (entry.Region == 0 ? .7f : entry.Region == 1 ? .35f : .5f) + off * .13f;
            side = Unit(new(off * .95f, 0, entry.Region == 2 ? -1 : 1), Vector3.UnitZ);
        }
        string prefix = wound.Segment < 2 || wound.Segment is 4 or 5 ? "Bip01 L " : "Bip01 R ";
        bool lower = (wound.Segment & 1) != 0;
        var (from, to) = torso ? ("Bip01 Pelvis", "Bip01 Neck")
            : wound.Segment < 4 ? lower ? (prefix + "Forearm", prefix + "Hand") : (prefix + "UpperArm", prefix + "Forearm")
            : lower ? (prefix + "Calf", prefix + "Foot") : (prefix + "Thigh", prefix + "Calf");
        if (rig.Handle(from) is not int first || rig.Handle(to) is not int second) return null;
        int group = torso ? 0 : wound.Segment / 2 + 1;
        var segment = rig.BindPosition[second] - rig.BindPosition[first];
        var target = rig.BindPosition[first] + at * segment + side * 1.2f;
        float best = 4;
        Vector4? result = null;
        foreach (var triangle in surface)
        {
            if (triangle.Group != group || Vector3.DistanceSquared(target, Vector3.Clamp(target, triangle.Low, triangle.High)) >= best) continue;
            var (a, b, c, _) = triangle;
            var bary = RagdollSkinSurface.ClosestBarycentric(target, a.Position, b.Position, c.Position);
            var normal = Unit(a.Normal * bary.X + b.Normal * bary.Y + c.Normal * bary.Z, side);
            if (Vector3.Dot(side, normal) < .18f) continue;
            var point = a.Position * bary.X + b.Position * bary.Y + c.Position * bary.Z;
            float distance = Vector3.DistanceSquared(target, point);
            if (!(distance < best)) continue;
            var vertical = Unit(segment - normal * Vector3.Dot(segment, normal), Vector3.UnitY);
            var across = Unit(Vector3.Cross(vertical, normal), Vector3.UnitX);
            float turn = WoundPolicy.VisualTurn(wound);
            float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
            var along = Unit(across * (wound.Shape.Cosine * cos - wound.Shape.Sine * sin)
                + vertical * (wound.Shape.Cosine * sin + wound.Shape.Sine * cos), across);
            float size = (.3f + .4f * wound.Shape.Depth) * wound.Size * (.55f + 1.1f * damage.Severity);
            bool thrust = wound.Shape.Kind == WoundKind.Thrust;
            size = Math.Clamp(size, .13f, 1.35f) * (thrust ? .64f : 1f) * (damage.Material == GarmentMaterial.Leather ? .72f : 1f);
            if (UvAxis(a, b, c, along * size) is not { } axis) continue;
            // The sign of the (unoriented) long axis encodes a wider thrust instead of an elongated slash.
            if ((axis.X < 0) != thrust) axis = -axis;
            if (MathF.Abs(axis.X) < 1e-6f) axis.X = thrust ? -1e-6f : 1e-6f;
            var uv = a.Uv * bary.X + b.Uv * bary.Y + c.Uv * bary.Z;
            result = new(uv.X, uv.Y, axis.X, axis.Y);
            best = distance;
        }
        return result;
    }

    static Vector2? UvAxis(in Vertex a, in Vertex b, in Vertex c, Vector3 offset)
    {
        var e1 = b.Position - a.Position;
        var e2 = c.Position - a.Position;
        float xx = Vector3.Dot(e1, e1), xy = Vector3.Dot(e1, e2), yy = Vector3.Dot(e2, e2);
        float determinant = xx * yy - xy * xy;
        if (determinant <= 1e-10f) return null;
        float x = Vector3.Dot(e1, offset), y = Vector3.Dot(e2, offset);
        var axis = (b.Uv - a.Uv) * ((yy * x - xy * y) / determinant)
            + (c.Uv - a.Uv) * ((xx * y - xy * x) / determinant);
        float length = axis.Length();
        return float.IsFinite(length) && length >= .0005f ? axis * (MathF.Min(.08f, length) / length) : null;
    }

    static Vector3 Unit(Vector3 vector, Vector3 fallback) =>
        vector.LengthSquared() > 1e-10f ? Vector3.Normalize(vector) : fallback;
}
