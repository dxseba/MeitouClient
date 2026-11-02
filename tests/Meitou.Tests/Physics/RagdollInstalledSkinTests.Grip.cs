using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Simulation.Saving;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld=Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    sealed class GripAnimations(CharacterAnimation carrier) : ITickSystem,IAnimationSource
    {
        public string Gait="";
        public float Time;
        public IReadOnlyList<AnimationLayer> Publish(CharacterAnimation animation,List<AnimationLayer> into)
        {
            into.Clear();
            if(ReferenceEquals(animation,carrier))
            {
                into.Add(new(Gait,Time,1));
                into.Add(new("carrying left arm only",0,1));
            }
            else into.Add(new(CarrySystem.CarriedClip,0,1));
            return into;
        }
    }

    [Theory,Slow]
    [InlineData(TorsoBisectionStyle.Transverse,2)]
    [InlineData(TorsoBisectionStyle.Vertical,1)]
    [InlineData(TorsoBisectionStyle.Oblique,2)]
    public void Installed_cut_halves_reuse_the_original_whole_body_seat_and_preserve_pinned_carry_pose_through_walk_jog_and_turning(TorsoBisectionStyle style,int direction)
    {
        var(rig,definition,model,setup,rigs,look)=Installed();
        var walk=rig.Skeleton.Animations.FirstOrDefault(a=>a.Name.StartsWith("walk",StringComparison.OrdinalIgnoreCase)&&!a.Name.Contains("carrying",StringComparison.OrdinalIgnoreCase));
        var jog=rig.Skeleton.Animations.FirstOrDefault(a=>a.Name.StartsWith("jog",StringComparison.OrdinalIgnoreCase)&&!a.Name.Contains("carrying",StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(walk);Assert.NotNull(jog);
        var carrierAnimation=new CharacterAnimation();
        var animation=new GripAnimations(carrierAnimation){Gait=walk.Name};
        using var physics=new RagdollSystem(rigs,new Flat(),BodyOptions.Default with{AdvancedRagdolls=true},_=>setup,(_,_,_)=>definition);
        using var world=new SimWorld(new WorldSettings{Seed=41},new OpenGroundWalkability((_,_)=>0),[animation,physics]);
        var cut=TorsoBisectionPolicy.StateForDirection(direction,1,Vector2.UnitX,-MathF.PI/2,style);
        var cold=new CharacterCold{Name="Grip skin corpse",Race=Human,Medical=Healthy(),Appearance=look,TorsoBisection=cut};
        cold.Medical.KillFromBisection();
        var id=world.Characters.Spawn(new(){Position=new(20,0,20)},cold,0);
        world.RunTicks(240);
        cold.Animation=new();
        var holders=new Dictionary<int,CharacterId>();
        foreach(int side in new[]{-1,1})
        {
            var holderCold=new CharacterCold{Name="Grip skin carrier",Appearance=look,Race=Human,Medical=Healthy(),Animation=carrierAnimation,Carrying=id,CarryingTorsoSide=side>0?1:0};
            holders[side]=world.Characters.Spawn(new(){Position=new(side>0?300:100,0,100),Health=100},holderCold,world.Tick);
        }
        cold.CarriedBy=holders[-1];cold.TorsoPositiveCarriedBy=holders[1];
        var bodySurface=RagdollSkinSurface.Read(OgreMeshReader.ReadFile(rigs.FindMesh(look.BodyMesh)!));
        var definitions=new Dictionary<int,RagdollDefinition>();
        foreach(int side in new[]{-1,1})definitions[side]=Assert.IsType<RagdollDefinition>(RagdollCut.Build(definition,cut.Plane,side,cut.FullBody,
            cut.FullBody?bodySurface.Bridges(rig,definition,cut.Plane,side):null));
        var source=model.Parts.SelectMany(part=>part.Indices.Select(i=>part.Vertices[i])).ToArray();
        var groups=new uint[8];
        foreach(var bone in rig.Skeleton.Bones)
            if(bone.Handle<64)groups[bone.Handle/8]|=(uint)(bone.Name=="Bip01 Jaw"?6:WoundMasks.BodyGroup(bone.Name))<<(bone.Handle%8*4);
        var originalContacts=new Dictionary<int,RagdollSkinSurface.Grip>();
        var scratch=rig.Rent();
        try
        {
            foreach(var gait in new[]{walk,jog})
                for(int phase=0;phase<4;phase++)
                {
                    animation.Gait=gait.Name;animation.Time=phase*gait.Length/4;
                    float yaw=phase*.65f;
                    foreach(int side in new[]{-1,1})world.Characters.Place(holders[side].Slot,new(side>0?300:100,0,100+phase*3),yaw);
                    world.Characters.Place(id.Slot,new(100,0,100+phase*3),yaw);
                    world.RunTick();
                    var layers=animation.Publish(carrierAnimation,[]).Where(l=>rig.Layer(rigs.Database,l.Name)is not null).Select(l=>new ActiveLayer(rig.Layer(rigs.Database,l.Name)!,l.Time,l.Weight)).ToList();
                    foreach(var(def,time)in setup.Fixed)layers.Add(new(def,time,1));
                    scratch.PoseDerived(setup.Shape,layers.ToArray());
                    int upperArm=Assert.IsType<int>(rig.Handle("Bip01 L UpperArm"));
                    int forearm=Assert.IsType<int>(rig.Handle("Bip01 L Forearm"));
                    int hand=Assert.IsType<int>(rig.Handle("Bip01 L Hand"));
                    Vector3? finger=rig.Handle("Bip01 L Finger0")is int f?scratch.DerivedPosition[f]:null;
                    var localPalm=CarryPose.PalmPoint(scratch.DerivedPosition[hand],finger,scratch.DerivedPosition[forearm]);
                    var shown=Assert.Single(world.Snapshot.Characters,c=>c.Id==id);
                    var halves=Assert.IsType<TorsoRagdollPose>(shown.TorsoRagdoll);
                    var held=(shown.Carry??CarryPose.Fallback).World(shown.Position,shown.Yaw);
                    var placement=CharacterRigs.Placement(setup,held.Position,held.Rotation,1);
                    foreach(int side in new[]{-1,1})
                    {
                        var holder=Assert.Single(world.Snapshot.Characters,c=>c.Id==holders[side]);
                        var palm=Vector3.Transform(localPalm,CharacterRigs.Placement(setup,holder.Position,holder.Yaw,1));
                        var pose=side>0?halves.Positive:halves.Negative;
                        var skin=new Matrix4x4[rig.BoneCount];
                        scratch.Pose(setup.Shape,[],skin,pose);
                        var attach=(side>0?shown.TorsoPositiveCarry:shown.Carry)??CarryPose.Fallback;
                        var referenceAt=attach.World(holder.Position,holder.Yaw);
                        var referencePlacement=CharacterRigs.Placement(setup,referenceAt.Position,referenceAt.Rotation,1);
                        var carryLayersForSeat=new List<ActiveLayer>();
                        if(rig.Layer(rigs.Database,CarrySystem.CarriedClip)is{}carryForSeat)carryLayersForSeat.Add(new(carryForSeat,0,1));
                        foreach(var(def,time)in setup.Fixed)carryLayersForSeat.Add(new(def,time,1));
                        var originalSkin=new Matrix4x4[rig.BoneCount];
                        scratch.Pose(setup.Shape,carryLayersForSeat.ToArray(),originalSkin);
                        for(int b=0;b<originalSkin.Length;b++)originalSkin[b]*=referencePlacement;
                        if(!originalContacts.TryGetValue(side,out var originalMaterial))
                        {
                            originalMaterial=Assert.IsType<RagdollSkinSurface.Grip>(bodySurface.FindGrip(look,rig,default,1,true,new int[rig.BoneCount],0,
                                definition,originalSkin,palm,query:new(NearestToPoint:true,AxialOnly:true,Uncut:true)));
                            originalContacts[side]=originalMaterial;
                        }
                        var originalContact=originalMaterial.Posed(originalSkin);
                        float gap=VisibleContactGap(source,groups,new(cut.Plane,side,cut.FullBody),skin,placement,originalContact,pose.Components!);
                        Assert.True(gap<.005f,$"{style}/{side}/{gait.Name}/{phase}: original whole seat/skin gap {gap:F6}");
                        var both=Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(physics.SaveState(id)).Torso);
                        var piece=side>0?both.Positive:both.Negative;
                        var partial=definitions[side];
                        int component=pose.Components![partial.Bodies[partial.Root].Bone];
                        var carryLayers=new List<ActiveLayer>();
                        if(rig.Layer(rigs.Database,CarrySystem.CarriedClip)is{}carried)carryLayers.Add(new(carried,0,1));
                        foreach(var(def,time)in setup.Fixed)carryLayers.Add(new(def,time,1));
                        var referenceScratch=rig.Rent();
                        try
                        {
                            referenceScratch.PoseDerived(setup.Shape,carryLayers.ToArray());
                            var referenceTurn=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(referencePlacement with{M41=0,M42=0,M43=0}));
                            var reference=partial.Bodies.Select(body=>body.BoneInBody.Inverse().Then(new RigidTransform(
                                Vector3.Transform(referenceScratch.DerivedPosition[body.Bone],referencePlacement),
                                Quaternion.Normalize(referenceTurn*referenceScratch.DerivedRotation[body.Bone])))).ToArray();
                            foreach(int b in Enumerable.Range(0,partial.Bodies.Length).Where(b=>pose.Components[partial.Bodies[b].Bone]==component
                                && partial.Bodies[b].Name is not ("Bip01 Head" or "Bip01 L Forearm" or "Bip01 R Forearm")))
                            {
                                Assert.True(MathF.Abs(Quaternion.Dot(reference[b].Rotation,piece.Pose[b].Rotation))>.99999f);
                                Assert.InRange(Vector3.Distance(reference[b].Position-reference[partial.Root].Position,piece.Pose[b].Position-piece.Pose[partial.Root].Position),0,.001f);
                            }
                        }
                        finally{rig.Return(referenceScratch);}
                    }
                }
        }
        finally{rig.Return(scratch);}
    }

    static float VisibleContactGap(Vertex[] source,uint[] groups,CharacterTorsoCut cut,Matrix4x4[] skin,Matrix4x4 placement,Vector3 palm,int[] components)
    {
        float nearest=float.MaxValue;
        int root=components[0];
        for(int i=0;i+2<source.Length;i+=3)
        {
            var a=source[i];var b=source[i+1];var c=source[i+2];
            var vertices=new[]{a,b,c};
            var votes=new Dictionary<int,float>();
            var shares=new float[3];
            for(int v=0;v<3;v++)for(int k=0;k<4;k++)
            {
                int bone=(int)(vertices[v].Bones>>(k*8)&255);
                if(bone>=components.Length||vertices[v].Weights[k]<=0)continue;
                int component=components[bone];
                votes[component]=votes.GetValueOrDefault(component)+vertices[v].Weights[k];
                if(component==root)shares[v]+=vertices[v].Weights[k];
            }
            if(votes.Count==0||votes.MaxBy(v=>v.Value).Key!=root)continue;
            var p=new[]{Vector3.Transform(Skin(a,skin),placement),Vector3.Transform(Skin(b,skin),placement),Vector3.Transform(Skin(c,skin),placement)};
            var bands=new[]{TorsoCutSurface.Band(a.Position,TorsoCutSurface.Group(a,groups),cut),TorsoCutSurface.Band(b.Position,TorsoCutSurface.Group(b,groups),cut),TorsoCutSurface.Band(c.Position,TorsoCutSurface.Group(c,groups),cut)};
            var polygon=new List<(Vector3 Position,float Band,float Root)>{(p[0],bands[0],shares[0]),(p[1],bands[1],shares[1]),(p[2],bands[2],shares[2])};
            Clip(v=>v.Band);Clip(v=>v.Root-.5f);
            var kept=polygon.Select(v=>v.Position).ToArray();
            void Clip(Func<(Vector3 Position,float Band,float Root),float> band)
            {
                var result=new List<(Vector3 Position,float Band,float Root)>();
                for(int k=0;k<polygon.Count;k++)
                {
                    var x=polygon[k];var y=polygon[(k+1)%polygon.Count];
                    float dx=band(x),dy=band(y);
                    if(dx>=0)result.Add(x);
                    if((dx<0)!=(dy<0))
                    {
                        float t=dx/(dx-dy);
                        result.Add((Vector3.Lerp(x.Position,y.Position,t),x.Band+(y.Band-x.Band)*t,x.Root+(y.Root-x.Root)*t));
                    }
                }
                polygon=result;
            }
            for(int k=1;k+1<kept.Length;k++)nearest=MathF.Min(nearest,FoliageLodBuilder.DistanceSquared(palm,kept[0],kept[k],kept[k+1]));
        }
        return MathF.Sqrt(nearest);
    }
}