using Meitou.Simulation.Movement;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Persists stock cuts beside the native corpse record and retains otherwise unwritten cut corpses.</summary>
internal static class StockTorsoSave
{
    const string CutKey = "dxseba brutality torso", Standalone = "dxseba brutality corpse";
    const int Version = 1, MaximumLength = 120_000;
    static readonly JsonSerializerOptions json = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 12 };
    static readonly ConditionalWeakTable<World, ConcurrentDictionary<CharacterId, Pose>> frames = new();
    static readonly ConditionalWeakTable<CharacterCold, CorpseLocation> locations = new();
    static readonly MethodInfo writer = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Length == 13);
    static readonly MethodInfo writesCold = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
    static readonly MethodInfo setCount = AccessTools.Method(typeof(SaveCapture), "SetCharCount");
    static readonly Type characters = typeof(SaveLoader).Assembly.GetType("Meitou.Simulation.Saving.SavedCharacters", true)!;
    static readonly Type assembly = typeof(SaveLoader).Assembly.GetType("Meitou.Simulation.Bodies.CharacterAssembly", true)!;
    static readonly MethodInfo read = AccessTools.Method(characters, "Read"), make = AccessTools.Method(characters, "Make");

    sealed record CorpseLocation(string Platoon, int Slot);
    sealed record Record(int Version, TorsoBisectionState Shape, ulong Seed, Vector2 Travel, float Force, int Part, uint Blood, bool Robot, Pose? Frame, bool Imported = false);
    internal sealed record Pose(Matrix4x4 World, Matrix4x4[] Birth, SavedFragmentPiece Positive, SavedFragmentPiece Negative);
    sealed class Capture(World world, PopulationData data, SaveClock clock, SaveCaptureOptions options)
    {
        internal readonly World World = world;
        internal readonly PopulationData Data = data;
        internal readonly SaveClock Clock = clock;
        internal readonly SaveCaptureOptions Options = options;
        internal readonly HashSet<CharacterCold> Written = new(ReferenceEqualityComparer.Instance);
        internal SaveBuilder? Builder;
        internal bool Completed;
    }
    [ThreadStatic] static Capture? capturing;

    internal static void Install(ModContext context)
    {
        context.Harmony.Patch(AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture)),
            prefix: Patch(nameof(BeforeCapture)), postfix: Patch(nameof(AfterCapture)), finalizer: Patch(nameof(CaptureFinished)));
        context.Harmony.Patch(AccessTools.Method(typeof(SaveCapture), "WriteMemories"), prefix: Patch(nameof(BeforeMemories)));
        context.Harmony.Patch(writesCold, prefix: Patch(nameof(Writing)), postfix: Patch(nameof(Written)));
        context.Harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)), postfix: Patch(nameof(Loaded)));
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockTorsoSave), name);
    internal static void Publish(World world, CharacterId owner, Pose pose) => frames.GetOrCreateValue(world)[owner] = pose;
    internal static void Remove(World world, CharacterId owner)
    {
        if (frames.TryGetValue(world, out var list)) list.TryRemove(owner, out _);
    }
    internal static Pose? For(World world, CharacterId owner) =>
        frames.TryGetValue(world, out var list) && list.TryGetValue(owner, out var value) ? value : null;

    internal static void Restore(World world, CharacterId id, CharacterCold cold)
    {
        if (StockHeadRules.Enabled) StockHeadSplitSave.Restore(world, id, cold);
        if (!StockTorsoRules.Enabled || cold is not { Medical.Dead: true, Save.Source.GameState: { } state } || Read(state) is not { } record) return;
        uint blood = record.Imported && cold.Race is { } race ? race.BloodColour : record.Blood;
        bool robot = record.Imported ? cold.Race?.IsRobot == true : record.Robot;
        if (StockTorsoRules.Restore(world, new(id, world.Tick - 1, record.Seed, record.Shape, record.Travel,
                record.Force, record.Part, blood, robot, Restoring: true))
            && record.Frame is { } pose && Valid(pose)) Publish(world, id, pose);
    }

    internal static void Migrate(FcsRecord state, TorsoBisectionState shape, ulong seed, bool standalone)
    {
        if (state.Strings.ContainsKey(CutKey)) return;
        state.Strings[CutKey] = JsonSerializer.Serialize(new Record(Version, shape, seed, default, 0, 0, 0, false, null, Imported: true), json);
        if (standalone) state.Bools[Standalone] = true;
    }

    internal static void RequireMigratedOwner(FcsRecord state)
    {
        if (Read(state) is { Imported: true }) state.Bools[Standalone] = true;
    }

    static Record? Read(FcsRecord state)
    {
        if (state.Strings.GetValueOrDefault(CutKey) is not { Length: > 0 and <= MaximumLength } text) return null;
        try
        {
            var record = JsonSerializer.Deserialize<Record>(text, json);
            return record is { Version: Version, Shape.IsValid: true, Part: >= 0 and < 128 }
                && float.IsFinite(record.Force) && record.Force >= 0 && float.IsFinite(record.Travel.LengthSquared()) ? record : null;
        }
        catch (JsonException) { return null; }
    }

    static bool Valid(Pose pose) => pose.Birth is { Length: > 0 and <= 1024 }
        && pose.Birth.All(MatrixValid) && MatrixValid(pose.World) && Matrix4x4.Invert(pose.World, out _)
        && AdvancedFragmentScene.Valid(pose.Positive) && AdvancedFragmentScene.Valid(pose.Negative);

    static bool MatrixValid(Matrix4x4 matrix) => float.IsFinite(matrix.GetDeterminant())
        && float.IsFinite(matrix.Translation.LengthSquared()) && matrix.Translation.LengthSquared() < 1e14f;

    static void BeforeCapture(World world, PopulationData data, SaveClock clock, SaveCaptureOptions? options, out Capture? __state)
    {
        __state = capturing;
        capturing = StockTorsoRules.HasCuts(world) || StockHeadSplitSave.HasCuts(world) ? new(world, data, clock, options ?? new()) : null;
    }
    static Exception? CaptureFinished(Exception? __exception, Capture? __state) { capturing = __state; return __exception; }
    static void Writing(SaveBuilder builder, CharacterCold cold)
    {
        if (capturing is not { } current) return;
        current.Builder = builder;
        current.Written.Add(cold);
    }
    static void Written(World world, CharacterCold cold, CharacterId id, SaveCharacter __result)
    {
        if (cold.Medical?.Dead != true || __result.GameState is not { } state) return;
        if (StockHeadRules.Enabled) StockHeadSplitSave.Write(world, id, cold, state);
        if (!StockTorsoRules.TryGetSaved(world, id, out var cut)) return;
        state.Strings[CutKey] = JsonSerializer.Serialize(new Record(Version, cut.Shape, cut.Seed, cut.Travel,
            cut.Force, cut.Part, cut.BloodColour, cut.Robot, For(world, id)), json);
    }

    static void BeforeMemories()
    {
        if (capturing is { Builder: { } builder } current) AddCorpses(current, builder);
    }
    static void AfterCapture(SaveGame __result)
    {
        if (capturing is { Completed: false } current)
        {
            AddCorpses(current, new SaveBuilder(__result, current.Options.Seed));
            __result.UpdateDerived();
        }
        StockTorsoProxySave.FinishCapture(__result);
    }

    static void AddCorpses(Capture capture, SaveBuilder builder)
    {
        if (capture.Completed) return;
        capture.Completed = true;
        var world = capture.World;
        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal);
        var buildings = HeldSave.BuildingStates(builder.Game);
        var platoons = builder.Game.Platoons.GroupBy(item => item.Name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var factions = builder.Game.Factions.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var id in StockTorsoRules.CutOwners(world).Union(StockHeadSplitSave.Owners(world)).OrderBy(Rng.Key))
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Medical.Dead: true } cold
                || capture.Written.Contains(cold) || (uint)cold.Faction >= (uint)capture.Data.Factions.Count
                || string.IsNullOrEmpty(cold.RecordId)) continue;
            var faction = capture.Data.Factions[cold.Faction];
            locations.TryGetValue(cold, out var previous);
            string? name = cold.Save?.PlatoonName ?? previous?.Platoon;
            var platoon = name is not null ? platoons.GetValueOrDefault(name) : null;
            SaveCharacter? source = platoon?.Characters.FirstOrDefault(actor => actor.Handle.I == (cold.Save?.Slot ?? previous?.Slot));
            if (platoon is null || source is null)
            {
                var state = factions.GetValueOrDefault(faction.Id);
                int serial = state?.PlatoonCounter ?? 0;
                do { name = faction.Name + "_" + serial++; } while (platoons.ContainsKey(name));
                if (state is not null) state.PlatoonCounter = serial;
                platoon = builder.AddPlatoon(name, faction.Name, faction.Id, cold.RecordId, world.Characters.Previous[slot].Position);
                platoons[name] = platoon;
            }
            var saved = (SaveCharacter)writer.Invoke(null, [builder, world, capture.Data, capture.Options, capture.Clock, buildings, races,
                platoon, source, id, faction.Id, false, false])!;
            if (saved.GameState is { } stateSaved)
                stateSaved.Bools[StockTorsoRules.TryGetSaved(world, id, out _) ? Standalone : StockHeadSplitSave.Standalone] = true;
            locations.Remove(cold);
            locations.Add(cold, new(platoon.Name, saved.Handle.I));
            setCount.Invoke(null, [platoon]);
        }
    }

    static void Loaded(World world, SaveGame save, PopulationData data, SaveLoadOptions? options, LoadedSave __result)
    {
        if (!(StockTorsoRules.Enabled || StockHeadRules.Enabled) || world.Saved is not { } context) return;
        var present = new HashSet<Hand>();
        var required = new HashSet<Hand>();
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Next[slot].Alive && world.Characters.Cold(slot) is { Save.Source.Handle: { IsNull: false } handle } cold)
            {
                present.Add(handle);
                Restore(world, world.Characters.IdOf(slot), cold);
                if (StockTorsoRules.Enabled) StockTorsoProxySave.AddRequiredOwners(cold, required);
            }

        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal);
        var parts = new Dictionary<string, BodyPartTemplate?>(StringComparer.Ordinal);
        var assembler = Activator.CreateInstance(assembly, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [data], null)!;
        foreach (var source in save.Characters)
        {
            if (source.Handle.IsNull || present.Contains(source.Handle) || source.GameState is not { } state
                || !(StockTorsoRules.Enabled && (state.Bools.GetValueOrDefault(Standalone) || required.Contains(source.Handle))
                    && Read(state) is not null
                    || StockHeadRules.Enabled && (StockHeadSplitSave.IsStandalone(state) || state.Bools.GetValueOrDefault(StockSaveMigration.WoundCorpse)))) continue;
            var prepared = read.Invoke(null, [source, source.Platoon.Name, data, options?.Appearances, races, parts, (Action<string>)__result.Notes.Add]);
            if (prepared is null || AccessTools.Property(prepared.GetType(), "Medical").GetValue(prepared) is not MedicalState { Dead: true }) continue;
            int faction = data.FactionIndex(source.OwnerFactionId.Length > 0 ? source.OwnerFactionId : source.Platoon.FactionId);
            var squad = new Squad { Id = -1, TemplateId = source.Platoon.SquadTemplate, Faction = faction, Position = source.Position };
            var id = (CharacterId)make.Invoke(null, [world, prepared, source.Platoon.Name, squad, faction, world.Player.Faction, false,
                source.Position, SaveRotation.YawOf(source.Rotation), CharacterTask.Idle, SpeedMode.Free, data, assembler, context, null, __result.Notes])!;
            present.Add(source.Handle);
            context.Spawned[(source.Handle.C, source.Handle.I)] = id;
            __result.Characters.Add(id);
            if (!context.ByName.TryGetValue(source.Platoon.Name, out var platoon))
            {
                platoon = new SavedPlatoon { Source = source.Platoon, Pinned = false, Touched = true, Anchor = new(source.Position.X, source.Position.Z) };
                context.Platoons.Add(platoon);
                context.ByName.Add(platoon.Name, platoon);
            }
            var member = platoon.Members.FirstOrDefault(item => item.Slot == source.Handle.I);
            if (member is null) platoon.Members.Add(member = new SavedMember { Source = source });
            member.Live = id;
            member.Dead = true;
            platoon.Touched = true;
            if (world.Characters.Cold(id.Slot) is { } corpse) Restore(world, id, corpse);
        }
    }
}
