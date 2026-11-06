using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Simulation;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>The mod's projected wound masks and opaque shader payload; no wound semantics enter the host API.</summary>
internal static partial class NativeSurfaceBridge
{
    static RenderResources? resources;
    static IGpuContext? gpu;
    static NativeWoundMasks? masks;
    static World? activeWorld;
    static bool reportedContext, reportedDraws;
    static readonly Dictionary<long, (Matrix4x4[] Prefix, RuntimeCharacterShaderData Data)> payloads = [];
    static readonly Dictionary<long, (BrutalityActorView State, int Piece)> owners = [];
    static readonly HashSet<long> used = [];
    static readonly List<long> stalePayloads = [];
    static readonly int[] pieceKeys = [1, 2, 3, 10, 11, 12, 13];
    internal const int BloodColourWord = 29;
    /// <summary>header[3][2]: bit 0 marks the chest hole matrix in front of the limb planes.</summary>
    internal const int ChestHoleWord = 30;

    public static void Install(ModContext context, RenderResources source)
    {
        resources = source;
        NativeWoundMasks.Sample = static (x,y,w) =>
        {
            var sample = WoundPolicy.Sample(x,y,new(w.Cosine,w.Sine,w.Aspect,w.Depth,(WoundKind)w.Kind,w.WeaponCategory));
            return new(sample.Core,sample.Area,sample.Ring);
        };
        RenderBridge.BeforeEffects += Fill;
    }

    static void Dispose()
    {
        masks?.Dispose(); masks=null; gpu=null; activeWorld=null; payloads.Clear();
        owners.Clear(); used.Clear(); stalePayloads.Clear(); ClearCaches();
    }

