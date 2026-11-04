using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

/// <summary>Bind support is cached; posed support is recomputed because the bridge reuses mutable interpolation poses.</summary>
internal static class FragmentWorldBounds
{
    const float SurfaceMargin = .5f;
    sealed class Support(SkeletonRig rig, float[] radii, bool[] used)
    {
        public readonly SkeletonRig Rig = rig;
        public readonly float[] Radii = radii;
        public readonly bool[] Used = used;
        public readonly PoseScratch Scratch = new(rig);
        public BodyBuild? Build;
        public BodyShapeData? Shape;
    }
    static readonly ConditionalWeakTable<RuntimeCharacterGeometry, Support> support = new();

    public static CharacterWorldBounds? For(RuntimeCharacterGeometry geometry, CharacterAppearance appearance, SkeletonRig rig,
        OgreMesh originalBody, BonePose? pose, Matrix4x4 placement, Func<string, OgreMesh?> loadPart,
        BodyShapeData? shape = null, CharacterRigs? rigs = null, BodyBuild? build = null)
    {
        if (pose is not { AbsolutePositions: true }) return null;
        var data = support.GetValue(geometry, _ => Make(geometry, appearance, rig, originalBody, loadPart));
        if (!ReferenceEquals(data.Rig, rig)) return null;
        lock (data)
        {
            if (data.Build != build)
            { data.Build = build; data.Shape = build is { } next && rigs is not null ? rigs.Reshape(appearance, next)?.Shape ?? shape : shape; }
            data.Scratch.PoseDerived(data.Shape ?? shape, [], pose);
            float worldScale = ScaleOf(placement);
            var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
            bool any = false;
            for (int bone = 0; bone < data.Used.Length; bone++)
            {
                if (!data.Used[bone]) continue;
                var at = Vector3.Transform(data.Scratch.DerivedPosition[bone], placement);
                var scale = data.Scratch.DerivedScale[bone] / rig.BindScale[bone];
                float size = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
                // Rigid worn parts retain their own size when their supporting bone shrinks.
                float radius = (data.Radii[bone] * MathF.Max(1, size) + SurfaceMargin) * worldScale;
                if (!Finite(at) || !float.IsFinite(radius)) continue;
                minimum = Vector3.Min(minimum, at - new Vector3(radius));
                maximum = Vector3.Max(maximum, at + new Vector3(radius)); any = true;
            }
            if (!any) return null;
            var centre = (minimum + maximum) * .5f;
            float extent = Vector3.Distance(minimum, maximum) * .5f;
            return Finite(centre) && float.IsFinite(extent) && extent > 0 ? new(centre, extent) : null;
        }
    }

    static Support Make(RuntimeCharacterGeometry geometry, CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        Func<string, OgreMesh?> loadPart)
    {
        var radius = new float[rig.BoneCount]; var used = new bool[rig.BoneCount];
        var groups = MeshCut.BoneGroups(rig);
        void Shared(OgreMesh mesh, bool raw)
        {
            var model = Model.Build(mesh, rig.BoneCount, gameBinormals: true, preserveRawVertexChannels: raw);
            foreach (var part in model.Parts)
            {
                var morph = MeshCut.MorphDeltas(mesh, part.SubMeshIndex, part.Vertices.Length, appearance.PoseWeights,
                    appearance.Character?.GetBool("shaved") == true);
                foreach (uint index in part.Indices)
                {
                    var vertex = part.Vertices[index];
                    int group = MeshCut.Group(vertex, groups);
                    if (appearance.Headless && group is 6 or 7 || appearance.HeadOnly && group is not (6 or 7)) continue;
                    var point = vertex.Position + morph[index];
                    for (int k = 0; k < 4; k++)
                    {
                        int bone = (int)(vertex.Bones >> (8 * k) & 255);
                        if (vertex.Weights[k] <= 0 || bone >= rig.BoneCount) continue;
                        used[bone] = true; radius[bone] = MathF.Max(radius[bone], Vector3.Distance(point, rig.BindPosition[bone]));
                    }
                }
            }
        }
        Shared(geometry.BodyMesh ?? body, geometry.PreserveRawBodyVertexChannels);
        var parts = geometry.Parts ?? appearance.Parts.Select(part => new RuntimeCharacterPart(part)).ToArray();
        foreach (var supplied in parts)
        {
            // These generated layers fit under the kept skin; their full source mesh may intentionally contain shader-hidden regions.
            if (supplied.Part.Field is "anatomy" or "head-relief" or "wound-section" or "mechanical-section" or "body-wound-relief") continue;
            var mesh = supplied.Mesh ?? loadPart(supplied.Part.Mesh);
            if (mesh is null) continue;
            if (supplied.Part.Mode == AttachMode.SharedSkeleton) { Shared(mesh, supplied.PreserveRawVertexChannels); continue; }
            var (at, bone) = CharacterGeometryAdapter.Attachment(appearance, rig, supplied.Part);
            if (bone < 0 || bone >= rig.BoneCount) continue;
            float reach = 0;
            foreach (var part in Model.Build(mesh, null).Parts)
                foreach (uint index in part.Indices) reach = MathF.Max(reach, part.Vertices[index].Position.Length());
            used[bone] = true;
            radius[bone] = MathF.Max(radius[bone], Vector3.Distance(at, rig.BindPosition[bone]) + reach);
        }
        return new(rig, radius, used);
    }

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    static float ScaleOf(Matrix4x4 world)
    {
        if (Matrix4x4.Decompose(world, out var scale, out _, out _))
            return MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        return MathF.Sqrt(world.M11 * world.M11 + world.M12 * world.M12 + world.M13 * world.M13
            + world.M21 * world.M21 + world.M22 * world.M22 + world.M23 * world.M23
            + world.M31 * world.M31 + world.M32 * world.M32 + world.M33 * world.M33);
    }
}
