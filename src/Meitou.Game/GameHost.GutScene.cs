using System.Globalization;
using System.Numerics;

namespace Meitou.Game;

sealed partial class GameHost
{
    Vector3 GutSceneFocus()
    {
        var pose = gutFalls.Values.Select(g => gutPhysics?.PoseOf(g.RigKey)).FirstOrDefault(p => p is not null);
        return pose?.Centre ?? camera.Target;
    }

    void ReportGutScene(int frame)
    {
        var pieces = gutFalls.Values.Where(g => g.Physical)
            .Select(g => gutPhysics?.PoseOf(g.RigKey)).Where(p => p is not null).ToArray();
        string Number(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        var ranges = pieces.Select(p => gpu.Debris?.BowelBounds(p!) is { } b
            ? $"centre {Number(p!.Centre.Y)}, mesh Y {Number(b.Min.Y)}..{Number(b.Max.Y)}" : "pending upload");
        Console.WriteLine($"bowel frame {frame}: physical {gutPhysics?.Count ?? 0}, moving {gutPhysics?.Moving ?? 0}, attached {gutFalls.Values.Count(f => gutPhysics?.IsAttached(f.RigKey) == true)}; {string.Join("; ", ranges)}");
        var weapons = session.CurrentSnapshot.GroundItems.Where(item => item.Fall is not null
            && item.Type is Meitou.Data.Fcs.FcsRecordType.WEAPON or Meitou.Data.Fcs.FcsRecordType.CROSSBOW);
        Console.WriteLine($"cut weapons frame {frame}: ground {string.Join("; ", weapons.Select(item => $"{item.Id}:{item.Name}"))}");
    }
}