    static void Fill(object host, WorldSession session, CharacterDrawList list)
    {
        if(resources is null) return;
        if (gpu is null && list.Context is { } context) { gpu=context; list.Disposing+=Dispose; }
        if (!reportedContext && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is not null) { Console.WriteLine($"Brutality native surface: GPU={gpu?.GetType().Name ?? "absent"}, draws={list.Items.Count}"); reportedContext=true; }
        if(gpu is null) return;
        if(!ReferenceEquals(activeWorld,session.World))
        {
            masks?.Dispose(); masks=new(gpu); payloads.Clear(); ClearCaches(); activeWorld=session.World;
        }
        gpu.EnsureFrame();
        masks!.BeginFrame();
        var frame=BrutalityRuntime.For(session.World)?.WoundFrameAt(session.ShownTick);
        if(frame is null) return;
        owners.Clear();
        foreach(var (owner,state) in frame.Characters)
        {
            if(RenderBridge.NativeDrawKey(host,owner) is { } key) owners[key]=(state,0);
            foreach(int piece in pieceKeys)
                if(RenderBridge.FragmentDrawKey(session.World,owner,piece) is { } shown) owners[shown]=(state,piece);
        }
        used.Clear();
        // The chest hole's matrix comes first; the legacy 36-matrix layout follows unchanged.
        Span<Matrix4x4> payload=stackalloc Matrix4x4[NativeSurfaceShaders.HolePaletteBones];
        var prefix=payload[1..];
        for(int i=0;i<list.Items.Count;i++)
        {
            var item=list.Items[i];
            if (RenderBridge.IsPresentationHeld(session.World, item.Key))
            { if (item.RuntimeShaderData is not null) { used.Add(item.Key); masks.Touch(item.Key); } continue; }
            if(item.Geometry is not { Shaders:not null } geometry || !owners.TryGetValue(item.Key,out var owner)
                || CharacterGeometryAdapter.SurfaceFor(geometry) is not { } info
                || resources.Rigs.For(item.Appearance) is not { } setup) continue;
            used.Add(item.Key);
            bool ordinary=HasOrdinaryMarks(owner.Piece);
            var marks=ordinary ? Marks(owner.State.Wounds,item.Appearance,session.ShownTick,session.World.TickSeconds) : Array.Empty<NativeWoundMark>();
            long maskStarted = RenderPhaseTrace.Start();
            uint index=ordinary ? masks.Update(item.Key,setup.Rig,info.Skin,marks) : 0;
            RenderPhaseTrace.End(RenderPhase.Masks, maskStarted);
            Pack(setup.Rig,owner.State,owner.Piece,index,ordinary ? masks.Spheres(item.Key) : null,ordinary ? masks.Cuts(item.Key) : null,info.HeadFrame?.Packed ?? default,prefix);
            SetBloodColour(prefix, unchecked((uint)item.Appearance.Race.GetInt("blood colour", 0x800000)));
            var words=MemoryMarshal.Cast<Matrix4x4,uint>(prefix);
            words[17]|=((uint)item.Appearance.HiddenLimbs&15u)<<1;
            if(AppliesHeadPlane(owner.State, owner.Piece) && owner.State.Head is { } headCut && resources.Mesh(item.Appearance.BodyMesh) is { } body)
            {
                var plane=headPlanes.For(body,setup.Rig,item.Appearance,headCut.Plane);
                prefix[3]=TorsoFrame(plane,owner.Piece==2 ? 1 : -1,2);
                var packedRig=PackedRig(setup.Rig);
                words[3*16+8]|=packedRig.HeadLow;
                words[3*16+9]|=packedRig.HeadHigh;
            }
            payload[0]=ChestHole(setup.Rig,owner.State,owner.Piece,words);
            if(!payloads.TryGetValue(item.Key,out var cached)
                || !MemoryMarshal.AsBytes(payload).SequenceEqual(MemoryMarshal.AsBytes(cached.Prefix.AsSpan())))
            {
                var saved=payload.ToArray();
                cached=(saved,new RuntimeCharacterShaderData(saved));
                payloads[item.Key]=cached;
            }
            list.Items[i]=item with { RuntimeShaderData=cached.Data };
        }
        if (!reportedDraws && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is not null) { Console.WriteLine($"Brutality native payloads: {used.Count}/{list.Items.Count}, states={frame.Characters.Count}, shaders={list.Items.Count(item=>item.Geometry?.Shaders is not null)}"); foreach (var drawn in list.Items)
            if (drawn.Bones is { AbsolutePositions: true } pose && resources.Rigs.For(drawn.Appearance) is { } rigSetup)
            {
                var placement = drawn.World ?? CharacterRigs.Placement(rigSetup, drawn.Position, drawn.Yaw, drawn.Scale);
                foreach (string name in new[] { "Bip01 L Foot", "Bip01 R Foot" })
                    if (rigSetup.Rig.Handle(name) is { } bone && pose.Set[bone])
                        Console.WriteLine($"Brutality native support: key={drawn.Key} {name} world={Vector3.Transform(pose.Position[bone], placement)}");
            }
            foreach (var drawn in list.Items)
                if (owners.TryGetValue(drawn.Key, out var owner) && owner.Piece is 2 or 3 && drawn.Geometry?.Parts is { } parts
                    && resources.Rigs.For(drawn.Appearance) is { } rigSetup)
                {
                    int brainVertices = 0, sectionVertices = 0; float minimumPixels = 0;
                    foreach (var part in parts)
                        if (part.Mesh is { } mesh && part.Part.Field is "anatomy" or "wound-section")
                        {
                            minimumPixels = MathF.Max(minimumPixels, part.MinimumScreenPixels);
                            foreach (var modelPart in Model.Build(mesh, rigSetup.Rig.BoneCount, preserveRawVertexChannels: true).Parts)
                                foreach (uint index in modelPart.Indices)
                                {
                                    var vertex = modelPart.Vertices[index];
                                    if (part.Part.Field == "anatomy" && vertex.Tangent.W == 6) brainVertices++;
                                    if (part.Part.Field == "wound-section" && vertex.Uv.X == NativeSectionGeometry.BrainSectionBand) sectionVertices++;
                                }
                        }
                    Console.WriteLine($"Brutality native head: piece={owner.Piece} brain={brainVertices} verts/{brainVertices / 3} tris, brain-section={sectionVertices} verts/{sectionVertices / 3} tris, minimum-pixels={minimumPixels:F2}");
                }
            reportedDraws=true; }
        stalePayloads.Clear();
        foreach(long key in payloads.Keys) if(!used.Contains(key)) stalePayloads.Add(key);
        foreach(long key in stalePayloads) payloads.Remove(key);
    }

    internal static Matrix4x4[] Pack(SkeletonRig rig,BrutalityActorView state,int piece,uint mask,
        Vector4[]? spheres,Vector4[]? cuts,Matrix4x4 head)
    {
        var prefix=new Matrix4x4[NativeSurfaceShaders.WoundPaletteBones];
        Pack(rig,state,piece,mask,spheres,cuts,head,prefix);
        return prefix;
    }

    internal static void Pack(SkeletonRig rig,BrutalityActorView state,int piece,uint mask,
        Vector4[]? spheres,Vector4[]? cuts,Matrix4x4 head,Span<Matrix4x4> destination)
    {
        if(destination.Length<NativeSurfaceShaders.WoundPaletteBones) throw new ArgumentException("The surface prefix needs 36 matrices.",nameof(destination));
        var prefix=destination[..NativeSurfaceShaders.WoundPaletteBones];
        prefix.Clear();
        var words=MemoryMarshal.Cast<Matrix4x4,uint>(prefix);
        words[16]=mask;
        words[BloodColourWord]=0x800000u;
        words[17]=state.SevereHead.RendererFlags;
        if(state.Torso is not null || state.Wounds.BellyHits>=10) words[17]|=1u<<5;
        if(piece is >=10 and <=13) words[19]=(uint)((piece-9)<<8)|255u;
        var packedRig=PackedRig(rig);
        uint limbMask=0;
        for(int index=0;index<state.Limbs.Count;index++)
        {
            var limb=state.Limbs[index];
            if(!packedRig.TryPlane(limb,out var plane)) continue;
            MemoryMarshal.Cast<Matrix4x4,Vector4>(prefix[..1])[limb.LimbSlot]=plane;
            limbMask|=1u<<limb.LimbSlot;
            words[17]|=1u<<(limb.LimbSlot+1);
        }
        words[28]=limbMask;
        packedRig.Groups.AsSpan().CopyTo(words[20..28]);
        prefix[2]=head;
        if(piece is 0 or 1 && state.Torso is { } torso)
            prefix[3]=TorsoFrame(torso.Plane,piece==1 ? -1 : 1,torso.FullBody ? 1 : 0);
        if(spheres is not null) spheres.AsSpan(0,Math.Min(64,spheres.Length)).CopyTo(MemoryMarshal.Cast<Matrix4x4,Vector4>(prefix.Slice(4,16)));
        if(cuts is not null) cuts.AsSpan(0,Math.Min(64,cuts.Length)).CopyTo(MemoryMarshal.Cast<Matrix4x4,Vector4>(prefix.Slice(20,16)));
    }

