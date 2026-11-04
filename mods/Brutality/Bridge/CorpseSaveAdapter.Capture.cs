using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

internal static partial class CorpseSaveAdapter
{
    sealed class CaptureContext(World world, PopulationData data, SaveClock clock, SaveCaptureOptions options)
    {
        public readonly World World = world;
        public readonly PopulationData Data = data;
        public readonly SaveClock Clock = clock;
        public readonly SaveCaptureOptions Options = options;
        public readonly HashSet<CharacterCold> Written = new(ReferenceEqualityComparer.Instance);
        public SaveBuilder? Builder;
        public bool Completed;
    }
    sealed class CorpseFile(string name, int slot) { public string Name = name; public int Slot = slot; }
    static readonly ConditionalWeakTable<CharacterCold, CorpseFile> corpseFiles = new();
    [ThreadStatic] static CaptureContext? capture;
    static readonly MethodInfo writer = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Length == 13);
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture)),
            prefix: new HarmonyMethod(typeof(CorpseSaveAdapter), nameof(CapturePrefix)),
            postfix: new HarmonyMethod(typeof(CorpseSaveAdapter), nameof(CapturePostfix)),
            finalizer: new HarmonyMethod(typeof(CorpseSaveAdapter), nameof(CaptureFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(SaveCapture), "WriteMemories"), prefix: new HarmonyMethod(typeof(CorpseSaveAdapter), nameof(BeforeMemories)));
        var withCold = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "WriteCharacter" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
        harmony.Patch(withCold, prefix: new HarmonyMethod(typeof(CorpseSaveAdapter), nameof(CharacterWriting)));
    }
    static void CapturePrefix(World world, PopulationData data, SaveClock clock, SaveCaptureOptions? options, out CaptureContext? __state)
    { __state = capture; capture = new(world, data, clock, options ?? new()); }
    static Exception? CaptureFinalizer(Exception? __exception, CaptureContext? __state) { capture = __state; return __exception; }
    static void CharacterWriting(SaveBuilder builder, CharacterCold cold)
    { if (capture is { } current) { current.Builder = builder; current.Written.Add(cold); } }
    static void BeforeMemories() { if (capture is { Builder: { } builder } current) AddUnwritten(current, builder); }
    static void CapturePostfix(SaveGame __result)
    {
        if (capture is not { Completed: false } current) return;
        AddUnwritten(current, new SaveBuilder(__result, current.Options.Seed)); __result.UpdateDerived();
    }
    static void AddUnwritten(CaptureContext current, SaveBuilder builder)
    {
        if (current.Completed) return; current.Completed = true;
        var world = current.World;
        if (BrutalityRuntime.For(world) is not { } gameplay) return;
        var pass = AccessTools.Field(typeof(SaveCapture), "memoryPass").GetValue(null);
        var handles = pass is null ? new Dictionary<CharacterId, Hand>() : (Dictionary<CharacterId, Hand>)AccessTools.Field(pass.GetType(), "Handles").GetValue(pass)!;
        var buildings = HeldSave.BuildingStates(builder.Game);
        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal);
        var platoons = builder.Game.Platoons.ToDictionary(platoon => platoon.Name, StringComparer.Ordinal);
        var factions = builder.Game.Factions.GroupBy(faction => faction.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var actor in gameplay.ActorViews)
        {
            if (actor.Torso is null && actor.Head is null || handles.ContainsKey(actor.Character)
                || !world.Characters.TryResolveNext(actor.Character, out int slot) || world.Characters.Cold(slot) is not { Medical.Dead: true } cold
                || current.Written.Contains(cold) || (uint)cold.Faction >= (uint)current.Data.Factions.Count || string.IsNullOrEmpty(cold.RecordId)) continue;
            var faction = current.Data.Factions[cold.Faction];
            corpseFiles.TryGetValue(cold, out var remembered);
            string? name = cold.Save?.PlatoonName ?? remembered?.Name;
            var platoon = name is null ? null : platoons.GetValueOrDefault(name);
            SaveCharacter? source = platoon?.Characters.FirstOrDefault(source => source.Handle.I == (cold.Save?.Slot ?? remembered?.Slot));
            if (platoon is null)
            {
                var state = factions.GetValueOrDefault(faction.Id); int number = state?.PlatoonCounter ?? 0;
                do { name = faction.Name + "_" + number++; } while (platoons.ContainsKey(name));
                if (state is not null) state.PlatoonCounter = number;
                platoon = builder.AddPlatoon(name, faction.Name, faction.Id, cold.RecordId, world.Characters.Previous[slot].Position);
                platoons[name] = platoon;
            }
            platoon.Record.Bools[actor.Torso is not null ? TorsoCorpse : HeadCorpse] = true;
            var saved = (SaveCharacter)writer.Invoke(null, [builder, world, current.Data, current.Options, current.Clock, buildings, races,
                platoon, source, actor.Character, faction.Id, false, false])!;
            corpseFiles.Remove(cold); corpseFiles.Add(cold, new(platoon.Name, saved.Handle.I));
            AccessTools.Method(typeof(SaveCapture), "SetCharCount").Invoke(null, [platoon]);
        }
    }
    internal static void CharacterWritten(World world, CharacterCold cold, SaveCharacter saved)
    {
        if (saved.GameState is not { } state || BrutalityRuntime.For(world)?.DurableFor(cold, "capture") is not { } actor) return;
        if (actor.Head is { } head)
        {
            var plane = head.Plane;
            state.Strings["meitou head cut"] = FormattableString.Invariant($"H1 {(int)head.Mode} {plane.X:R} {plane.Y:R} {plane.Z:R} {plane.W:R}");
            if (cold.Medical?.Dead == true) state.Bools[HeadCorpse] = true;
        }
        if (actor.Torso is { } cut)
        {
            var plane = cut.Plane;
            state.Strings["meitou torso cut"] = FormattableString.Invariant($"{plane.X:R} {plane.Y:R} {plane.Z:R} {plane.W:R} {(cut.FullBody ? 1 : 0)}");
        }
    }
}
