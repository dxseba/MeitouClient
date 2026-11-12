using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Owns the original wound progression and publishes immutable stock renderer snapshots.</summary>
internal sealed class StockWoundRules(Func<ChestHoleRule> punchSettings) : ITickSystem, IModSaveData, IStateHashed
{
    internal sealed record View(BodyWoundState Body, SevereHeadWoundState Head, ChestHoleState? Hole = null, HeadCutState? AttachedHead = null)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public BodyWoundState? Bruises { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        public long? PunchTick { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        internal bool HasVisual => Body.HasAny || Bruises?.HasAny == true || Head.Kind != SevereHeadWoundKind.None
            || Hole is not null || AttachedHead is not null;
    }
    internal static readonly View EmptyView = new(BodyWoundState.Empty, new(0));
    internal sealed record Frame(long Tick, IReadOnlyDictionary<CharacterId, View> Actors);
    sealed record SavedWound(Hand Owner, View Wound);
    sealed record Document(int Version, SavedWound[] Rows);
    sealed class Actor
    {
        public Hand? Owner;
        public WoundBody Body = new();
        public WoundBody Bruises = new();
        public SevereHeadWoundState Head = new(0);
        public ChestHoleState? Hole;
        public HeadCutState? AttachedHead;
        public long? PunchTick;
        public View Snapshot() => new(Body.Snapshot(), Head, Hole, AttachedHead)
            { PunchTick = PunchTick, Bruises = Bruises.Snapshot() is { HasAny: true } bruises ? bruises : null };
        public void Restore(View view)
        {
            Body = WoundBody.From(view.Body);
            Bruises = WoundBody.From(view.Bruises);
            Head = view.Head;
            Hole = view.Hole;
            AttachedHead = view.AttachedHead ?? AttachedHead;
        }
    }

    const int SaveLimit = 100_000;
    static readonly ConditionalWeakTable<World, StockWoundRules> worlds = new();
    static readonly JsonSerializerOptions json = new() { IncludeFields = true };
    static readonly RngPurpose headVisual = (RngPurpose)(uint)(Rng.StableHash("meitou.brutality.head-wound") | 0x80000000UL);
    static readonly RngPurpose punchVisual = (RngPurpose)(uint)(Rng.StableHash("meitou.brutality.chest-hole") | 0x80000000UL);
    static readonly string? preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        ? Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_WOUND_PREVIEW") : null;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly ConditionalWeakTable<CharacterCold, Actor> retained = new();
    readonly Dictionary<CharacterId, Actor> active = [];
    readonly Dictionary<Hand, Actor> saved = [];
    readonly Queue<Frame> history = new();
    volatile Frame[] frames = [];
    long lastSpawn = -1, lastHeal;
    bool changed, previewed;
#if BRUTALITY_STOCK_SAVE_PROBE
    long nextHeadWave;
    int previewedHeads;
#endif
    internal static bool Enabled { get; private set; }

    ChestHoleRule punchRule = ChestHoleRule.Default;

    internal static void Install(ModContext context, Func<ChestHoleRule> punchSettings)
    {
        try
        {
            if (!StockWoundPresentation.Install(context)) return;
            var write = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name == "WriteCharacter" && method.ReturnType == typeof(SaveCharacter)
                    && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
            context.Harmony.Patch(write, postfix: new HarmonyMethod(typeof(StockWoundRules), nameof(CharacterWritten)));
            context.AddSystem("wounds", _ => new StockWoundRules(punchSettings), SystemAnchor.AfterPart(StandardParts.Combat));
            Enabled = true;
            context.Log("Brutality stock localized wounds initialized");
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock localized wounds unavailable: {error.GetBaseException().Message}");
        }
    }

    static StockWoundRules? For(World world) => worlds.TryGetValue(world, out var state)
        ? state : world.Systems.OfType<StockWoundRules>().FirstOrDefault();