    /// <summary>The hole's prefix matrix, marked in the header, for the retained body only: a cleaved torso keeps no hole.</summary>
    internal static Matrix4x4 ChestHole(SkeletonRig rig,BrutalityActorView state,int piece,Span<uint> headerWords)
    {
        if(piece!=0 || CharacterGeometryAdapter.ShownHole(state) is not { } hole) return default;
        if(ChestHoleFrame.Resolve(rig,hole) is not { } frame) return default;
        headerWords[ChestHoleWord]|=NativeSurfaceShaders.ChestHoleHeaderBit;
        return frame.Packed;
    }

    internal static void SetBloodColour(Span<Matrix4x4> prefix, uint colour) =>
        MemoryMarshal.Cast<Matrix4x4,uint>(prefix)[BloodColourWord] = colour & 0xFFFFFF;

    internal static bool HasOrdinaryMarks(int piece) => piece is 0 or 1;

    internal static bool AppliesHeadPlane(BrutalityActorView state, int piece) => state.Head is not null && (piece is 2 or 3 || piece == 0 && state.Torso is null && state.Head.Value.Mode == HeadCutMode.AttachedSplit);

    internal static Matrix4x4 TorsoFrame(Vector4 plane,int side,int mode) => new(
        plane.X,plane.Y,plane.Z,plane.W,
        side,mode==2 ? 1.1f : 2.8f,mode,0,
        0,0,0,0,0,0,0,0);

    internal static IReadOnlyList<NativeWoundMark> Marks(BodyWoundState state,CharacterAppearance look,long tick,double tickSeconds)
    {
        if(state.Entries.Count==0) return Array.Empty<NativeWoundMark>();
        bool thin=look.Race.Name.Contains("Hive",StringComparison.OrdinalIgnoreCase)||look.Race.Name.Contains("Drone",StringComparison.OrdinalIgnoreCase);
        return markCache.GetValue(state,static state=>new MarkProjection(state)).Get(thin,tick,tickSeconds);
    }

    static IReadOnlyList<NativeWoundMark> BuildMarks(IReadOnlyList<BodyWoundEntry> entries,bool thin)
    {
        var marks=new List<NativeWoundMark>(entries.Count);
        foreach(var entry in entries)
        {
            var w=entry.Mark;
            float turn=(w.Age*2.399963f%MathF.Tau-MathF.PI)/MathF.PI*.35f,c=MathF.Cos(turn),s=MathF.Sin(turn);
            float cosine=w.Shape.Cosine*c-w.Shape.Sine*s,sine=w.Shape.Cosine*s+w.Shape.Sine*c;
            const float flow=0;
            string a,b; float t=w.T; Vector3 side=w.Side;
            if(entry.Region>=0)
            {
                float off=w.Age*37%7/3f-1;
                a="Bip01 Pelvis";b="Bip01 Neck";t=(entry.Region==0 ? .7f : entry.Region==1 ? .35f : .5f)+off*.13f;
                side=Vector3.Normalize(new(off*.95f,0,entry.Region==2 ? -1 : 1));
            }
            else if(w.Segment==9) { a="Bip01 Pelvis";b="Bip01 Neck"; }
            else if(w.Segment==8) { a="Bip01 Neck";b="Bip01 Head";t=2.2f+w.T;side=new(w.Age%2==0 ? .9f : -.9f,.5f,.2f*w.Side.Z); }
            else
            {
                string lr=w.Segment<2 || w.Segment is 4 or 5 ? "L" : "R";
                bool lower=(w.Segment&1)!=0;
                var (first,second)=w.Segment<4 ? lower ? ("Forearm","Hand") : ("UpperArm","Forearm") : lower ? ("Calf","Foot") : ("Thigh","Calf");
                a=$"Bip01 {lr} {first}";b=$"Bip01 {lr} {second}";
            }
            marks.Add(new(a,b,t,side,cosine,sine,w.Shape.Aspect,w.Shape.Depth,w.Size*(thin ? .55f : 1),flow,(int)w.Shape.Kind,w.Shape.WeaponCategory));
        }
        return marks;
    }
}
