using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Wounds;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Order-level torso carries on flat ground; no game install, renderer or user save is involved.</summary>
public sealed class TorsoCarryFixture : IDisposable
{
    public PopulationData Data { get; }
    public SimWorld World { get; }
    public TorsoCarryFixture()
    {
        var db = SyntheticTown.Database(anatomy: true, items: true);
        var patch = new FcsFile();
        var race = new FcsRecord { StringId = "1-t", RecordType = FcsRecordType.RACE, Name = "Testers" };
        race.Bools["carriable"] = true;
        patch.Records.Add(race);
        db.Apply(patch, "torso-carry-tests.mod");
        Data = SyntheticTown.Data(db);
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        var systems = SyntheticTown.Standard(Data, walk, StandardParts.Player | StandardParts.Movement | StandardParts.Body | StandardParts.Carry);
        World = new(new WorldSettings { Seed = 31 }, walk, systems.Systems);
        World.Player.Faction = Data.PlayerFaction;
        World.Player.Squad = 0;
        World.Squads.Add(new Squad { Id = World.Squads.NextId(), Name = "Carriers", TemplateId = "12-t", Faction = Data.PlayerFaction, HomeCentre = new(0, 0), HomeRadius = 100 });
    }

    public CharacterId Add(string name, float x, bool player = true)
    {
        var race = Data.Bodies.Race(Data.Db.Find("1-t")!);
        var stats = new CharacterStats();
        stats[StatsEnumerated.Strength] = stats[StatsEnumerated.Athletics] = 50;
        var cold = new CharacterCold
        {
            Name = name, RecordId = "2-t", Race = race, Stats = stats, Medical = MedicalState.Create(race, 50), Inventory = new Inventory(),
            Faction = player ? Data.PlayerFaction : Data.FactionIndex("20-t"), IsPlayer = player, SquadId = player ? 0 : -1,
        };
        var id = World.Characters.Spawn(new() { Position = new(x, 300, 0), Health = 100, MaxSpeed = 40, WalkSpeed = 15, Task = (byte)CharacterTask.Idle }, cold, 0);
        if (player)
        {
            var squad = World.Squads.Find(0)!;
            squad.Members.Add(id);
            if (squad.Leader.IsNone) squad.Leader = id;
        }
        return id;
    }

    public CharacterId Cut(string name = "Body", float negativeX = 0, float positiveX = 100)
    {
        var body = Add(name, negativeX, false);
        var cold = Cold(body);
        cold.TorsoBisection = new(new(0, 1, 0, -12));
        cold.TorsoPositivePosition = new(positiveX, 300, 0);
        cold.TorsoPositiveYaw = 0.25f;
        cold.Medical!.KillFromBisection();
        return body;
    }

    public CharacterCold Cold(CharacterId id) => World.Characters.Cold(id.Slot)!;
    public ref readonly CharacterHot Hot(CharacterId id) => ref World.Characters.Previous[id.Slot];
    public CharacterId Named(string name) => World.Snapshot.Characters.Single(c => c.Name == name).Id;
    public void PickUp(CharacterId carrier, CharacterId body, int side)
    {
        World.Commands.Enqueue(new PickUpOrder([carrier], body) { Tick = World.Tick, TorsoSide = side });
        for (int i = 0; i < 1000; i++)
        {
            World.RunTick();
            if (Cold(carrier).Carrying == body && Cold(carrier).CarryingTorsoSide == (side > 0 ? 1 : 0) && Hot(carrier).Task != (byte)CharacterTask.PickUp) return;
        }
        throw new Xunit.Sdk.XunitException("The selected torso half was not picked up.");
    }
    public void PutDown(CharacterId carrier)
    {
        World.Commands.Enqueue(new PutDownOrder([carrier]) { Tick = World.Tick });
        for (int i = 0; i < 100; i++)
        {
            World.RunTick();
            if (Cold(carrier).Carrying.IsNone) return;
        }
        throw new Xunit.Sdk.XunitException("The torso half was not put down.");
    }
    public void Dispose() => World.Dispose();
}

