using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>Live articulated cut-off parts; the snapshot's bones already contain each independent physical chain.</summary>
sealed partial class GameHost
{
    sealed class DetachedPartDraw
    {
        public required CharacterAppearance Look;
        public BonePose? Kept, Spare;
    }

    readonly Dictionary<(CharacterId Owner, int Part), DetachedPartDraw> detachedPartDraws = [];
    readonly HashSet<long> liveDetachedKeys = [];
    long detachedPartsEpoch = -1;

    void AddDetachedRagdolls(CharacterDrawList list)
    {
        if (detachedPartsEpoch != worldEpoch) { detachedPartDraws.Clear(); detachedPartsEpoch = worldEpoch; }
        liveDetachedKeys.Clear();
        IndexPrevious();
        var retained = new HashSet<(CharacterId, int)>();
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (c.Appearance is not { } appearance) continue;
            previousById.TryGetValue(c.Id, out var before);
            foreach (var part in c.DetachedRagdolls)
            {
                if (part.Part == -1 && !appearance.Headless) continue;
                var id = (c.Id, part.Part);
                retained.Add(id);
                long source = DrawKey(c.Id);
                long key = part.Part == -1 ? source ^ (1L << 54) : source ^ (1L << 53) ^ ((long)part.Part << 48);
                liveDetachedKeys.Add(key);
                if (!detachedPartDraws.TryGetValue(id, out var draw))
                {
                    var look = part.Part == -1 ? appearance.SeveredHead() : appearance.SeveredLimb(scene.Database!);
                    detachedPartDraws[id] = draw = new DetachedPartDraw { Look = look };
                    gpu.Characters?.Prefetch(look);
                    if (part.Part == -1 && gpu.Characters?.BonePosition(source, "Bip01 Head") is { } start && headsSeen.Add(c.Id))
                        Spurt(source, appearance, start);
                }
                var old = before is not null && !Reanchored(before, c) ? before.DetachedRagdolls.FirstOrDefault(p => p.Part == part.Part) : null;
                var bones = Blend(old?.Bones, part.Bones, session.SimulationAlpha, ref draw.Kept, ref draw.Spare);
                var position = c.Position;
                float yaw = DrawnYaw(c);
                if (before is not null && !Reanchored(before, c))
                {
                    position = Vector3.Lerp(before.Position, position, session.SimulationAlpha);
                    yaw = Meitou.Engine.Time.Interp.LerpAngle(DrawnYaw(before), yaw, session.SimulationAlpha);
                }
                Quaternion? rotation = null;
                if (c.Carry is { } carry)
                {
                    yaw = CarrierYaw(session.CurrentSnapshot, c, yaw, session.SimulationAlpha);
                    (position, rotation) = MixCarry(before?.Carry, carry, session.SimulationAlpha).World(position, yaw);
                }
                uint only = 0;
                if (part.Part >= 0)
                {
                    int level = Meitou.Simulation.Wounds.SeveredLevels.Of(c.Id, part.Part);
                    uint height = level == 0 ? 255u : (uint)(Meitou.Simulation.Wounds.SeveredLevels.Height(level, part.Part) * 10);
                    only = (uint)(part.Part + 1) << 8 | height;
                }
                list.Add(new CharacterInstance(key, draw.Look, position, yaw, [])
                {
                    Bones = bones, Scale = c.Scale, Rotation = rotation, Blood = c.BloodAmounts, PartOnly = only,
                });
            }
        }
        foreach (var id in detachedPartDraws.Keys.Where(id => !retained.Contains(id)).ToArray()) detachedPartDraws.Remove(id);
    }
}