    internal static Frame? FrameAt(World world, long tick)
    {
        if (!worlds.TryGetValue(world, out var state)) return null;
        var available = state.frames;
        for (int i = available.Length - 1; i >= 0; i--)
            if (available[i].Tick <= tick) return available[i];
        return null;
    }

    internal static void Observe(World world, in CombatHit hit, float level)
    {
        if (hit.Source != CombatHitSource.Melee || !hit.VictimWasAlive || hit.CutDirection < 0
            || !world.Characters.TryResolveNext(hit.Victim, out int victim)
            || world.Characters.Cold(victim) is not { Fighter.IsAnimal: false, Race: { } race, Medical: { } medical } cold
            || (uint)hit.Part >= (uint)medical.Parts.Count
            || !world.Characters.TryResolveNext(hit.Attacker, out int attacker)
            || world.Characters.Cold(attacker)?.Fighter is not { } fighter
            || !StockTrainingHook.TryGetPartMaximum(world, hit, out float maximum)
            || For(world) is not { } state) return;
        var contact = StockHeadGate.PacketFor(world, hit) ?? new DamagePacket(hit.Damage.Cut + hit.Absorbed.Cut,
            hit.Damage.Blunt + hit.Absorbed.Blunt, hit.Damage.Pierce + hit.Absorbed.Pierce, hit.Damage.ExtraStun, 1, 0);
        var mechanism = CombatWoundMechanisms.Classify(fighter, contact, hit.CutDirection);
        if (mechanism == CombatWoundMechanism.Unspecified) return;

        var actor = state.Find(hit.Victim, cold);
        var part = medical.Parts[hit.Part].Template;
        bool covered = StockGarmentHits.Observe(world, hit, cold, part, maximum, contact, mechanism, fighter.TechniqueCategory);
        float damage = hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce;
        int punchRegion = ChestHolePolicy.RegionOf(part.StringId);
        if (part.Type != BodyPartType.Head || !medical.Decapitated)
        {
            if (mechanism == CombatWoundMechanism.Crush && !race.IsRobot
                && (punchRegion < 0 || !covered || cold.Appearance?.BodyLayers.Count is not > 0)
                && damage >= Math.Max(3, maximum * .04f))
                state.changed |= actor.Bruises.Apply(hit, part, maximum, mechanism, fighter.TechniqueCategory);
            else if (mechanism != CombatWoundMechanism.Crush
                && hit.Damage.Cut + hit.Damage.Pierce >= StockGarmentHits.MinimumSkinCut(maximum, covered))
                state.changed |= actor.Body.Apply(hit, part, maximum, mechanism, fighter.TechniqueCategory, detailedSites: !race.IsRobot);
        }
        var punch = new ChestHoleInput(hit.Source, mechanism, fighter.TechniqueCategory, part.StringId,
            damage, maximum, hit.VictimWasAlive, medical.Dead, actor.Hole is not null, false, race.IsRobot, false);
        bool punchesThrough = ChestHolePolicy.Qualifies(punch, state.punchRule);
        if (trace && fighter.Unarmed && punchRegion >= 0 && actor.Hole is null)
            Console.WriteLine(FormattableString.Invariant($"Brutality stock punch check actor={hit.Victim} part={part.Name} damage={damage:F1}/{maximum:F1} required={state.punchRule.DamageFraction:P0} enabled={state.punchRule.Enabled} lethalOnly={state.punchRule.LethalOnly} lethal={medical.Dead} qualifies={punchesThrough}"));
        if (punchesThrough)
        {
            ulong roll = Rng.Hash(world.Seed, Rng.Key(hit.Victim), punchVisual, CombatRolls.BlowCounter(hit.Attacker.Slot, hit.Sequence, hit.Blow, 0));
            if (ChestHolePolicy.StateFor(hit.Travel, hit.VictimYaw, damage, maximum, roll, punchRegion, state.punchRule.Size, lethalSide: medical.Dead) is { IsValid: true } hole)
            {
                // Keep the opened torso within view of a finishing strike's impact.
                if (medical.Dead && cold.Blow is { } blow)
                {
                    var limited = StockTorsoLaunch.Limit(blow, robot: race.IsRobot);
                    cold.Blow = limited;
                    cold.Knockdown = limited.Velocity;
                }
                actor.Hole = hole;
                actor.PunchTick = hit.Tick;
                state.changed = true;
                if (trace) Console.WriteLine($"Brutality stock punch-through actor={hit.Victim} part={part.Name} tick={hit.Tick}");
            }
            else if (trace) Console.WriteLine($"Brutality stock punch check rejected the side approach actor={hit.Victim} tick={hit.Tick}");
        }
        float force = hit.Damage.Cut + hit.Damage.Pierce + (mechanism == CombatWoundMechanism.Crush ? hit.Damage.Blunt : 0);
        if (race.IsRobot || medical.Decapitated || part.Type != BodyPartType.Head || force < BrutalityHeadPolicy.HeavyForce) return;
        var requested = (hit.Victim.Slot % 3) switch
        {
            1 => SevereHeadWoundKind.Scalp, 2 => SevereHeadWoundKind.BrokenSkull, _ => SevereHeadWoundKind.JawLost,
        };
        ulong seed = Rng.Hash(world.Seed, Rng.Key(hit.Victim), headVisual, (ulong)hit.Tick);
        var placement = HeadWoundPlacementPolicy.Estimate(new(hit.Travel.X, hit.Travel.Z), hit.VictimYaw, hit.CutDirection, force, seed);
        int profile = (hit.CutDirection switch { 1 or 4 or 5 or 7 => 2, 2 => 1, _ => 0 }) + (placement is null && force >= 35 ? 3 : 0);
        var wound = BrutalityHeadPolicy.Apply(actor.Head, level, force, requested, mechanism, profile, placement);
        if (wound == actor.Head) return;
        actor.Head = wound;
        state.changed = true;
        if (trace && wound.Kind != SevereHeadWoundKind.None)
            Console.WriteLine($"Brutality stock head wound actor={hit.Victim} kind={wound.Kind} tick={hit.Tick}");
    }

