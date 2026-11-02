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
    public void Installed_cut_halves_keep_visible_skin_at_the_palm_through_walk_jog_and_turning(TorsoBisectionStyle style,int direction)
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
                    int hand=Assert.IsType<int>(rig.Handle("Bip01 L Hand"));
                    Vector3? Bone(string name)=>rig.Handle(name)is{}h?scratch.DerivedPosition[h]:null;
                    var localPalm=CarryPose.PalmPoint(scratch.DerivedPosition[hand],Bone("Bip01 L Finger0")??Bone("Bip01 L Finger1"),Bone("Bip01 L Forearm"));
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
                        float gap=VisibleContactGap(source,groups,new(cut.Plane,side,cut.FullBody),skin,placement,palm,pose.Components!);
                        Assert.True(gap<.005f,$"{style}/{side}/{gait.Name}/{phase}: palm/skin gap {gap:F6}");
                        var both=Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(physics.SaveState(id)).Torso);
                        var piece=side>0?both.Positive:both.Negative;
                        var partial=definitions[side];
                        int component=pose.Components![partial.Bodies[partial.Root].Bone];
                        var heldBodies=Enumerable.Range(0,partial.Bodies.Length).Where(b=>pose.Components[partial.Bodies[b].Bone]==component).ToArray();
                        float mass=heldBodies.Sum(b=>partial.Bodies[b].Mass);
                        float centreY=heldBodies.Sum(b=>piece.Pose[b].Position.Y*partial.Bodies[b].Mass)/mass;
                        Assert.True(centreY<palm.Y,$"{style}/{side}/{gait.Name}/{phase}: held mass {centreY:F4} must hang below palm {palm.Y:F4}");
                    }
                }
        }
        finally{rig.Return(scratch);}
    }

    static float VisibleContactGap(Vertex[] source,uint[] groups,CharacterTorsoCut cut,Matrix4x4[] skin,Matrix4x4 placement,Vector3 palm,int[] components)
    {
        float nearest=float.MaxValue;
        for(int i=0;i+2<source.Length;i+=3)
        {
            var a=source[i];var b=source[i+1];var c=source[i+2];
            var p=new[]{Vector3.Transform(Skin(a,skin),placement),Vector3.Transform(Skin(b,skin),placement),Vector3.Transform(Skin(c,skin),placement)};
            var bands=new[]{TorsoCutSurface.Band(a.Position,TorsoCutSurface.Group(a,groups),cut),TorsoCutSurface.Band(b.Position,TorsoCutSurface.Group(b,groups),cut),TorsoCutSurface.Band(c.Position,TorsoCutSurface.Group(c,groups),cut)};
            var polygon=new List<(Vector3 Position,float Band)>{(p[0],bands[0]),(p[1],bands[1]),(p[2],bands[2])};
            var kept=new List<Vector3>();
            for(int k=0;k<3;k++)
            {
                var x=polygon[k];var y=polygon[(k+1)%3];
                if(x.Band>=0)kept.Add(x.Position);
                if((x.Band<0)!=(y.Band<0))kept.Add(Vector3.Lerp(x.Position,y.Position,x.Band/(x.Band-y.Band)));
            }
            for(int k=1;k+1<kept.Count;k++)nearest=MathF.Min(nearest,FoliageLodBuilder.DistanceSquared(palm,kept[0],kept[k],kept[k+1]));
        }
        return MathF.Sqrt(nearest);
    }
}