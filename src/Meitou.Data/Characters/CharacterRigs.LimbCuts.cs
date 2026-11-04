using System.Runtime.CompilerServices;
using Meitou.Data.Fcs;

namespace Meitou.Data.Characters;

public sealed partial class CharacterRigs
{
    sealed class LimbShapeSet
    {
        public CharacterShapeInput? Input;
        public ulong Stamp;
        public GameRecord? Stats;
        public bool ReadStats;
        public readonly Dictionary<SkeletonRig, LimbShapes> Rigs = [];
    }

    sealed class LimbShapes
    {
        // Physics uses the source shape; rendering can simultaneously use the latest stat-driven shape.
        // Each of the sixteen masks retains at most those two versions, never a build history.
        public readonly BodyShapeData?[] Source = new BodyShapeData?[16], Built = new BodyShapeData?[16];
        public readonly BodyBuild?[] Builds = new BodyBuild?[16];
        public uint SourceReady, BuiltReady;
    }

    readonly ConditionalWeakTable<CharacterAppearance, LimbShapeSet> limbShapes = new();

    /// <summary>Restores natural proportions only for limbs whose retained skin is clipped by explicit planes. Other missing limbs keep their original shape.</summary>
    public BodyShapeData? ShapeForLimbCuts(CharacterAppearance c, SkeletonRig rig, uint mask, BodyBuild? build = null)
    {
        if (c.Animal is not null || rig.BoneCount != CharacterShape.HumanBoneCount) return null;
        mask &= 15;
        var cache = limbShapes.GetValue(c, static _ => new());
        lock (cache)
        {
            if (!cache.ReadStats)
            {
                var references = c.Character?.GetReferences("stats");
                if (references is not null)
                    for (int i = 0; i < references.Count; i++)
                        if (db.Find(references[i].TargetStringId) is { Type: FcsRecordType.STATS } stats) { cache.Stats = stats; break; }
                cache.ReadStats = true;
            }
            ulong stamp = LimbShapeStamp(c, cache.Stats);
            if (cache.Input is null || cache.Stamp != stamp)
            {
                cache.Input = c.Shape ?? CharacterShape.InputFor(db, c);
                cache.Stamp = stamp;
                cache.Rigs.Clear();
            }
            if (!cache.Rigs.TryGetValue(rig, out var shapes)) cache.Rigs.Add(rig, shapes = new());
            uint bit = 1u << (int)mask;
            int slot = (int)mask;
            if (build is null && (shapes.SourceReady & bit) != 0) return shapes.Source[slot];
            if (build is not null && (shapes.BuiltReady & bit) != 0 && shapes.Builds[slot] == build) return shapes.Built[slot];

            var shape = ComputeLimbShape(rig, cache.Input, mask, build);
            if (build is null) { shapes.Source[slot] = shape; shapes.SourceReady |= bit; }
            else { shapes.Built[slot] = shape; shapes.Builds[slot] = build; shapes.BuiltReady |= bit; }
            return shape;
        }
    }

    // Keep the captured has-bone delegate outside the cache-hit method: hoisting its rig parameter would allocate even on a hit.
    static BodyShapeData ComputeLimbShape(SkeletonRig rig, CharacterShapeInput input, uint mask, BodyBuild? build)
    {
        if (build is { } value) input = input with { Muscle = value.Muscle, Starvation = value.Starvation };
        if (mask != 0)
        {
            var missing = new HashSet<Limb>(input.Missing);
            for (int limb = 0; limb < 4; limb++) if ((mask & 1u << limb) != 0) missing.Remove((Limb)limb);
            input = input with
            {
                Missing = missing, HideStump = input.HideStump & ~(HideStump)(mask & 3),
                NarrowLeftThigh = (mask & 4) == 0 && input.NarrowLeftThigh,
                NarrowRightThigh = (mask & 8) == 0 && input.NarrowRightThigh,
            };
        }
        var computed = CharacterShape.Compute(input, name => rig.Handle(name) is not null);
        return rig.Shape(computed.BoneSize, computed.PositionalSize, computed.MovementScale);
    }

    // Read only known shape inputs: interface enumeration would allocate an enumerator on every posed frame.
    static ulong LimbShapeStamp(CharacterAppearance c, GameRecord? stats)
    {
        ulong hash = 14695981039346656037UL;
        var input = c.Shape;
        Add(input is null ? 0u : 1u);
        var sliders = input?.Sliders ?? c.Body?.Floats;
        for (int index = 0; index < CharacterShape.Sliders.Count; index++)
            Number(sliders is not null && sliders.TryGetValue(CharacterShape.Sliders[index], out float value) ? value : 100);
        if (input is not null)
        {
            Add(input.Female ? 1u : 0u); Number(input.Muscle); Number(input.Starvation); Number(input.LegRatio);
            Add((uint)input.HideStump); Add(input.NarrowLeftThigh ? 1u : 0u); Add(input.NarrowRightThigh ? 1u : 0u);
            for (int limb = 0; limb < 4; limb++) Add(input.Missing.Contains((Limb)limb) ? 1u : 0u);
        }
        else
        {
            Add(c.Female ? 1u : 0u);
            for (int limb = 0; limb < 4; limb++) Add((uint)(c.Loadout?.Limbs[limb]?.State ?? 0));
            for (int i = 0; i < c.Parts.Count; i++)
            {
                var part = c.Parts[i];
                Add(part.Slot is { } slot ? (uint)slot + 1 : 0); Add((uint)part.Record.Type); Add((uint)part.Record.GetInt("hide stump"));
                Add((uint)part.Record.GetInt("slot")); Number(part.Record.GetFloat("boot height")); Number(part.Record.GetFloat("offset"));
            }
            Number(stats?.GetFloat("strength") ?? 0); Number(stats?.GetFloat("weapon smith") ?? 0); Number(stats?.GetFloat("armour smith") ?? 0);
        }
        return hash;

        void Add(uint value) => hash = (hash ^ value) * 1099511628211UL;
        void Number(float value) => Add(BitConverter.SingleToUInt32Bits(value));
    }
}