    internal static void RecordHeadCut(World world, CharacterId id, HeadCutState cut)
    {
        if (cut is not { IsValid: true, Mode: HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit }
            || For(world) is not { } state || !world.Characters.TryResolveNext(id, out int slot)
            || world.Characters.Cold(slot) is not { } cold) return;
        var actor = state.Find(id, cold);
        if (actor.AttachedHead is not null) return;
        actor.AttachedHead = cut;
        StockHeadSplitSave.Remember(world, id, cut, Rng.Key(id));
        state.changed = true;
    }

    Actor Find(CharacterId id, CharacterCold cold)
    {
        if (active.TryGetValue(id, out var existing)) return existing;
        var actor = retained.GetValue(cold, owner =>
        {
            if (owner.Save is { Source.Handle.IsNull: false } source)
            {
                var handle = source.Source.Handle;
                if (saved.TryGetValue(handle, out var known)) return known;
                var created = new Actor { Owner = handle };
                saved.Add(handle, created);
                return created;
            }
            return new();
        });
        active.Add(id, actor);
        changed = true;
        return actor;
    }

    public void Schedule(World world)
    {
        StockBowelSave.ObserveSpawns(world);
        punchRule = punchSettings();
        worlds.GetValue(world, _ => this);
        if (saved.Count == 0 || lastSpawn == world.Characters.Spawns) return;
        lastSpawn = world.Characters.Spawns;
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Next[slot] is { Alive: true } next && world.Characters.Cold(slot) is { Save.Source.Handle: { IsNull: false } handle } cold
                && saved.ContainsKey(handle))
            {
                var id = new CharacterId(slot, next.Generation);
                if (Find(id, cold).AttachedHead is { } cut)
                    StockHeadSplitSave.Remember(world, id, cut, Rng.Key(id));
            }
    }

