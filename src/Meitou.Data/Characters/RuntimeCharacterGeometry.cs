using Meitou.Data.Ogre;

namespace Meitou.Data.Characters;

/// <summary>
/// Optional generated geometry for one presentation variant. Its object identity is the renderer cache identity: keep the same instance while unchanged.
/// Meshes and their arrays must not be changed after publication. The original appearance still supplies the rig, shape, textures and animation.
/// </summary>
public sealed class RuntimeCharacterGeometry
{
    int retired;
    /// <summary>The producer no longer publishes this variant. Already submitted frames remain valid until the renderer retires their resources.</summary>
    public bool IsRetired => Volatile.Read(ref retired) != 0;
    public void Retire() => Interlocked.Exchange(ref retired, 1);
    /// <summary>Null keeps the appearance's body mesh. Generated meshes use the original body's skeleton and bone handles.</summary>
    public OgreMesh? BodyMesh { get; }

    /// <summary>Null keeps the original parts; otherwise this is the complete replacement list (empty removes all non-body parts).</summary>
    public IReadOnlyList<RuntimeCharacterPart>? Parts { get; }

    public RuntimeCharacterGeometry(OgreMesh? bodyMesh = null, IEnumerable<RuntimeCharacterPart>? parts = null)
    {
        BodyMesh = bodyMesh;
        Parts = parts is null ? null : Array.AsReadOnly(parts.ToArray());
    }
}

/// <summary>
/// A part with the usual attachment/material metadata and an optional generated mesh. Null Mesh loads Part.Mesh normally.
/// With a supplied Mesh, VertexColours preserves generated colour values and enables ordinary item vertex-colour shading; meshes remain immutable after publication.
/// </summary>
public sealed record RuntimeCharacterPart(CharacterPart Part, OgreMesh? Mesh = null, bool VertexColours = false)
{
    /// <summary>Optional decreasing projected-diameter thresholds in pixels, one per supplied Mesh.Lod level after full detail. The list is immutable after publication.</summary>
    public IReadOnlyList<float>? ScreenLodThresholds { get; init; }
}
