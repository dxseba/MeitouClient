using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSurfaceBridge
{
    static readonly ConditionalWeakTable<BodyWoundState,MarkProjection> markCache=new();
    static readonly ConditionalWeakTable<SkeletonRig,RigPacking> rigCache=new();
    static void ClearCaches() { markCache.Clear(); rigCache.Clear(); }
    static RigPacking PackedRig(SkeletonRig rig)=>rigCache.GetValue(rig,static rig=>new RigPacking(rig));

    sealed class RigPacking
    {
        readonly SkeletonRig rig;
        readonly Dictionary<LimbCutState,Vector4?> planes=[];
        public readonly uint[] Groups=new uint[8];
        public readonly uint HeadLow,HeadHigh;
        public RigPacking(SkeletonRig rig)
        {
            this.rig=rig;
            foreach(var bone in rig.Skeleton.Bones)
            {
                if(bone.Handle>=64) continue;
                uint group=(uint)(bone.Name=="Bip01 Jaw" ? 6 : NativeWoundMasks.BodyGroup(bone.Name));
                Groups[bone.Handle/8]|=group<<(bone.Handle%8*4);
            }
            var groups=MeshCut.BoneGroups(rig);
            for(int bone=0;bone<Math.Min(64,groups.Length);bone++)
                if(groups[bone] is 6 or 7)
                {
                    if(bone<32) HeadLow|=1u<<bone;
                    else HeadHigh|=1u<<(bone-32);
                }
        }
        public bool TryPlane(in LimbCutState cut,out Vector4 plane)
        {
            if(!planes.TryGetValue(cut,out var cached))
            {
                cached=LimbCutGeometry.TryPlane(rig,cut,out var computed) ? computed : null;
                // Bound rare unique historical cuts without changing the result of any recomputation.
                if(planes.Count>=4096) planes.Clear();
                planes.Add(cut,cached);
            }
            plane=cached.GetValueOrDefault();
            return cached.HasValue;
        }
    }

    sealed class MarkProjection
    {
        readonly BodyWoundEntry[] entries;
        MarkVariant? human,thin;
        public MarkProjection(BodyWoundState state)=>entries=state.Entries.OrderBy(entry=>entry.Region<0 ? 1 : 0)
            .ThenBy(entry=>entry.Region>=0 ? entry.Region : 0).ThenByDescending(entry=>entry.Mark.Age).ToArray();
        public IReadOnlyList<NativeWoundMark> Get(bool isThin,long tick,double tickSeconds)=>
            (isThin ? thin??=new(entries,true) : human??=new(entries,false)).Get(tick,tickSeconds);
    }

    sealed class MarkVariant
    {
        readonly BodyWoundEntry[] entries;
        readonly NativeWoundMark[] templates;
        readonly byte[] steps;
        IReadOnlyList<NativeWoundMark>? marks;
        long lastTick;
        double lastTickSeconds;
        public MarkVariant(BodyWoundEntry[] entries,bool thin)
        {
            this.entries=entries;
            templates=BuildMarks(entries,thin).ToArray();
            steps=new byte[entries.Length];
            Array.Fill(steps,byte.MaxValue);
        }
        public IReadOnlyList<NativeWoundMark> Get(long tick,double tickSeconds)
        {
            if(marks is not null && tick==lastTick && tickSeconds==lastTickSeconds) return marks;
            lastTick=tick; lastTickSeconds=tickSeconds;
            bool changed=marks is null;
            for(int i=0;i<entries.Length;i++)
            {
                byte step=(byte)MathF.Ceiling(Math.Clamp((float)((tick-entries[i].Mark.Born)*tickSeconds/5),0,1)*12);
                if(steps[i]==step) continue;
                steps[i]=step; changed=true;
            }
            if(!changed) return marks!;
            var values=new NativeWoundMark[entries.Length];
            for(int i=0;i<values.Length;i++) values[i]=templates[i] with { Flow=steps[i]/12f };
            return marks=Array.AsReadOnly(values);
        }
    }
}
