using Meitou.Tests.Combat;

namespace Meitou.Tests.Simulation;

/// <summary>
/// Pins the state hashes of the scripted scenarios the other tests use, so a refactor that is meant to change nothing is checked to
/// change nothing (<see cref="A_scenario_hashes_the_same_at_1_4_and_16_threads"/> only checks that the threads agree with each other). A change of behaviour that is meant must
/// update these numbers in the same commit and say so.
/// </summary>
public class GoldenHashTests(ITestOutputHelper output)
{
    static readonly (string Name, Func<int, List<ulong>> Run)[] Scenarios =
    [
        ("wander", t => DeterminismTests.Hashes(7, t)),
        ("player", PlayerTests.Scripted),
        ("roaming", RoamingTests.Scripted),
        ("bodies", t => BodyWiringTests.Hashes(9, t, 2000)),
        ("fight", FightWiringTests.Hashes),
        ("duels", t => DuelTests.Hashes(5, t, out _)),
        ("wander-parts", t => Parts(WanderSystem.Create(11, t, 200, minPartition: 4), 1, 30, 90, 300)),
        ("town-parts", t => Parts(TownParts(t), 1, 40, 200, 700)),
        ("first-aid", FirstAidTests.Scripted),
        ("aggro", t => AggroTests.Hashes(t)),
        ("goal-fights", AggroTests.GoalFightHashes),
        ("ranged", t => RangedTests.Hashes(t)),
        ("carry", CarryTests.Scripted),
        ("crime", CrimeTests.Scripted),
        ("build", ConstructionTests.Scripted),
        ("dialogue", DialogueTests.Scripted),
        ("ai", AiTests.Scripted),
        ("arrest", ArrestTests.Scripted),
        ("kidnap", KidnapTests.Scripted),
        ("production", ProductionTests.Scripted),
        ("wild", WildTests.Scripted),
        ("luckyren", LuckyrenTests.Scripted),
    ];

    public static TheoryData<string> ScenarioNames => [.. Scenarios.Select(s => s.Name)];

    /// <summary>A roaming town with bodies and feeding, cut into many partitions (the other worlds here are mostly one partition at the default size).</summary>
    static Meitou.Simulation.World TownParts(int threads)
    {
        var db = SyntheticTown.Database(roaming: true, anatomy: true, items: true);
        return SyntheticTown.World(21, threads, db: db, bodies: true, bodyTimeScale: 2000, feed: new Meitou.Simulation.Items.FeedSettings(), minPartition: 4);
    }

    static List<ulong> Parts(Meitou.Simulation.World w, params int[] checkpoints)
    {
        using var world = w;
        var hashes = new List<ulong>();
        int ran = 0;
        foreach (int target in checkpoints)
        {
            world.RunTicks(target - ran);
            ran = target;
            hashes.Add(world.StateHash());
        }
        return hashes;
    }

