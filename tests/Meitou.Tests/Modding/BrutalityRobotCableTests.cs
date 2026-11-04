using System.Numerics;
using Meitou.Brutality.Effects;

namespace Meitou.Tests.Modding;

public sealed class BrutalityRobotCableTests
{
    [Fact]
    public void A_cut_cable_keeps_its_root_at_the_face_sags_under_gravity_and_stops_rebuilding_when_settled()
    {
        var scene = new RobotCableSimulation();
        var contact = new CutFluidContact(new(2, 8, 3), Vector3.UnitX);
        RobotCableContact[] contacts = [new(7, contact)];
        scene.Advance(0, contacts, _ => 0);
        for (int frame = 1; frame <= 600; frame++) scene.Advance(frame / 60.0, contacts, _ => 0);
        var cable = Assert.Single(scene.Rendered);
        Assert.True(cable.Mechanical); Assert.Equal(RobotCableSimulation.VertexCount, cable.Vertices.Length);
        var root = cable.Vertices.Take(3).Select(v => cable.Centre + v.Position).ToArray();
        Assert.Contains(root, p => Vector3.Distance(p, contact.Position + contact.Normal * .04f) < .056f);
        Assert.True(cable.Vertices.Min(v => v.Position.Y) < -.7f);
        Assert.All(cable.Vertices, v =>
        {
            Assert.True(float.IsFinite(v.Position.LengthSquared()) && float.IsFinite(v.Normal.LengthSquared()));
            Assert.InRange(v.Normal.Length(), .999f, 1.001f);
            Assert.InRange(v.Colour.X, .03f, .04f); Assert.InRange(Vector3.Distance(v.Position, Vector3.Zero), 0, 3);
        });
        Assert.Equal(0, scene.Moving);
        long version = cable.Version;
        scene.Advance(10.02, contacts, _ => 0); Assert.Equal(version, Assert.Single(scene.Rendered).Version);
    }

    [Fact]
    public void Paused_and_carried_cables_keep_the_shape_without_an_old_world_position_tether()
    {
        var scene = new RobotCableSimulation(); var point = new Vector3(0, 8, 0);
        RobotCableContact[] Contacts(Vector3 p) => [new(10, new(p, Vector3.UnitZ))];
        scene.Advance(0, Contacts(point), _ => 0); scene.Advance(.1, Contacts(point), _ => 0);
        var before = Assert.Single(scene.Rendered); var positions = before.Vertices.Select(v => v.Position).ToArray();
        scene.Advance(.1, Contacts(point), _ => 0);
        Assert.Equal(before.Version, Assert.Single(scene.Rendered).Version);
        var shift = new Vector3(14, 3, -2); scene.Advance(.1, Contacts(point + shift), _ => 0);
        var carried = Assert.Single(scene.Rendered);
        Assert.Equal(before.Key, carried.Key); Assert.Equal(point + shift, carried.Centre);
        for (int i = 0; i < positions.Length; i++) Assert.InRange(Vector3.Distance(positions[i], carried.Vertices[i].Position), 0, .041f);
        Assert.All(carried.Vertices, v => Assert.InRange(Vector3.Distance(carried.Centre + v.Position, point + shift), 0, 3));
    }

    [Fact]
    public void Ground_contact_crowd_limits_visibility_and_disabling_keep_cable_resources_bounded()
    {
        var scene = new RobotCableSimulation();
        var contacts = Enumerable.Range(0, 100).Select(i => new RobotCableContact(i + 1, new(new(i * 2, .2f, 0), Vector3.UnitX))).ToArray();
        scene.Advance(0, contacts, _ => 0); scene.Advance(.1, contacts, _ => 0);
        Assert.Equal(RobotCableSimulation.Capacity, scene.Count); Assert.Equal(RobotCableSimulation.Capacity, scene.Rendered.Count);
        Assert.All(scene.Rendered.SelectMany(p => p.Vertices.Select(v => p.Centre.Y + v.Position.Y)), height => Assert.True(height >= .004f));
        long oldKey = scene.Rendered[0].Key;
        scene.Advance(.2, [], _ => 0); Assert.Equal(0, scene.Count); Assert.Empty(scene.Rendered);
        scene.Advance(.3, contacts, _ => 0); Assert.NotEqual(oldKey, scene.Rendered[0].Key);
        scene.Advance(.4, contacts, _ => 0, enabled: false); Assert.Equal(0, scene.Count); Assert.Empty(scene.Rendered);
    }
}