    public void SlowWorld(World world)
    {
        Preview(world);
        foreach (var id in active.Keys.Where(id => !world.Characters.TryResolveNext(id, out _)).ToArray())
            changed |= active.Remove(id);
        if (world.Tick / 3000 != lastHeal)
        {
            lastHeal = world.Tick / 3000;
            foreach (var actor in active.Values.Concat(saved.Values).Distinct())
            {
                if (actor.Body.Snapshot() is { HasAny: true })
                {
                    if (!actor.Body.Heal(.01f)) actor.Body = new();
                    changed = true;
                }
                if (actor.Bruises.Snapshot() is { HasAny: true })
                {
                    if (!actor.Bruises.Heal(.03f)) actor.Bruises = new();
                    changed = true;
                }
            }
        }
        if (!changed) return;
        var views = active.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot());
        foreach (var id in views.Where(pair => !pair.Value.HasVisual)
            .Select(pair => pair.Key).ToArray())
            views.Remove(id);
        history.Enqueue(new(world.Tick + 1, views));
        while (history.Count > 64) history.Dequeue();
        frames = history.ToArray();
        changed = false;
    }

    internal static bool QueuePreview(World world, CharacterId id, int slot, CharacterCold cold, string kind, int variant)
    {
        if (!Enabled || For(world) is not { } state || cold.Medical is not { } medical) return false;
        var next = world.Characters.Next[slot];
        if (kind == "severe")
        {
            if (cold.Race?.IsRobot != false || !medical.Parts.Any(part => part.Template.Type == BodyPartType.Head)) return false;
            var wound = variant switch { 0 => SevereHeadWoundKind.JawLost, 1 => SevereHeadWoundKind.Scalp, _ => SevereHeadWoundKind.BrokenSkull };
            var facing = new Vector2(MathF.Sin(next.Yaw), MathF.Cos(next.Yaw));
            var placement = HeadWoundPlacementPolicy.Estimate(-facing, next.Yaw, 2, 55, Rng.Key(id));
            state.Find(id, cold).Head = BrutalityHeadPolicy.Apply(new(3), 100, 55, wound, CombatWoundMechanism.Slash, 1, placement, forceForm: true);
        }
        else
        {
            int region = kind == "stomach" ? ChestHoleState.Abdomen : ChestHoleState.Chest;
            if (medical.Parts.FirstOrDefault(part => part.Template.Vital
                    && ChestHolePolicy.RegionOf(part.Template.StringId) == region) is not { } hitPart) return false;
            var actor = state.Find(id, cold);
            actor.Hole = ChestHolePolicy.Preview(variant, region);
            actor.PunchTick = world.Tick;
            if (variant >= 3) world.Commands.Enqueue(new DebugDamage([id], 5000, hitPart.Template.StringId) { Tick = world.Tick + 1 });
        }
        state.changed = true;
        return true;
    }

    void Preview(World world)
    {
        if (previewed || world.Tick < 90) return;
#if BRUTALITY_STOCK_SAVE_PROBE
        bool mixed = preview == "head-mixed";
        bool sustained = mixed || preview == "head-sustained";
        if (mixed && world.Tick < 400 || sustained && world.Tick < nextHeadWave) return;
        int wave = 0;
        if (!sustained &&
#else
        if (
#endif
            preview is not
            ("jaw" or "scalp" or "skull" or "chest" or "back" or "stomach" or "bowels" or "punch-chest" or "punch-stomach"
            or "attached" or "head-vertical" or "head-transverse" or "head-oblique")) return;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot] is not { Alive: true } next || table.Cold(slot) is not
                { IsPlayer: false, Fighter.IsAnimal: false, Race.IsRobot: false, Medical: { Dead: false } medical } cold) continue;
#if BRUTALITY_STOCK_SAVE_PROBE
            if (mixed && slot <= 12) continue;
