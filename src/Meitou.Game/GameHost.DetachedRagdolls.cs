using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Wounds;

namespace Meitou.Game;

/// <summary>Live articulated cut-off parts; the snapshot's bones already contain each independent physical chain.</summary>
sealed partial class GameHost
{
    sealed class DetachedPartDraw
    {
        public required CharacterAppearance Look;
        public BonePose? Kept, Spare;
    }

    readonly Dictionary<(CharacterId Owner, int Part, int Fragment), DetachedPartDraw> detachedPartDraws = [];
    readonly HashSet<long> liveDetachedKeys = [];
    long detachedPartsEpoch = -1;

    static long DetachedDrawKey(long owner, int part, int fragment = 0) => part == -1
        ? owner ^ (1L << 54) ^ (fragment == 2 ? 1L << 51 : 0)
        : owner ^ (1L << 53) ^ ((long)part << 48);

    CharacterTorsoCut? AttachedHeadClip(CharacterSnapshot character) =>
        GoreEnabled && character.HeadCut is { IsValid: true, Mode: HeadCutMode.AttachedSplit } cut
            ? new(cut.Plane, -1, FullBody: true, HeadOnly: true) : null;

    CharacterTorsoCut? DetachedHeadClip(CharacterSnapshot character, int fragment) =>
        GoreEnabled && character.HeadCut is { IsValid: true } cut
            ? new(cut.Plane, fragment == 2 ? -1 : 1, FullBody: true, HeadOnly: true) : null;

    CharacterAppearance DetachedHeadLook(long source, CharacterAppearance current, CharacterSnapshot? previous)
    {
        if (gpu.Characters?.LastDrawn(source, "Bip01 Head") is (_, { Headless: false } drawn, _)) return drawn.SeveredHead();
        if (previous?.Appearance is { Headless: false } before) return before.SeveredHead();
        return current.SeveredHead();
    }

    void AddDetachedRagdolls(CharacterDrawList list)
    {
        if (!GoreEnabled) return;
        if (detachedPartsEpoch != worldEpoch) { detachedPartDraws.Clear(); detachedPartsEpoch = worldEpoch; }
        liveDetachedKeys.Clear();
        IndexPrevious();
        var retained = new HashSet<(CharacterId, int, int)>();
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (c.Appearance is not { } appearance) continue;
            previousById.TryGetValue(c.Id, out var before);
            foreach (var part in c.DetachedRagdolls)
            {
                if (part.Part == -1 && !appearance.Headless && c.HeadCut is not { Mode: HeadCutMode.AttachedSplit }) continue;
                var id = (c.Id, part.Part, part.FragmentId);
                retained.Add(id);
                long source = DrawKey(c.Id);
                long key = DetachedDrawKey(source, part.Part, part.FragmentId);
                liveDetachedKeys.Add(key);
                if (!detachedPartDraws.TryGetValue(id, out var draw))
                {
                    var look = part.Part == -1 ? DetachedHeadLook(source, appearance, before) : appearance.SeveredLimb(scene.Database!);
                    detachedPartDraws[id] = draw = new DetachedPartDraw { Look = look };
                    gpu.Characters?.Prefetch(look);
                    if (part.Part == -1 && gpu.Characters?.BonePosition(source, "Bip01 Head") is { } start && headsSeen.Add(c.Id))
                        Spurt(source, appearance, start);
                }
                var old = before is not null && !Reanchored(before, c)
                    ? before.DetachedRagdolls.FirstOrDefault(p => p.Part == part.Part && p.FragmentId == part.FragmentId) : null;
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
                    Bones = bones, Scale = c.Scale, Rotation = rotation, Blood = c.BloodAmounts, PartOnly = only, LimbSeverances = c.LimbSeverances,
                    TorsoCut = part.Part == -1 ? DetachedHeadClip(c, part.FragmentId) : null,
                });
            }
        }
        foreach (var id in detachedPartDraws.Keys.Where(id => !retained.Contains(id)).ToArray()) detachedPartDraws.Remove(id);
    }
}
