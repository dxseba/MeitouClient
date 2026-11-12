using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Local, textured discoloration on the existing skin triangles.</summary>
internal static class StockBruiseGeometry
{
    const int MaxMarks = 6;
    const float Marker = -6191;
    const string GlossAnchor = "if (flag(f, 16384u) && flag(f, 1024u)) metal = cm.b;";
    const string OpacityScope = "bool cut = false;";
    const string AlphaAnchor = "fragColour = vec4(colour, 1.0);";
    const string MaterialAnchor = "CharMaterial m = materials.items[vMaterial];";
    const string BruiseShading = """

        if (!icon && flag(f, 524288u) && m.skinTone.x == -6191.0)
        {
            float shape = length(uv);
            float stain = 0.58 * sin(uv.x * 12.7 + uv.y * 8.8)
                        + 0.42 * sin(uv.x * 8.0 - uv.y * 15.3 + sin(uv.y * 12.1));
            float edge = shape + 0.095 * stain;
            float feather = 1.0 - smoothstep(0.38, 1.05, edge);
            float coverage = feather * vColour.a * (0.86 + 0.14 * stain);
            if (coverage <= 0.001) discard;
            bruiseOpacity = clamp(coverage, 0.0, 1.0);
            albedo *= mix(0.74, 1.13, clamp(edge + stain * 0.14, 0.0, 1.0));
            gloss = 0.15;
            metal = 0.0;
        }
        """;
    static readonly BlendState SkinBlend = new(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
        AlphaSrc: BlendingFactor.One, AlphaDst: BlendingFactor.OneMinusSrcAlpha);
    static readonly ConditionalWeakTable<object, object> bruiseParts = new();
    static readonly object bruisePart = new();
    static FieldInfo? skinTone;
    static FieldInfo? batchPart;
    static PropertyInfo? loadedGpu, gpuParts;
    static volatile bool enabled;

    readonly record struct SkinTriangle(Vertex A, Vertex B, Vertex C, int Group)
    {
        public Vector3 Low { get; } = Vector3.Min(A.Position, Vector3.Min(B.Position, C.Position));
        public Vector3 High { get; } = Vector3.Max(A.Position, Vector3.Max(B.Position, C.Position));
    }
    readonly record struct Placement(string From, string To, float Along, Vector3 Side, int Group);
    readonly record struct SkinPoint(Vector3 Position, Vector3 Normal);

    internal static void Install(ModContext context)
    {
        if (enabled) return;
        try
        {
            var assembly = typeof(Model).Assembly;
            var shaders = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterShaders", throwOnError: true)!;
            var material = assembly.GetType("Meitou.Game.Rendering.Characters.PartMaterial", throwOnError: true)!;
            var renderer = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer", throwOnError: true)!;
            var content = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterContent", throwOnError: true)!;
            var tone = AccessTools.Field(material, "SkinTone");
            var fragment = AccessTools.Method(shaders, "Fragment", [typeof(bool)]);
            var depth = AccessTools.Method(shaders, "DepthFragment", []);
            var motion = AccessTools.Method(shaders, "MotionFragment", []);
            var draws = AccessTools.Method(renderer, "RecordDraws");
            var batch = renderer.GetNestedType("Batch", BindingFlags.NonPublic);
            var loaded = content.GetNestedType("LoadedMesh", BindingFlags.NonPublic);
            batchPart = batch is null ? null : AccessTools.Field(batch, "Part");
            loadedGpu = loaded is null ? null : AccessTools.Property(loaded, "Gpu");
            gpuParts = loadedGpu is null ? null : AccessTools.Property(loadedGpu.PropertyType, "Parts");
            if (tone?.FieldType != typeof(Vector3) || fragment?.ReturnType != typeof(string)
                || draws is null || batchPart is null || loadedGpu is null || gpuParts is null
                || fragment.Invoke(null, [false]) is not string early || !HasColourAnchors(early)
                || fragment.Invoke(null, [true]) is not string late || !HasColourAnchors(late)
                || depth?.Invoke(null, null) is not string depthSource || !Unique(depthSource, MaterialAnchor)
                || motion?.Invoke(null, null) is not string motionSource || !Unique(motionSource, MaterialAnchor))
            {
                context.Log("Brutality bruises unavailable for this stock shader.");
                return;
            }

            context.Harmony.Patch(draws, transpiler: new HarmonyMethod(typeof(StockBruiseGeometry), nameof(UseBruisePipeline)));
            context.Harmony.Patch(fragment, postfix: new HarmonyMethod(typeof(StockBruiseGeometry), nameof(FragmentPostfix)));
            var hide = new HarmonyMethod(typeof(StockBruiseGeometry), nameof(DepthOrMotionPostfix));
            context.Harmony.Patch(depth, postfix: hide);
            context.Harmony.Patch(motion, postfix: hide);
            skinTone = tone;
            enabled = true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality bruises unavailable: {error.GetBaseException().Message}");
        }
    }

    internal static void Mark(object material)
    {
        if (enabled) skinTone!.SetValue(material, new Vector3(Marker, 0, 0));
    }