#endif
            if (preview == "bowels" && cold.Race?.Name is not ("Greenlander" or "Scorchlander")) continue;
            var id = new CharacterId(slot, next.Generation);
            var actor = Find(id, cold);
            var facing = new Vector2(MathF.Sin(next.Yaw), MathF.Cos(next.Yaw));
            var travel = facing * (preview == "back" ? 1 : -1);
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained || preview is "attached" or "head-vertical" or "head-transverse" or "head-oblique")
#else
            if (preview is "attached" or "head-vertical" or "head-transverse" or "head-oblique")
#endif
            {
                if (medical.Parts.FirstOrDefault(part => part.Template.Type == BodyPartType.Head) is not { } part) continue;
                string angle = preview == "attached" ? "oblique" : preview![5..];
#if BRUTALITY_STOCK_SAVE_PROBE
                if (sustained) angle = (previewedHeads % 3) switch { 0 => "vertical", 1 => "oblique", _ => "transverse" };
#endif
                if (HeadCutPolicy.Preview(angle, visualSeed: Rng.Key(id), travel: travel, victimYaw: next.Yaw) is { } shape)
                    RecordHeadCut(world, id, shape);
                world.Commands.Enqueue(new DebugDamage([id], 5000, part.Template.StringId) { Tick = world.Tick + 1 });
            }
            else if (preview is "punch-chest" or "punch-stomach")
            {
                int region = preview == "punch-stomach" ? ChestHoleState.Abdomen : ChestHoleState.Chest;
                actor.Hole = ChestHolePolicy.Preview(1, region);
                actor.PunchTick = world.Tick;
            }
            else if (preview is "jaw" or "scalp" or "skull")
            {
                var kind = preview == "jaw" ? SevereHeadWoundKind.JawLost : preview == "scalp" ? SevereHeadWoundKind.Scalp : SevereHeadWoundKind.BrokenSkull;
                var placement = HeadWoundPlacementPolicy.Estimate(travel, next.Yaw, 2, 55, Rng.Key(id));
                actor.Head = BrutalityHeadPolicy.Apply(new(3), 100, 55, kind, CombatWoundMechanism.Slash, 1, placement, forceForm: true);
            }
            else
            {
                string partId = preview is "stomach" or "bowels" ? GorePartIds.Stomach : ArmourData.ChestPartId;
                int part = -1;
                for (int i = 0; i < medical.Parts.Count; i++)
                    if (medical.Parts[i].Template.StringId == partId) { part = i; break; }
                if (part < 0) continue;
                var hit = new CombatHit(world.Tick, CombatHitSource.Melee, id, id, 0, 0, part, new(65, 0, 0), default, next.Position,
                    new(travel.X, 0, travel.Y), next.Yaw, 2, false, true, true, false);
                actor.Body.Apply(hit, medical.Parts[part].Template, medical.Parts[part].MaxHp, CombatWoundMechanism.Slash, 3);
                if (preview == "bowels") actor.Body.BellyHits = 10;
            }
            changed = true;
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained)
            {
                previewed = ++previewedHeads >= (mixed ? 8 : 20);
                nextHeadWave = world.Tick + 100;
            }
            else
