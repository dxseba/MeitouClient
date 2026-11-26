using System.Globalization;
using System.Numerics;

namespace Meitou.Nitoryu;

/// <summary>
/// Where the second blade is held: a bone and an offset in its frame (the original's <c>[OffHand] bone / pos / rot</c>).
/// <para>
/// The original holds it on <c>Bip01 Prop1</c> only while its pose engine rewrites that bone each frame, mirroring the right hand. Without the
/// pose engine it hangs the blade on <c>Bip01 L Hand</c> with a fixed grip (its log line "pose engine off; the second blade hangs in the left
/// hand with a fixed grip"). Meitou has no such pose engine, and <c>Bip01 Prop1</c> is no grip bone: in the human skeletons it sits 3.67 units
/// from the left hand in the bind pose, and every vanilla clip moves it for its own use. A blade on it jumps next to the right hand or swings
/// loose. So the port always uses the left hand.
/// </para>
/// <para>
/// The default grip is the main weapon's, mirrored to the left: <c>Bip01 Prop2</c>'s bind offset from <c>Bip01 R Hand</c> in
/// <c>male_skeleton.skeleton</c> (<c>female_skeleton</c>'s is within 0.015 of it), reflected through the plane that maps the right hand's
/// frame onto the left one. The two hands' bind rotations differ by that reflection: (x, y, z, w) to (-x, y, -z, w), so positions map
/// (x, y, z) to (x, -y, z).
/// </para>
/// </summary>
readonly record struct OffHandGrip(string Bone, Vector3 Position, Quaternion Rotation)
{
    const string HandBone = "Bip01 L Hand";

    static readonly Vector3 MainGripPosition = new(-0.001004219f, -0.0016928315f, -0.2817644f);
    static readonly Quaternion MainGripRotation = new(-0.0007300798f, 0.00071391946f, -0.70532733f, 0.7088811f);

    /// <summary>The main weapon's grip mirrored onto the left hand.</summary>
    public static OffHandGrip Default => new(HandBone, Mirror(MainGripPosition), Mirror(MainGripRotation));

    /// <summary>
    /// The grip with the original's ini overrides: <c>[OffHand] bone</c>, <c>pos</c> (x y z) and <c>rot</c> (w x y z, Ogre's order). A value that
    /// does not parse keeps the default. <c>Bip01 Prop1</c> as the bone is ignored, for the reason above.
    /// </summary>
    public static OffHandGrip From(NitoryuConfig config, Action<string> log)
    {
        var grip = Default;
        string bone = config.IniRaw("OffHand.bone");
        bool prop = bone.Equals("Bip01 Prop1", StringComparison.OrdinalIgnoreCase);
        if (bone.Length > 0 && !prop) grip = grip with { Bone = bone };
        if (prop) log("[OffHand] bone = Bip01 Prop1 is not used: every vanilla clip moves that bone; the left hand holds the blade");

        if (Floats(config.IniRaw("OffHand.pos"), 3) is { } p) grip = grip with { Position = new Vector3(p[0], p[1], p[2]) };

        // Ogre writes a quaternion w x y z.
        if (Floats(config.IniRaw("OffHand.rot"), 4) is { } r)
            grip = grip with { Rotation = Quaternion.Normalize(new Quaternion(r[1], r[2], r[3], r[0])) };

        return grip;
    }

    static Vector3 Mirror(Vector3 v) => new(v.X, -v.Y, v.Z);

    static Quaternion Mirror(Quaternion q) => new(-q.X, q.Y, -q.Z, q.W);

    static float[]? Floats(string text, int count)
    {
        var parts = text.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != count) return null;

        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        }

        return values;
    }
}
