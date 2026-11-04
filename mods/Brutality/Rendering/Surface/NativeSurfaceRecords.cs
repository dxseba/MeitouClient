// Adapted from the integrated GPL-3.0-or-later renderer at 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using System.Numerics;
namespace Meitou.Brutality.Rendering.Surface;
internal readonly record struct NativeWoundMark(string BoneA, string BoneB, float T, Vector3 Side,
    float Cosine, float Sine, float Aspect, float Depth, float Size=1, float Flow=1, int Kind=0, int WeaponCategory=-1);
internal readonly record struct NativeHeadPlacement(Vector3 Point,Vector3 Normal,Vector3 Tangent,float LengthScale=1,float WidthScale=1);
internal static class NativeFluidPalette
{
    public const uint DefaultBlood=0x800000;
    public static Vector4 Recolour(Vector4 colour,uint blood) => AnatomyGeometry.Recolour(colour,blood);
}
