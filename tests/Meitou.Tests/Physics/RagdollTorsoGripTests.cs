using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using Scene = Meitou.Tests.Physics.RagdollTorsoPhysicsTests.Scene;

namespace Meitou.Tests.Physics;

public class RagdollTorsoGripTests
{
    [Theory]
    [InlineData(0, -1)] [InlineData(0, 1)]
    [InlineData(1, -1)] [InlineData(1, 1)]
    [InlineData(2, -1)] [InlineData(2, 1)]
    public void A_retained_half_reuses_the_original_whole_body_contact_while_its_carrier_moves_and_turns(int style, int side)
    {
        var plane = style switch { 0 => new Vector4(0, 1, 0, -10), 1 => new(1, 0, 0, 0.25f), _ => new(0.6f, 0.7f, 0.2f, -7.7f) };
        plane /= new Vector3(plane.X, plane.Y, plane.Z).Length();
        var cut = new TorsoBisectionState(plane, style != 0);
        using var scene = new Scene(cut: cut);
        scene.World.RunTicks(240);
        var own = Assert.IsType<RagdollDefinition>(RagdollCut.Build(scene.SourceDefinition, plane, side, cut.FullBody));
        var other = Assert.IsType<RagdollDefinition>(RagdollCut.Build(scene.SourceDefinition, plane, -side, cut.FullBody));
        var before = State(-side).Pose[other.Root].Position;
        var carrierCold = new CharacterCold { Name = "Grip carrier", Appearance = scene.Cold.Appearance, Race = Human, Medical = Healthy(), Carrying = scene.Id, CarryingTorsoSide = side > 0 ? 1 : 0 };
        var carrier = scene.World.Characters.Spawn(new() { Position = new(100, 0, 100), Health = 100 }, carrierCold, scene.World.Tick);
        if (side > 0) scene.Cold.TorsoPositiveCarriedBy = carrier;
        else scene.Cold.CarriedBy = carrier;
        int heldBody = -1;
        Vector3 localGrip = default;
        foreach (var (position, yaw) in new[] { (new Vector3(100,0,100),0f), (new Vector3(110,0,104),0.8f), (new Vector3(106,2,115),-1.3f) })
        {
            scene.World.Characters.Place(carrier.Slot, position, yaw);
            if (side < 0) scene.World.Characters.Place(scene.Id.Slot, position, yaw);
            scene.World.RunTick();
            var scratch = scene.Setup.Rig.Rent();
            Vector3 palm;
            RigidTransform[] reference;
            try
            {
                scratch.PoseDerived(scene.Setup.Shape, []);
                palm = CarryPose.PalmPoint(scratch.DerivedPosition[3], forearm: scratch.DerivedPosition[2]);
                palm = Vector3.Transform(palm, CharacterRigs.Placement(scene.Setup, position, yaw, 1));
                var attachment=side>0?scene.Snapshot.TorsoPositiveCarry:scene.Snapshot.Carry;
                var heldPlacement=(attachment??CarryPose.Fallback).World(position,yaw);
                var placement=CharacterRigs.Placement(scene.Setup,heldPlacement.Position,heldPlacement.Rotation,1);
                var rotation=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with{M41=0,M42=0,M43=0}));
                reference=own.Bodies.Select(body=>body.BoneInBody.Inverse().Then(new RigidTransform(
                    Vector3.Transform(scratch.DerivedPosition[body.Bone],placement),Quaternion.Normalize(rotation*scratch.DerivedRotation[body.Bone])))).ToArray();
            }
            finally { scene.Setup.Rig.Return(scratch); }
            palm = ReferenceContact(scene, side, carrier);
            var held = State(side);
            var distances = own.Bodies.Select((body, i) => Distance(body, held.Pose[i].Inverse().Apply(palm))).ToArray();
            var connected = new HashSet<int> { own.Root };
            bool changed;
            do
            {
                changed = false;
                foreach (var joint in own.Joints)
                    if (connected.Contains(joint.A) != connected.Contains(joint.B)) changed |= connected.Add(joint.A) | connected.Add(joint.B);
            } while (changed);
            foreach(int b in connected.Where(b => own.Bodies[b].Name is not ("Bip01 L Forearm" or "Bip01 R Forearm" or "Bip01 Head")))
            {
                Assert.True(MathF.Abs(Quaternion.Dot(reference[b].Rotation,held.Pose[b].Rotation))>.99999f);
                Assert.InRange(Vector3.Distance(reference[b].Position-reference[own.Root].Position,held.Pose[b].Position-held.Pose[own.Root].Position),0,.001f);
            }
            Assert.True(distances.Min() < 0.001f, $"style={style},side={side}: nearest surface {distances.Min()}, support {palm}, actual root {held.Pose[own.Root]}, expected root {reference[own.Root]}");
            if (heldBody < 0)
            {
                heldBody = Array.IndexOf(distances, distances.Min());
                localGrip = held.Pose[heldBody].Inverse().Apply(palm);
            }
            else Assert.InRange(Vector3.Distance(palm, held.Pose[heldBody].Apply(localGrip)), 0, 0.001f);
            Assert.InRange(Vector3.Distance(before, State(-side).Pose[other.Root].Position), 0, 0.1f);
        }
        SavedRagdoll State(int half)
        {
            var both = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id)).Torso);
            return half > 0 ? both.Positive : both.Negative;
        }
    }

    [Fact]
    public void Clipped_skin_grips_use_exact_original_triangle_barycentrics_and_keep_the_appearance_morph()
    {
        using var scene = new Scene();
        var mesh = Mesh([new(-2,-2,0), new(2,-2,0), new(0,2,0)], [0,0,0]);
        var morph = new OgrePose { Name = "grip_morph", Target = 1 };
        for(uint i=0;i<3;i++) morph.Vertices.Add(new(i,new(0,0,1),default));
        mesh.Poses.Add(morph);
        scene.Cold.Appearance!.PoseWeights["grip_morph"] = 1;
        var surface = RagdollSkinSurface.Read(mesh);
        var skin = Enumerable.Repeat(Matrix4x4.Identity, 4).ToArray();
        var grip = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance, scene.Setup.Rig, new(1,0,0,0), 1, true,
            [0,0,0,0], 0, scene.SourceDefinition, skin, new(-1,0,2)));
        var point = grip.Posed(skin);
        Assert.InRange(point.X, 0.9999f, 1.0001f);
        Assert.InRange(MathF.Abs(point.Y),0,.0001f);
        Assert.InRange(point.Z, 0.9999f, 1.0001f);
        skin[0] = Matrix4x4.CreateScale(1.5f, 0.7f, 2) * Matrix4x4.CreateRotationY(0.8f) * Matrix4x4.CreateTranslation(7,4,-3);
        Assert.InRange(Vector3.Distance(grip.Posed(skin), Vector3.Transform(point, skin[0])), 0, 0.0001f);
    }

    [Fact]
    public void A_grip_cannot_select_a_loose_arm_even_when_that_skin_is_closer_to_the_hand()
    {
        using var scene = new Scene();
        var mesh = Mesh([new(-1,0,0),new(1,0,0),new(0,2,0),new(-1,0,5),new(1,0,5),new(0,2,5)], [0,0,0,2,2,2]);
        var surface = RagdollSkinSurface.Read(mesh);
        var skin = Enumerable.Repeat(Matrix4x4.Identity,4).ToArray();
        var grip = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!, scene.Setup.Rig, new(0,1,0,100),1,true,
            [0,0,1,1],0,scene.SourceDefinition,skin,new(0,1,5)));
        Assert.InRange(MathF.Abs(grip.Posed(skin).Z),0,0.0001f);
    }

    [Fact]
    public void A_partial_stump_grip_clips_at_its_actual_bind_height()
    {
        using var scene = new Scene();
        var surface=RagdollSkinSurface.Read(Mesh([new(-1,11,0),new(1,11,0),new(0,14,0)],[2,2,2]));
        var skin=Enumerable.Repeat(Matrix4x4.Identity,4).ToArray();
        var grip=Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!,scene.Setup.Rig,new(0,1,0,100),1,true,
            [0,0,0,0],0,scene.SourceDefinition,skin,new(0,11,1),[13.8f,-1,-1,-1]));
        Assert.InRange(grip.Posed(skin).Y,13.8999f,13.9001f);
    }

    [Fact]
    public void Ordinary_carried_bodies_keep_the_existing_whole_body_placement()
    {
        using var scene=new Scene(torso:false);
        var carrierCold=new CharacterCold {Name="Ordinary carrier",Appearance=scene.Cold.Appearance,Race=Human,Medical=Healthy(),Carrying=scene.Id};
        var carrier=scene.World.Characters.Spawn(new(){Position=new(20,0,20),Health=100},carrierCold,0);
        scene.Cold.CarriedBy=carrier;
        scene.World.Characters.Place(scene.Id.Slot,new(20,0,20),0);
        scene.World.RunTick();
        Assert.Null(scene.Physics.PublishTorso(scene.Id));
        var pose=Assert.IsType<BonePose>(scene.Physics.Publish(scene.Id));
        // CarryMode overrides only dangling forearms/head; the root remains in the original animation, not in pose.Position[0].
        Assert.False(pose.Set[0]);
        var shown=scene.Snapshot;
        var held=(shown.Carry??CarryPose.Fallback).World(shown.Position,shown.Yaw);
        Assert.Equal(new Vector3(22,14,20),held.Position);
        var scratch=scene.Setup.Rig.Rent();
        try
        {
            scratch.PoseDerived(scene.Setup.Shape,[],pose);
            var renderedRoot=Vector3.Transform(scratch.DerivedPosition[0],CharacterRigs.Placement(scene.Setup,held.Position,held.Rotation,1));
            Assert.Equal(new Vector3(22,25,20),renderedRoot);
        }
        finally{scene.Setup.Rig.Return(scratch);}
    }

    [Fact]
    public void A_root_dominant_skin_contact_accounts_for_its_unmoved_loose_component_weights()
    {
        using var scene = new Scene();
        var mesh = Mesh([new(-1,0,0),new(1,0,0),new(0,2,0)],[0,0,0]);
        mesh.SubMeshes[0].BoneAssignments.Clear();
        for(uint i=0;i<3;i++)
        {
            mesh.SubMeshes[0].BoneAssignments.Add(new(i,0,.75f));
            mesh.SubMeshes[0].BoneAssignments.Add(new(i,2,.25f));
        }
        var skin = Enumerable.Repeat(Matrix4x4.Identity,4).ToArray();
        var grip = Assert.IsType<RagdollSkinSurface.Grip>(RagdollSkinSurface.Read(mesh).FindGrip(scene.Cold.Appearance!,scene.Setup.Rig,
            new(0,1,0,100),1,true,[0,0,1,1],0,scene.SourceDefinition,skin,new(0,1,1)));
        Assert.InRange(grip.RootShare,.7499f,.7501f);
        var before=grip.Posed(skin);
        var wanted=new Vector3(4,3,-2);
        skin[0]=Matrix4x4.CreateTranslation(wanted/grip.RootShare);
        Assert.InRange(Vector3.Distance(grip.Posed(skin),before+wanted),0,.0001f);
        Assert.Equal(Matrix4x4.Identity,skin[2]);
    }
    [Fact]
    public void Transverse_head_skin_uses_the_geometric_cut_band_like_the_renderer()
    {
        using var scene=new Scene();
        var skeleton=new OgreSkeleton();
        skeleton.Bones.Add(new(){Handle=0,Name="Bip01"});
        skeleton.Bones.Add(new(){Handle=1,Parent=0,Name="Bip01 Head",Position=new(0,16,0)});
        var rig=new SkeletonRig(skeleton);
        var definition=RagdollCutTests.Definition([RagdollCutTests.Box("Bip01",0,Vector3.Zero,Vector3.One),RagdollCutTests.Box("Bip01 Head",1,new(0,16,0),new(2,4,2))]);
        var surface=RagdollSkinSurface.Read(Mesh([new(-1,14,0),new(1,14,0),new(0,18,0)],[1,1,1]));
        var grip=Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!,rig,new(0,1,0,-17.8f),1,false,
            [0,0],0,definition,[Matrix4x4.Identity,Matrix4x4.Identity],new(0,15,1)));
        Assert.InRange(grip.Posed([Matrix4x4.Identity,Matrix4x4.Identity]).Y,17.8999f,17.9001f);
    }
    internal static Vector3 ReferenceContact(Scene scene, int side, CharacterId carrier)
    {
        var scratch = scene.Setup.Rig.Rent();
        try
        {
            scratch.PoseDerived(scene.Setup.Shape, []);
            // Physics reads simulation coordinates. DrawnPosition can lower a healthy snapshot to the synthetic water surface.
            ref readonly var holder = ref scene.World.Characters.Previous[carrier.Slot];
            var palm = Vector3.Transform(CarryPose.PalmPoint(scratch.DerivedPosition[3], forearm: scratch.DerivedPosition[2]),
                CharacterRigs.Placement(scene.Setup, holder.Position, holder.Yaw, 1));
            var attach = (side > 0 ? scene.Snapshot.TorsoPositiveCarry : scene.Snapshot.Carry) ?? CarryPose.Fallback;
            // This minimal scene has no CarrySystem.KeepLinks: an ordinary negative owner keeps its own hot node.
            ref readonly var owner = ref scene.World.Characters.Previous[scene.Id.Slot];
            var held = side > 0 ? attach.World(holder.Position, holder.Yaw) : attach.World(owner.Position, owner.Yaw);
            var placement = CharacterRigs.Placement(scene.Setup, held.Position, held.Rotation, 1);
            var turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement with { M41 = 0, M42 = 0, M43 = 0 }));
            var source = scene.SourceDefinition.Bodies[scene.SourceDefinition.Root];
            var root = source.BoneInBody.Inverse().Then(new RigidTransform(Vector3.Transform(scratch.DerivedPosition[source.Bone], placement),
                Quaternion.Normalize(turn * scratch.DerivedRotation[source.Bone])));
            // The fixture's ordinary contact is on its sole axial box, rather than on the carrier's biceps.
            return root.Apply(Vector3.Clamp(root.Inverse().Apply(palm), -source.Size * .5f, source.Size * .5f));
        }
        finally { scene.Setup.Rig.Return(scratch); }
    }

    internal static void AssertReferenceContact(Scene scene, int side, CharacterId carrier)
    {
        var cut = scene.Cold.TorsoBisection!.Value;
        var definition = Assert.IsType<RagdollDefinition>(RagdollCut.Build(scene.SourceDefinition, cut.Plane, side, cut.FullBody));
        var saved = Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id)).Torso);
        var held = side > 0 ? saved.Positive : saved.Negative;
        var contact = ReferenceContact(scene, side, carrier);
        Assert.InRange(Distance(definition.Bodies[definition.Root], held.Pose[definition.Root].Inverse().Apply(contact)), 0, .001f);
    }
    [Fact]
    public void Middle_contact_uses_the_full_3D_extent_instead_of_grabbing_a_horizontal_endpoint()
    {
        using var scene=new Scene();
        var surface=RagdollSkinSurface.Read(Mesh([new(1,-1,-10),new(1,1,-10),new(1,1,10),new(1,-1,-10),new(1,1,10),new(1,-1,10)],[0,0,0,0,0,0]));
        var skin=Enumerable.Repeat(Matrix4x4.Identity,4).ToArray();
        var grip=Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!,scene.Setup.Rig,new(0,1,0,100),1,true,
            [0,0,0,0],0,scene.SourceDefinition,skin,new(1,0,10)));
        Assert.InRange(Vector3.Distance(grip.Posed(skin),new(1,0,0)),0,.0001f);
    }
    [Fact]
    public void Middle_reference_rotates_with_the_original_carried_model_frame()
    {
        using var scene=new Scene();
        var surface=RagdollSkinSurface.Read(Mesh([new(-2,-2,0),new(2,-2,0),new(0,2,0)],[0,0,0]));
        var frame=Matrix4x4.CreateRotationZ(.7f)*Matrix4x4.CreateRotationY(.8f)*Matrix4x4.CreateTranslation(7,4,-3);
        var skin=Enumerable.Repeat(frame,4).ToArray();
        var grip=Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!,scene.Setup.Rig,new(0,1,0,100),1,true,
            [0,0,0,0],0,scene.SourceDefinition,skin,new(20,4,-3),boundsFrame:frame));
        Assert.InRange(Vector3.Distance(grip.Posed(skin),Vector3.Transform(Vector3.Zero,frame)),0,.0001f);
    }
    [Fact]
    public void The_original_material_contact_is_reused_exactly_when_its_triangle_survives_the_cut()
    {
        using var scene = new Scene();
        var surface = RagdollSkinSurface.Read(Mesh([new(-2,-2,0),new(2,-2,0),new(0,2,0)],[0,0,0]));
        var skin = Enumerable.Repeat(Matrix4x4.Identity, 4).ToArray();
        var look = scene.Cold.Appearance!;
        var reference = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(look, scene.Setup.Rig, default, 1, true,
            [0,0,0,0], 0, scene.SourceDefinition, skin, new(1,-1,2), query: new(NearestToPoint:true, AxialOnly:true, Uncut:true)));
        var kept = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(look, scene.Setup.Rig, new(0,1,0,1.5f), 1, true,
            [0,0,0,0], 0, scene.SourceDefinition, skin, new(0,100,0), query: new(NearestToPoint:true, AxialOnly:true, Retained:reference)));
        Assert.Equal(reference.Triangle, kept.Triangle);
        Assert.Equal(reference.Barycentric, kept.Barycentric);
        Assert.Equal(reference.Posed(skin), kept.Posed(skin));
    }

    [Fact]
    public void A_removed_seat_moves_to_the_nearest_retained_trunk_instead_of_a_nearer_hand_endpoint()
    {
        using var scene = new Scene();
        var surface = RagdollSkinSurface.Read(Mesh([new(-1,0,0),new(1,0,0),new(0,2,0),new(-1,0,4),new(1,0,4),new(0,2,4)],[0,0,0,2,2,2]));
        var skin = Enumerable.Repeat(Matrix4x4.Identity,4).ToArray();
        var selected = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(scene.Cold.Appearance!,scene.Setup.Rig,default,1,true,
            [0,0,0,0],0,scene.SourceDefinition,skin,new(0,1,4),query:new(NearestToPoint:true,AxialOnly:true,Uncut:true)));
        Assert.InRange(MathF.Abs(selected.Posed(skin).Z),0,.0001f);
    }

    [Fact]
    public void A_carried_retained_forearm_uses_ordinary_carry_dynamics_and_reseeds_on_a_new_holder()
    {
        using var scene = new Scene(cut: new(new(0,1,0,-10), false));
        scene.World.RunTicks(120);
        var holderCold = new CharacterCold { Name="First carrier",Appearance=scene.Cold.Appearance,Race=Human,Medical=Healthy(),Carrying=scene.Id,CarryingTorsoSide=1 };
        var holder = scene.World.Characters.Spawn(new(){Position=new(100,0,100),Health=100},holderCold,scene.World.Tick);
        scene.Cold.TorsoPositiveCarriedBy=holder;
        scene.World.RunTick();
        var first = State();
        var cutDefinition=Assert.IsType<RagdollDefinition>(RagdollCut.Build(scene.SourceDefinition,new(0,1,0,-10),1,false));
        int forearm=Array.FindIndex(cutDefinition.Bodies,b=>b.Name=="Bip01 L Forearm");
        Assert.True(forearm>=0);
        scene.World.Characters.Place(holder.Slot,new(104,0,100),0);
        scene.World.RunTick();
        var dangling=State();
        Assert.InRange(Vector3.Distance(first.Pose[cutDefinition.Root].Position+new Vector3(4,0,0),dangling.Pose[cutDefinition.Root].Position),0,.0001f);
        var beforeLocal=first.Pose[forearm].Position-first.Pose[cutDefinition.Root].Position;
        var afterLocal=dangling.Pose[forearm].Position-dangling.Pose[cutDefinition.Root].Position;
        Assert.True(Vector3.Distance(beforeLocal,afterLocal)>.05f
            || MathF.Abs(Quaternion.Dot(first.Pose[forearm].Rotation,dangling.Pose[forearm].Rotation))<.999f,
            $"Dynamic forearm should lag the moving torso: before {beforeLocal}, after {afterLocal}");
        var replacementCold=new CharacterCold{Name="Second carrier",Appearance=scene.Cold.Appearance,Race=Human,Medical=Healthy(),Carrying=scene.Id,CarryingTorsoSide=1};
        var replacement=scene.World.Characters.Spawn(new(){Position=new(300,0,100),Health=100},replacementCold,scene.World.Tick);
        holderCold.Carrying=CharacterId.None;
        scene.Cold.TorsoPositiveCarriedBy=replacement;
        scene.World.RunTick();
        var transferred=State();
        Assert.True(transferred.Pose[forearm].Position.X>280);
        Assert.InRange(Vector3.Distance(transferred.Pose[forearm].Position,transferred.Pose[cutDefinition.Root].Position),0,15);
        AssertReferenceContact(scene,1,replacement);

        SavedRagdoll State()=>Assert.IsType<SavedTorsoRagdoll>(Assert.IsType<SavedRagdoll>(scene.Physics.SaveState(scene.Id)).Torso).Positive;
    }

    static OgreMesh Mesh(Vector3[] positions,int[] bones)
    {
        var data=new OgreVertexData {VertexCount=(uint)positions.Length};
        data.Elements.Add(new(0,OgreVertexElementType.Float3,OgreVertexSemantic.Position,0,0));
        data.Buffers[0]=new(12,MemoryMarshal.AsBytes(positions.AsSpan()).ToArray());
        var sub=new OgreSubMesh {VertexData=data,Indices=new(false,Enumerable.Range(0,positions.Length).Select(i=>(uint)i).ToArray())};
        for(uint i=0;i<positions.Length;i++)sub.BoneAssignments.Add(new(i,(ushort)bones[i],1));
        var mesh=new OgreMesh();mesh.SubMeshes.Add(sub);return mesh;
    }
    static float Distance(RagdollBody body,Vector3 local)
    {
        if(body.Type==PhysicsShapeType.Box){var q=Vector3.Abs(local)-body.Size*0.5f;return MathF.Abs(Vector3.Max(q,Vector3.Zero).Length()+MathF.Min(MathF.Max(q.X,MathF.Max(q.Y,q.Z)),0));}
        if(body.Type is PhysicsShapeType.Sphere or PhysicsShapeType.Capsule){float length=body.Type==PhysicsShapeType.Capsule?body.Size.Y*.5f:0;return MathF.Abs(Vector3.Distance(local,new(0,Math.Clamp(local.Y,-length,length),0))-body.Size.X);}
        var points=RagdollCut.Points(body);
        var pool=new BepuUtilities.Memory.BufferPool();
        try
        {
            BepuPhysics.Collidables.ConvexHullHelper.ComputeHull(points,pool,out var hull);
            try
            {
                float nearest=float.MaxValue, outside=0;
                var centre=points.Aggregate(Vector3.Zero,(sum,p)=>sum+p)/points.Length;
                for(int f=0;f<hull.FaceStartIndices.Length;f++)
                {
                    hull.GetFace(f,out var face);
                    if(face.VertexCount<3)continue;
                    var a=points[face[0]];var b=points[face[1]];var c=points[face[2]];
                    var normal=Vector3.Normalize(Vector3.Cross(b-a,c-a));
                    if(Vector3.Dot(normal,a-centre)<0)normal=-normal;
                    float distance=Vector3.Dot(normal,local-a);
                    outside=MathF.Max(outside,distance);nearest=MathF.Min(nearest,MathF.Abs(distance));
                }
                return MathF.Max(nearest,outside);
            }
            finally{hull.Dispose(pool);}
        }
        finally{pool.Clear();}
    }
}