namespace Meitou.Simulation.Wounds;

/// <summary>Where a lost limb was cut: 0 the game's stump at the top of the limb, 1 and 2 further down. A look, rolled from the character and limb.</summary>
public static class SeveredLevels
{
    /// <summary>Set by a test: every limb at this level.</summary>
    public static int? Forced { get; set; }

    public static int Of(CharacterId id, int limb) => Forced ?? (id.Slot * 7 + limb * 3 + id.Generation) % 3;

    /// <summary>The bind height of the cut, 0 at level 0.</summary>
    public static float Height(int level, int limb) => level == 0 ? 0 : limb < 2 ? (level == 1 ? 12.4f : 11f) : level == 1 ? 6f : 3.5f;
}