    // Pins include deterministic Brutality NPC seeds and earned combat experience.
    static readonly Dictionary<string, ulong[]> Golden = new()
    {
        ["wander"] = [0xD1500034628C41D4UL, 0xC2340E8F70A18BFEUL, 0xB81E6A160F9D1F38UL, 0xBBE20F6A3B73104CUL, 0xAB6267B04ABC2401UL, 0xEF2473425CE9AC92UL],
        ["player"] = [0x01AFDA21D35821DFUL, 0x398650178D03F5D7UL, 0x4F36710F60B7C4DCUL, 0x40DA99AF1F5C897FUL, 0x5EBFF3E64BC8A8E0UL, 0xB9747B63C188AC3DUL],
        ["roaming"] = [0x4AEA691C7445151EUL, 0xE9C7A001FDBD5274UL, 0xBFE3223977AE0E0DUL, 0x0FEF318884988F57UL, 0x67E51431F62757E8UL, 0xEA48299A2025C453UL],
        ["bodies"] = [0x1AEBD671C59AC0EFUL, 0xB463E90F833FE894UL, 0x147FB371E0AC6325UL, 0x2589BA85BA693D82UL, 0x50E10795C31C4861UL, 0x11F5943BCF75951AUL, 0xA052F605918D9EFEUL],
        ["fight"] = [0x38411BF7F1A51CD7UL, 0x1AB6C6EE4D2C7322UL, 0xF5ECD83A79337D02UL, 0x6676A7452DAEC0E4UL, 0x7096BEB650F6A56AUL],
        ["duels"] = [0x5BD0316B21E086D4UL, 0xE1E240985ABA571CUL, 0x2E85D5D89D09B597UL, 0xE33B937A5509F75BUL, 0x6D5318BC4CBF2EC9UL, 0x11C449117345CA77UL],
        ["wander-parts"] = [0x207C09DDD8D8C79DUL, 0xE812753F34AF26C5UL, 0x02676EDB7F8150D0UL, 0x93618B2A03EE44FDUL],
        ["town-parts"] = [0x9DEDE375FE71A5A1UL, 0x2AAA3CB08B6AB322UL, 0xE5F25915149650E8UL, 0x014A1BCBD20D77F5UL],
        ["first-aid"] = [0xD43B31BADDB8C09FUL, 0xDC93083E304BE810UL, 0x24148D2EA6D5D8D1UL, 0xFE5CD1DE4E9CAF14UL, 0xAB0306AEAD282770UL, 0x6B981AFCDA26FEFDUL],
        ["aggro"] = [0x2527042ACF6A4822UL, 0x766F6EF9A8771105UL, 0x365EE14D5271A21CUL, 0x7C242A29795EF259UL, 0x340FB8D534436160UL],
        ["goal-fights"] = [0x6ACC65E57671062AUL, 0x41B1A9C6E2FCFE46UL, 0x11BD7A034573C2BDUL, 0xDB8C936940FEB68AUL],
        ["ranged"] = [0xC23DE2D859764D24UL, 0xB66D01C7D27658CAUL, 0x66B9EC21DC7F70A1UL, 0x28096DD42D69C40DUL, 0x551979002B501C21UL],
        ["carry"] = [0x567D03762A25393FUL, 0x06CC86B8FA26E8EBUL, 0xFCC8EF19066574C7UL, 0x27B05FE2B17E9A89UL, 0x572E6DBB014934D7UL, 0x845B54E9030ED339UL, 0x2248E19C6A91754BUL],
        ["crime"] = [0x868B0BB7ADDDF7F5UL, 0x7748ACD2CC921B47UL, 0x64517807F527BFB1UL, 0x18680EF17705B07DUL, 0x8C75FC2ADDD36D7AUL, 0x6A02DCAFBF59EA69UL],
        ["build"] = [0x0BF1D83B68110C29UL, 0x8CDF4E3FDDB903F9UL, 0x6709321EE82A44C3UL, 0xB3CE185610D2C597UL, 0x6F72AD8287A51649UL, 0xE022324A21B2E6F4UL, 0x4EA708C861457CBCUL, 0xE2979945B2E5291DUL],
        ["dialogue"] = [0x3490F7D063916E12UL, 0x9165C58C27428685UL, 0xE63BC85E1E354E13UL, 0x47CADC420684AAE2UL, 0x06164A262EFB318DUL, 0xB77F31DDD48E154DUL],
        ["ai"] = [0x7410364BD0FD103EUL, 0x8549BDB585F593AFUL],
        ["arrest"] = [0xDC6F4A592CA92E13UL, 0x32556BA1E4821FCFUL, 0x6EC6DA085AEBA11DUL, 0x51A0230D4E023171UL, 0xF6B6BCC543B994F6UL],
        ["kidnap"] = [0xB52E0A3F4B550AEFUL, 0x486FDE4E7EB58D9FUL, 0x1B2FF026C7C4F051UL, 0x270184B34C1FE0F9UL, 0x493B89974BE243C8UL],
        ["production"] = [0x837C9EECE3802F75UL, 0xBC4BF6A1887697A3UL, 0xE737F9A955009B38UL, 0xDB4DBABA1E7D1393UL, 0x68392DC2E5592616UL, 0xBA0E8E5482719C25UL],
        ["wild"] = [0x7A40EA78CD33A7C6UL, 0x2A19553963C56CF9UL, 0x9567A9B4DC915E53UL, 0x1CB0521A379F1267UL],
        ["luckyren"] = [0x61F8BB212688EDA1UL],
    };

    [Fact]
    public void Scripted_scenarios_hash_as_pinned()
    {
        // The pins were made on Windows; the wandering scenarios use MathF.Sin/Cos/Atan2, which come from the platform's C library and can differ in the last bit elsewhere.
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "hashes are pinned on Windows");
        var failures = new List<string>();
        foreach (var (name, run) in Scenarios)
        {
            var got = run(4);
            output.WriteLine($"[\"{name}\"] = [{string.Join(", ", got.Select(h => $"0x{h:X16}UL"))}],");
            if (!Golden.TryGetValue(name, out var want)) failures.Add($"{name}: not pinned");
            else if (!want.SequenceEqual(got)) failures.Add($"{name}: hashes changed");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Theory, MemberData(nameof(ScenarioNames))]
    public void A_scenario_hashes_the_same_at_1_4_and_16_threads(string name)
    {
        var run = Scenarios.Single(s => s.Name == name).Run;
        var one = run(1);
        Assert.Equal(one, run(4));
        Assert.Equal(one, run(16));
    }
}
