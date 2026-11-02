using System.Numerics;

namespace Meitou.Data.Physics;

/// <summary>The loose bowel mesh's physical rest lattice, in its bounds-centred local space.</summary>
public sealed record BowelRigGeometry(Vector3[] RestNodes, float Radius);

/// <summary>Maps rest-local mesh points through each tissue node into coordinates relative to <see cref="Centre"/>.</summary>
public sealed record BowelPose(long Key, Vector3 Centre, Matrix4x4[] Transforms, Vector3[] RestNodes);
