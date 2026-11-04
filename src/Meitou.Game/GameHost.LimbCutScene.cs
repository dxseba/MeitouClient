using System.Globalization;
using System.Numerics;

namespace Meitou.Game;

sealed partial class GameHost
{
    Vector3 LimbCutSceneFocus()
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe);
        if (character is null) return camera.Target;
        var renderer = gpu.Characters;
        var centre = renderer?.BonePosition(DrawKey(character.Id), "Bip01 Spine1")
            ?? DrawnPosition(character) + Vector3.UnitY * 7;
        var min = centre; var max = centre;
        foreach (var piece in character.DetachedRagdolls)
        {
            string side = piece.Part % 2 == 0 ? "L" : "R";
            string bone = $"Bip01 {side} {(piece.Part < 2 ? "Forearm" : "Calf")}";
            if (renderer?.BonePosition(DetachedDrawKey(DrawKey(character.Id), piece.Part, piece.FragmentId), bone) is not { } at) continue;
            min = Vector3.Min(min, at); max = Vector3.Max(max, at);
        }
        return (min + max) * .5f;
    }

    void ReportLimbCutScene(int frame)
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe);
        if (character is null) return;
        string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        string cuts = string.Join("; ", character.LimbSeverances.Select(c => $"{c.LimbSlot}:{F(c.Fraction)}/{F(c.Slant.X)},{F(c.Slant.Y)}"));
        Console.WriteLine($"limb cut frame {frame}: limb bits {character.LimbBits}, conscious {character.Conscious}, cuts {cuts}, detached {character.DetachedRagdolls.Count}");
    }
}
