namespace Meitou.Brutality.Rendering;

/// <summary>Immutable per-piece physical-island maps. The physics owner must never change arrays after publication.</summary>
public readonly record struct CharacterGeometryComponents(int[]? Retained = null, int[]? TorsoNegative = null,
    int[]? HeadPositive = null, int[]? HeadNegative = null, int[]? LeftArm = null, int[]? RightArm = null,
    int[]? LeftLeg = null, int[]? RightLeg = null)
{
    public int[]? For(int stableKey) => stableKey switch
    {
        0 => Retained, 1 => TorsoNegative, 2 => HeadPositive, 3 => HeadNegative,
        10 => LeftArm, 11 => RightArm, 12 => LeftLeg, 13 => RightLeg, _ => null,
    };
}