#endif
                previewed = true;
            Console.WriteLine($"Brutality stock wound preview style={preview} target={id} tick={world.Tick}");
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained && !previewed && ++wave < 4) continue;
#endif
            return;
        }
    }

    static void CharacterWritten(World world, CharacterCold cold, SaveCharacter __result)
    {
        if (__result.Handle.IsNull || For(world) is not { } state || !state.retained.TryGetValue(cold, out var actor)) return;
        if (actor.Owner is { } old && old != __result.Handle && ReferenceEquals(state.saved.GetValueOrDefault(old), actor))
            state.saved.Remove(old);
        actor.Owner = __result.Handle;
        state.saved[__result.Handle] = actor;
    }

    public void Write(Stream stream)
    {
        var rows = SavedRows().Take(SaveLimit + 1).ToArray();
        if (rows.Length > SaveLimit) throw new InvalidDataException("Too many saved Brutality wounds.");
        JsonSerializer.Serialize(stream, new Document(1, rows), json);
    }

    internal static byte[] MigrationBytes(IEnumerable<(Hand Key, BrutalityDurableState State)> source)
    {
        var rows = source.Select(row => new SavedWound(row.Key, new View(row.State.Wounds, row.State.SevereHead, row.State.ChestHole,
                row.State.Head is { Mode: HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit } cut ? cut : null)))
            .Where(row => row.Wound.Body.HasAny || row.Wound.Head.HeavyHits > 0 || row.Wound.Hole is not null || row.Wound.AttachedHead is not null)
            .OrderBy(row => (row.Owner.Type, row.Owner.C, row.Owner.Cs, row.Owner.I, row.Owner.S)).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new Document(1, rows), json);
    }

    public void Read(Stream stream)
    {
        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(stream, json); }
        catch (JsonException) { return; }
        if (document is not { Version: 1, Rows.Length: <= SaveLimit }) return;
        foreach (var row in document.Rows)
            if (row is { Owner.IsNull: false, Wound: { Body.IsValid: true, Head.IsValid: true } wound }
                && (wound.Bruises is null || wound.Bruises is { IsValid: true })
                && (wound.Hole is null || wound.Hole.Value.IsValid)
                && (wound.AttachedHead is null || wound.AttachedHead is { IsValid: true, Mode: HeadCutMode.NeckDetach or HeadCutMode.AttachedSplit }))
            {
                if (!saved.TryGetValue(row.Owner, out var actor)) saved.Add(row.Owner, actor = new() { Owner = row.Owner });
                actor.Restore(wound);
            }
        lastSpawn = -1;
        changed = true;
    }

    IEnumerable<SavedWound> SavedRows() => saved.OrderBy(pair => (pair.Key.Type, pair.Key.C, pair.Key.Cs, pair.Key.I, pair.Key.S))
        .Select(pair => new SavedWound(pair.Key, pair.Value.Snapshot()))
        .Where(row => row.Wound.HasVisual || row.Wound.Head.HeavyHits > 0);

    public void Hash(ref StateHasher hash)
    {
        foreach (var (id, actor) in active.OrderBy(pair => Rng.Key(pair.Key)))
        {
            var view = actor.Snapshot();
            if (!view.HasVisual && view.Head.HeavyHits == 0) continue;
            hash.Add(id);
            HashWound(ref hash, view);
        }
        var live = active.Values.ToHashSet();
        foreach (var row in SavedRows())
        {
            if (live.Contains(saved[row.Owner])) continue;
            hash.Add(row.Owner.Type); hash.Add(row.Owner.C); hash.Add(row.Owner.Cs); hash.Add(row.Owner.I); hash.Add(row.Owner.S);
            HashWound(ref hash, row.Wound);
        }
    }

    static void HashWound(ref StateHasher hash, View wound)
    {
        hash.Add(wound.Body.StateKey()); hash.Add(wound.Head.HeavyHits); hash.Add((int)wound.Head.Kind);
        hash.Add(wound.Head.Profile); hash.Add((int)wound.Head.Mechanism); hash.Add(wound.Head.Placement is not null);
        if (wound.Head.Placement is { } place)
        {
            hash.Add(place.Point); hash.Add(place.Normal); hash.Add(place.Tangent);
            hash.Add(place.LengthScale); hash.Add(place.WidthScale);
        }
        hash.Add(wound.Hole is not null);
        if (wound.Hole is { } hole)
        {
            hash.Add(hole.Height); hash.Add(hole.Across); hash.Add(hole.Yaw); hash.Add(hole.Radius); hash.Add(hole.Entry); hash.Add(hole.Seed);
            hash.Add(hole.Region); hash.Add(hole.Aspect); hash.Add(hole.Tilt); hash.Add(hole.Tear); hash.Add(hole.Flare);
        }
        hash.Add(wound.AttachedHead is not null);
        if (wound.AttachedHead is { } attached) attached.Hash(ref hash);
        if (wound.Bruises is { HasAny: true } bruises) hash.Add(bruises.StateKey());
    }
}