    internal static void MarkLoadedMesh(object loaded)
    {
        if (!enabled || loadedGpu?.GetValue(loaded) is not { } mesh || gpuParts?.GetValue(mesh) is not IEnumerable parts) return;
        foreach (var part in parts)
            if (part is not null) bruiseParts.GetValue(part, static _ => bruisePart);
    }

    static IEnumerable<CodeInstruction> UseBruisePipeline(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var part = code.FindIndex(instruction => instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, batchPart));
        var pipeline = AccessTools.Method(typeof(DrawState), nameof(DrawState.Pipeline));
        var calls = code.Where(instruction => instruction.Calls(pipeline)).ToArray();
        if (part < 0 || part + 1 >= code.Count || !code[part + 1].IsStloc() || calls.Length != 1)
            throw new InvalidOperationException("Character draw batches no longer expose their mesh and pipeline.");

        var load = CodeInstruction.LoadLocal(code[part + 1].LocalIndex());
        var call = calls[0];
        load.labels.AddRange(call.labels);
        call.labels.Clear();
        code.Insert(code.IndexOf(call), load);
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.Method(typeof(StockBruiseGeometry), nameof(PipelineFor));
        return code;
    }

    static GraphicsPipelineDesc PipelineFor(DrawState state, GpuProgram program, VertexLayout vertex,
        PrimitiveType topology, AttachmentFormats targets, string label, object part)
    {
        var pipeline = state.Pipeline(program, vertex, topology, targets, label);
        return label == "characters" && bruiseParts.TryGetValue(part, out _)
            ? pipeline with { Blend = SkinBlend }
            : pipeline;
    }

    static void FragmentPostfix(ref string __result)
    {
        if (!HasColourAnchors(__result)) return;
        int index = __result.IndexOf(GlossAnchor, StringComparison.Ordinal) + GlossAnchor.Length;
        __result = __result.Insert(index, BruiseShading)
            .Replace(OpacityScope, OpacityScope + "\n            float bruiseOpacity = 1.0;", StringComparison.Ordinal)
            .Replace(AlphaAnchor, "fragColour = vec4(colour, bruiseOpacity);", StringComparison.Ordinal);
    }

    static void DepthOrMotionPostfix(ref string __result)
    {
        if (Unique(__result, MaterialAnchor))
            __result = __result.Replace(MaterialAnchor, MaterialAnchor + "\n            if (m.skinTone.x == -6191.0) discard;", StringComparison.Ordinal);
    }

    static bool HasColourAnchors(string source) => Unique(source, GlossAnchor) && Unique(source, OpacityScope) && Unique(source, AlphaAnchor);

    static bool Unique(string source, string anchor)
    {
        int first = source.IndexOf(anchor, StringComparison.Ordinal);
        return first >= 0 && source.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal) < 0;
    }

    internal static OgreMesh? Build(OgreMesh body, SkeletonRig rig, CharacterAppearance look, BodyWoundState crushWounds)
    {
        if (!enabled || !crushWounds.HasAny || look.Race.GetBool("is robot") || body.SkeletonName is not { } skeleton) return null;
        var wounds = crushWounds.Entries.Where(entry => entry.Mark.Shape.Kind == WoundKind.Crush && entry.Mark.Dose >= .12f
                && (!look.Headless || entry.Mark.Segment != 8))
            .OrderByDescending(entry => entry.Mark.Age).Take(MaxMarks).ToArray();
        if (wounds.Length == 0) return null;

        var palette = StockTissuePalette.For(look);
        var pigment = palette.Tint(palette.Hiver ? new(.13f, .17f, .15f, 1) : new(.21f, .087f, .17f, 1), .32f);
        var skin = SkinTriangles(body, rig, look);
        var result = new List<Vertex>();
        // The native body is single-sided and draws first. Blend the farthest bruise before the nearer one.
        for (int i = wounds.Length - 1; i >= 0; i--)
        {
            var entry = wounds[i];
            var wound = entry.Mark;
            var placement = Place(entry);
            if (rig.Handle(placement.From) is not int from || rig.Handle(placement.To) is not int to) continue;
            Vector3 side = Unit(placement.Side, Vector3.UnitZ);
            Vector3 bone = rig.BindPosition[to] - rig.BindPosition[from];
            var target = rig.BindPosition[from] + bone * placement.Along + side * .8f;
            if (Closest(skin, target, side, placement.Group) is not { } surface) continue;
            Vector3 along = Unit(bone - surface.Normal * Vector3.Dot(bone, surface.Normal),
                Unit(Vector3.Cross(surface.Normal, Vector3.UnitX), Vector3.UnitY));
            Vector3 across = Vector3.Cross(along, surface.Normal);
            var axis = Unit(along * wound.Shape.Sine + across * wound.Shape.Cosine, along);
            var perpendicular = Vector3.Cross(surface.Normal, axis);
            float size = (.33f + .39f * wound.Shape.Depth) * wound.Size * (palette.Hiver ? .73f : 1f);
            if (placement.Group == 7) size *= .76f;
            float width = size * (.68f + .28f * wound.Shape.Aspect);
            float strength = Math.Clamp(.16f + wound.Shape.Depth * .6f, 0, .76f);
            float offset = .004f + (wounds.Length - i) * .0003f;
            float reach = size * 1.28f;
            float reachSquared = reach * reach;
            foreach (var triangle in skin)
            {
                if (!Matches(triangle.Group, placement.Group) || !Near(triangle, surface.Position, reachSquared)
                    || Vector3.Dot(triangle.A.Normal + triangle.B.Normal + triangle.C.Normal, surface.Normal) < .45f) continue;
                result.Add(Overlay(triangle.A));
                result.Add(Overlay(triangle.B));
                result.Add(Overlay(triangle.C));
            }

            Vertex Overlay(in Vertex vertex)
            {
                var delta = vertex.Position - surface.Position;
                return vertex with
                {
                    Position = vertex.Position + vertex.Normal * offset,
                    Uv = new(Vector3.Dot(delta, axis) / size, Vector3.Dot(delta, perpendicular) / width),
                    Colour = pigment with { W = strength },
                };
            }
        }
        if (result.Count == 0) return null;
        var mesh = MeshCut.FromTriangles(result, skeleton);
        OgreMeshWeld.Apply(mesh);
        return mesh;
    }

    static Placement Place(in BodyWoundEntry entry)
    {
        var mark = entry.Mark;
        if (entry.Region >= 0)
        {
            float offset = (mark.Age * 37 % 7) / 3f - 1;
            return new("Bip01 Pelvis", "Bip01 Neck", (entry.Region == 0 ? .7f : entry.Region == 1 ? .35f : .5f) + offset * .13f,
                new(offset * .95f, 0, entry.Region == 2 ? -1 : 1), 0);
        }
        if (mark.Segment == 9) return new("Bip01 Pelvis", "Bip01 Neck", mark.T, mark.Side, 0);
        if (mark.Segment == 8) return new("Bip01 Neck", "Bip01 Head", 1.65f + mark.T * .45f,
            mark.Side + new Vector3(mark.Age % 2 == 0 ? .36f : -.36f, .18f, 0), 7);
        bool left = mark.Segment < 2 || mark.Segment is 4 or 5;
        bool lower = (mark.Segment & 1) != 0;
        string side = left ? "L" : "R";
        var (a, b) = mark.Segment < 4
            ? lower ? ("Forearm", "Hand") : ("UpperArm", "Forearm")
            : lower ? ("Calf", "Foot") : ("Thigh", "Calf");
        return new($"Bip01 {side} {a}", $"Bip01 {side} {b}", mark.T, mark.Side, mark.Segment / 2 + 1);
    }

    static List<SkinTriangle> SkinTriangles(OgreMesh body, SkeletonRig rig, CharacterAppearance look)
    {
        var triangles = new List<SkinTriangle>();
        var groups = MeshCut.BoneGroups(rig);
        bool shaved = look.Character?.GetBool("shaved") == true;
        foreach (var part in MeshCut.SourceModel(body, rig.BoneCount).Parts)
        {
            var vertices = part.Vertices;
            var morphs = MeshCut.MorphDeltas(body, part.SubMeshIndex, vertices.Length, look.PoseWeights, shaved);
            var indices = part.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                if (ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length) continue;
                var a = vertices[ia] with { Position = vertices[ia].Position + morphs[ia] };
                var b = vertices[ib] with { Position = vertices[ib].Position + morphs[ib] };
                var c = vertices[ic] with { Position = vertices[ic].Position + morphs[ic] };
                if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).LengthSquared() <= 1e-12f) continue;
                int ga = MeshCut.Group(a, groups), gb = MeshCut.Group(b, groups), gc = MeshCut.Group(c, groups);
                triangles.Add(new(a, b, c, ga == gb || ga == gc ? ga : gb));
            }
        }
        return triangles;
    }

    static SkinPoint? Closest(List<SkinTriangle> skin, Vector3 target, Vector3 side, int group)
    {
        float nearest = 12;
        SkinPoint? result = null;
        foreach (var triangle in skin)
        {
            if (!Matches(triangle.Group, group) || !Near(triangle, target, nearest)) continue;
            var (a, b, c, _) = triangle;
            var bary = RagdollSkinSurface.ClosestBarycentric(target, a.Position, b.Position, c.Position);
            var normal = Unit(a.Normal * bary.X + b.Normal * bary.Y + c.Normal * bary.Z, side);
            if (Vector3.Dot(normal, side) < .26f) continue;
            var point = a.Position * bary.X + b.Position * bary.Y + c.Position * bary.Z;
            float distance = Vector3.DistanceSquared(point, target);
            if (!(distance < nearest)) continue;
            nearest = distance;
            result = new(point, normal);
        }
        return result;
    }

    static bool Matches(int triangle, int group) => triangle == group || group == 7 && triangle == 6;

    static bool Near(in SkinTriangle triangle, Vector3 point, float reachSquared) =>
        Vector3.DistanceSquared(point, Vector3.Clamp(point, triangle.Low, triangle.High)) <= reachSquared;

    static Vector3 Unit(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : fallback;
}
