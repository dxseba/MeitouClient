using System.Numerics;
using System.Text;
using Meitou.Brutality;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public sealed class BrutalityWeaponDeterminismTests
{
    static SimWorld World(ulong seed, WeaponBloodState state) => new(new WorldSettings { Seed = seed, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [state]);
    static ItemInstance Weapon() => new() { Record = "same-blade", Type = FcsRecordType.WEAPON };
    static void Deposit(WeaponBloodState state, ItemInstance item, uint colour) => Assert.True(state.Deposit(item, new RaceData { BloodColour = colour }, new(20, 0, 0, 1), true, true));
    static ulong Hash(WeaponBloodState state) { var hash = new StateHasher(); state.Hash(ref hash); return hash.Value; }
    static string Token(ulong seed, uint purpose)
    {
        var state = new WeaponBloodState((RngPurpose)purpose); using var world = World(seed, state);
        state.Schedule(world); var item = Weapon(); Deposit(state, item, 0xFF0000);
        return Assert.IsType<string>(state.ContainerToken(item));
    }

    [Fact]
    public void Fresh_container_identity_is_repeatable_and_separates_world_seed_and_registered_mod_purpose()
    {
        string first = Token(21, 0x80000001);
        Assert.Equal(first, Token(21, 0x80000001));
        Assert.NotEqual(first, Token(22, 0x80000001));
        Assert.NotEqual(first, Token(21, 0x80000002));
        Assert.True(WeaponBloodState.IsContainerToken(first));
    }

    [Fact]
    public void Loaded_legacy_guid_namespace_and_serial_survive_binding_to_a_different_world()
    {
        const string legacy = "0123456789abcdef0123456789abcdef";
        var state = new WeaponBloodState((RngPurpose)0x80000002);
        state.Read(new MemoryStream(Encoding.UTF8.GetBytes("{\"SchemaVersion\":1,\"Items\":[],\"ContainerNamespace\":\"" + legacy + "\",\"ContainerSerial\":41}")));
        using var world = World(999, state); state.Schedule(world);
        var item = Weapon(); Deposit(state, item, 0xFF0000);
        Assert.Equal(WeaponBloodState.ContainerTokenPrefix + legacy + ":42", state.ContainerToken(item));
    }

    static ulong LiveHash(bool reverseDeposits, bool swapColours)
    {
        var state = new WeaponBloodState((RngPurpose)0x80000001); using var world = World(21, state);
        var first = Weapon(); var second = Weapon(); var cold = new Combatant().MakeCold();
        cold.Inventory = new(); cold.Inventory.Items.AddRange([first, second]);
        world.Characters.Spawn(new() { Position = Vector3.Zero, Health = 100 }, cold, 0);
        state.Schedule(world);
        uint a = swapColours ? 0x00FF00U : 0xFF0000U, b = swapColours ? 0xFF0000U : 0x00FF00U;
        if (reverseDeposits) { Deposit(state, second, b); Deposit(state, first, a); }
        else { Deposit(state, first, a); Deposit(state, second, b); }
        return Hash(state);
    }

    [Fact]
    public void Unsaved_identical_weapons_hash_by_inventory_path_and_colour_not_deposit_or_dictionary_order()
    {
        Assert.Equal(LiveHash(false, false), LiveHash(true, false));
        Assert.NotEqual(LiveHash(false, false), LiveHash(false, true));
    }

    [Fact]
    public void Lazy_durable_stains_are_hashed_even_before_an_item_is_materialized()
    {
        static WeaponBloodState Read(string items)
        {
            var state = new WeaponBloodState();
            state.Read(new MemoryStream(Encoding.UTF8.GetBytes("{\"SchemaVersion\":1,\"ContainerNamespace\":\"0123456789abcdef0123456789abcdef\",\"ContainerSerial\":2,\"Items\":" + items + "}")));
            return state;
        }
        const string red = "{\"Key\":\"a\",\"R\":1,\"G\":0,\"B\":0,\"Amount\":0.2}";
        const string green = "{\"Key\":\"b\",\"R\":0,\"G\":1,\"B\":0,\"Amount\":0.4}";
        Assert.Equal(Hash(Read("[" + red + "," + green + "]")), Hash(Read("[" + green + "," + red + "]")));
        Assert.NotEqual(Hash(Read("[" + red + "," + green + "]")), Hash(Read("[" + red + "]")));
    }
}