public class TorsoFragmentCarryTests
{
    [Fact]
    public void Picking_the_positive_half_approaches_its_own_location_instead_of_the_negative_body()
    {
        using var rig = new TorsoCarryFixture();
        var carrier = rig.Add("Carrier", 250);
        var body = rig.Cut(negativeX: 0, positiveX: 250);
        rig.PickUp(carrier, body, 1);
        Assert.InRange(rig.Hot(carrier).Position.X, 249, 251);
        Assert.InRange(rig.Hot(body).Position.X, -1, 1);
        Assert.Equal(carrier, rig.Cold(body).TorsoPositiveCarriedBy);
        Assert.True(rig.Cold(body).CarriedBy.IsNone);
    }

    [Fact]
    public void Two_carriers_can_hold_both_halves_and_putting_down_the_positive_half_keeps_the_negative_held()
    {
        using var rig = new TorsoCarryFixture();
        var negative = rig.Add("Negative carrier", 0);
        var positive = rig.Add("Positive carrier", 100);
        var body = rig.Cut();
        rig.PickUp(negative, body, -1);
        rig.PickUp(positive, body, 1);
        Assert.Equal(body, rig.Cold(negative).Carrying);
        Assert.Equal(body, rig.Cold(positive).Carrying);
        Assert.Equal(negative, rig.Cold(body).CarriedBy);
        Assert.Equal(positive, rig.Cold(body).TorsoPositiveCarriedBy);
        rig.PutDown(positive);
        Assert.Equal(body, rig.Cold(negative).Carrying);
        Assert.Equal(negative, rig.Cold(body).CarriedBy);
        Assert.True(rig.Cold(body).TorsoPositiveCarriedBy.IsNone);
        Assert.Equal(rig.Hot(negative).Position, rig.Hot(body).Position);
        Assert.InRange(rig.Cold(body).TorsoPositivePosition!.Value.X, 90, 110);
    }

    [Fact]
    public void Putting_down_the_negative_half_does_not_drop_or_relocate_the_positive_half()
    {
        using var rig = new TorsoCarryFixture();
        var negative = rig.Add("Negative carrier", 0);
        var positive = rig.Add("Positive carrier", 100);
        var body = rig.Cut();
        rig.PickUp(negative, body, 0);
        rig.PickUp(positive, body, 1);
        var before = rig.Cold(body).TorsoPositivePosition;
        rig.PutDown(negative);
        Assert.True(rig.Cold(body).CarriedBy.IsNone);
        Assert.Equal(positive, rig.Cold(body).TorsoPositiveCarriedBy);
        Assert.Equal(body, rig.Cold(positive).Carrying);
        Assert.Equal(before, rig.Cold(body).TorsoPositivePosition);
        Assert.Equal(1, rig.Cold(positive).CarryingTorsoSide);
    }

    [Fact]
    public void A_deleted_positive_carrier_cannot_be_replaced_by_an_unrelated_character_reusing_its_slot()
    {
        using var rig = new TorsoCarryFixture();
        var negative = rig.Add("Negative carrier", 0);
        var positive = rig.Add("Positive carrier", 100);
        var body = rig.Cut();
        rig.PickUp(negative, body, 0);
        rig.PickUp(positive, body, 1);
        Assert.True(rig.World.Characters.Remove(positive));
        var replacement = rig.Add("Replacement", 1000);
        Assert.Equal(positive.Slot, replacement.Slot);
        Assert.NotEqual(positive, replacement);
        rig.World.RunTick();
        Assert.True(rig.Cold(body).TorsoPositiveCarriedBy.IsNone);
        Assert.True(rig.Cold(replacement).Carrying.IsNone);
        Assert.Equal(negative, rig.Cold(body).CarriedBy);
        Assert.Equal(body, rig.Cold(negative).Carrying);
    }

    [Fact]
    public void An_ordinary_uncut_body_keeps_the_original_pick_up_and_put_down_link()
    {
        using var rig = new TorsoCarryFixture();
        var carrier = rig.Add("Carrier", 0);
        var body = rig.Add("Uncut body", 5, false);
        rig.Cold(body).Medical!.KnockOut(10000);
        rig.PickUp(carrier, body, 0);
        Assert.Equal(carrier, rig.Cold(body).CarriedBy);
        Assert.Equal(0, rig.Cold(carrier).CarryingTorsoSide);
        Assert.True(rig.Cold(body).TorsoPositiveCarriedBy.IsNone);
        rig.PutDown(carrier);
        Assert.True(rig.Cold(carrier).Carrying.IsNone);
        Assert.True(rig.Cold(body).CarriedBy.IsNone);
    }
}
