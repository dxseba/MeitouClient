using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Wounds;

namespace Meitou.Game;

sealed partial class GameHost
{
    static readonly (int Part, string[] Bones)[] FragmentLabelBones =
    [
        (-1, ["Bip01 Head", "Bip01 Jaw"]),
        (0, ["Bip01 L Forearm", "Bip01 L Hand", "Bip01 L UpperArm"]),
        (1, ["Bip01 R Forearm", "Bip01 R Hand", "Bip01 R UpperArm"]),
        (2, ["Bip01 L Calf", "Bip01 L Foot", "Bip01 L Thigh"]),
        (3, ["Bip01 R Calf", "Bip01 R Foot", "Bip01 R Thigh"]),
    ];
    static readonly string[] TorsoLabelBones = ["Bip01 Pelvis", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head", "Bip01 L Thigh", "Bip01 R Thigh"];

    IReadOnlyList<BodyFragmentLabel> BodyFragmentLabels() => gpu.Characters is { } renderer ? BodyFragmentLabels(renderer) : [];

    IReadOnlyList<BodyFragmentLabel> BodyFragmentLabels(CharacterRenderer renderer)
    {
        var result = new List<BodyFragmentLabel>();
        var snapshot = session.CurrentSnapshot;
        var loot = snapshot.GroundItems.Select(g => g.Id).ToHashSet();
        string Name(string owner, string part) => (owner.Length == 0 ? Loc.Tr("Unknown") : owner) + " — " + Loc.Tr(part);
        foreach (var c in snapshot.Characters)
        {
            if (CharacterHidden(c.Id) || c.Appearance is not { } look || renderer.Rigs.For(look) is not { } setup) continue;
            var rig = setup.Rig;
            long key = DrawKey(c.Id);
            string rootName = rig.Order.Length > 0 ? rig.ByHandle[rig.Order[0]]?.Name ?? "Bip01" : "Bip01";
            if (c.TorsoBisection is { IsValid: true } cut)
            {
                bool? leftPositive = rig.Handle("Bip01 L UpperArm") is { } left && rig.Handle("Bip01 R UpperArm") is { } right
                    && MathF.Abs(rig.BindPosition[left].X - rig.BindPosition[right].X) > 1e-4f ? rig.BindPosition[left].X > rig.BindPosition[right].X : null;
                for (int side = 0; side <= 1; side++)
                {
                    long pieceKey = side == 1 ? key ^ (1L << 52) : key;
                    if (renderer.BonePosition(pieceKey, rootName) is null) continue;
                    var pose = side == 1 ? c.TorsoRagdoll?.Positive : c.TorsoRagdoll?.Negative;
                    int rootComponent = pose?.Components is { } components && rig.Order.Length > 0 && rig.Order[0] < components.Length ? components[rig.Order[0]] : -1;
                    var anchor = RootAnchor(c, pieceKey, side, rig, pose, rootComponent, cut, renderer);
                    if (anchor is { } at)
                        result.Add(new(pieceKey, c.Id, Name(c.Name, BodyFragmentNames.Torso(cut, side, leftPositive)), at, BodyFragmentLabelKind.TorsoHalf, side));
                    if (pose?.Components is not { } pieces) continue;
                    foreach (int component in pieces.Distinct().Where(value => value != rootComponent))
                    {
                        var matching = FragmentLabelBones.Where(part => part.Part == -1 ? !look.Headless : (look.HiddenLimbs & (1 << part.Part)) == 0).Select(part => (part.Part, Bones: part.Bones.Where(name => rig.Handle(name) is { } h && h < pieces.Length && pieces[h] == component).ToArray()))
                            .Where(part => part.Bones.Length > 0).ToArray();
                        if (matching.Length == 0) continue;
                        var kept = matching.SelectMany(part => part.Bones).Where(name => KeptBone(rig, name, cut, side)).Distinct().ToArray();
                        if (kept.Length == 0) kept = matching.SelectMany(part => part.Bones).Distinct().OrderBy(name => MathF.Abs(Vector4.Dot(cut.Plane, new(rig.BindPosition[rig.Handle(name)!.Value], 1)))).Take(1).ToArray();
                        var positions = kept.Select(name => renderer.BonePosition(pieceKey, name)).Where(at => at is not null).Select(at => at!.Value).ToArray();
                        if (positions.Length == 0) continue;
                        var middle = positions.Aggregate(Vector3.Zero, (sum, position) => sum + position) / positions.Length;
                        string partName = matching.Length == 1 ? BodyFragmentNames.Part(matching[0].Part) : "Body fragment";
                        result.Add(new(pieceKey, c.Id, Name(c.Name, partName), middle, BodyFragmentLabelKind.Detached, side, component));
                    }
                }
            }
            else if (c.Body is { Dead: true } && (renderer.BonePosition(key, "Bip01 Spine1") ?? renderer.BonePosition(key, rootName)) is { } body)
                result.Add(new(key, c.Id, Name(c.Name, "Body"), body, BodyFragmentLabelKind.Body));

            foreach (var part in c.DetachedRagdolls)
            {
                if (part.GroundItemId > 0 && loot.Contains(part.GroundItemId)) continue;
                long pieceKey = part.Part == -1 ? key ^ (1L << 54) : key ^ (1L << 53) ^ ((long)part.Part << 48);
                var names = FragmentLabelBones.FirstOrDefault(p => p.Part == part.Part).Bones ?? [];
                var points = names.Select(name => renderer.BonePosition(pieceKey, name)).Where(at => at is not null).Select(at => at!.Value).ToArray();
                if (points.Length == 0) continue;
                var at = points.Aggregate(Vector3.Zero, (sum, position) => sum + position) / points.Length;
                result.Add(new(pieceKey, c.Id, Name(c.Name, BodyFragmentNames.Part(part.Part)), at, BodyFragmentLabelKind.Detached));
            }
        }
        if (headsEpoch == worldEpoch)
            foreach (var part in heads)
            {
                if (liveDetachedKeys.Contains(part.Key)) continue;
                int group = (int)(part.PartOnly >> 8), kind = part.PartOnly == 0 ? -1 : group == 6 ? 7 : group - 1;
                string bone = kind == 7 ? "Bip01 Jaw" : FragmentLabelBones.FirstOrDefault(p => p.Part == kind).Bones?.FirstOrDefault() ?? "Bip01 Head";
                if (renderer.BonePosition(part.Key, bone) is not { } at) continue;
                var owner = snapshot.Characters.FirstOrDefault(c => DrawKey(c.Id) == part.From);
                if (owner is not null && CharacterHidden(owner.Id)) continue;
                if (owner is not null && kind is >= 0 and <= 3 && snapshot.GroundItems.Any(item => item.Fall is { } fall && fall.Character == owner.Id
                    && FragmentLabelBones.First(p => p.Part == kind).Bones.Contains(fall.Bone, StringComparer.OrdinalIgnoreCase))) continue;
                string ownerName = owner?.Name ?? part.Look.Character?.Name ?? Loc.Tr("Unknown");
                result.Add(new(part.Key, owner?.Id ?? CharacterId.None, Name(ownerName, BodyFragmentNames.Part(kind)), at, BodyFragmentLabelKind.Detached));
            }
        return result;
    }

    static bool KeptBone(SkeletonRig rig, string name, in TorsoBisectionState cut, int side)
    {
        if (rig.Handle(name) is not { } handle) return false;
        return TorsoCutSurface.Band(rig.BindPosition[handle], WoundMasks.BodyGroup(name), new(cut.Plane, side == 1 ? 1 : -1, cut.FullBody)) >= 0;
    }

    Vector3? RootAnchor(CharacterSnapshot c, long key, int side, SkeletonRig rig, BonePose? pose, int component, TorsoBisectionState cut, CharacterRenderer renderer)
    {
        if (pose is not null)
        {
            if (side == 1 && c.TorsoPositivePosition is { } positive) return positive;
            if (side == 0 && c.CarriedBy.IsNone) return DrawnPosition(c);
        }
        var names = TorsoLabelBones.Where(name => rig.Handle(name) is { } h && (pose?.Components is not { } groups || h < groups.Length && groups[h] == component)).ToArray();
        foreach (string name in names.Where(name => KeptBone(rig, name, cut, side)))
            if (renderer.BonePosition(key, name) is { } at) return at;
        foreach (string name in names.OrderBy(name => MathF.Abs(Vector4.Dot(cut.Plane, new(rig.BindPosition[rig.Handle(name)!.Value], 1)))))
            if (renderer.BonePosition(key, name) is { } at) return at;
        return null;
    }
}
