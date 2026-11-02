using System.Globalization;
using System.Numerics;

namespace Meitou.Game;

sealed partial class GameHost
{
    Vector3 HeadCutSceneFocus()
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe);
        if (character is null) return camera.Target;
        var points = new List<Vector3>();
        var renderer = gpu.Characters;
        if (character.HeadCut is null or { Mode: Meitou.Simulation.Wounds.HeadCutMode.AttachedSplit }
            && renderer?.BonePosition(DrawKey(character.Id), "Bip01 Head") is { } attached) points.Add(attached);
        foreach (var part in character.DetachedRagdolls)
            if (part.Part == -1 && renderer?.BonePosition(DetachedDrawKey(DrawKey(character.Id), -1, part.FragmentId), "Bip01 Head") is { } point)
                points.Add(point);
        return points.Count > 0 ? points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / points.Count
            : DrawnPosition(character) + new Vector3(0, 17, 0);
    }

    void ReportHeadCutScene(int frame)
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe);
        if (character is null) return;
        string Point(Vector3 p) => $"{p.X.ToString("0.000", CultureInfo.InvariantCulture)},"
            + $"{p.Y.ToString("0.000", CultureInfo.InvariantCulture)},{p.Z.ToString("0.000", CultureInfo.InvariantCulture)}";
        var parts = character.DetachedRagdolls.Where(p => p.Part == -1).Select(part =>
        {
            var point = gpu.Characters?.BonePosition(DetachedDrawKey(DrawKey(character.Id), -1, part.FragmentId), "Bip01 Head");
            return $"{part.FragmentId}@{(point is { } p ? Point(p) : "pending")}";
        });
        Console.WriteLine($"head cut frame {frame}: {character.HeadCut?.Mode.ToString() ?? "none"}; "
            + $"headless {character.Appearance?.Headless}; pieces {string.Join("; ", parts)}");
    }
}
