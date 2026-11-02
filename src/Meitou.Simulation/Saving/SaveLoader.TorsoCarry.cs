using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;

namespace Meitou.Simulation.Saving;

public static partial class SaveLoader
{
    /// <summary>Restore carried torso owners and saved head-cut corpses without duplicating their original inventory or squad.</summary>
    static void LoadCarriedTorsoOwners(World world, SaveGame save, PopulationData data, SaveLoadOptions options, LoadedSave loaded,
        int playerIndex, Dictionary<string, RaceData?> races, Dictionary<string, BodyPartTemplate?> parts, CharacterAssembly assembly, SavedPopulation context)
    {
        var required = new HashSet<(int C, int I)>();
        foreach (var carrier in save.Platoons.SelectMany(p => p.Characters))
        {
            if (carrier.GameState is { } state && state.Bools.GetValueOrDefault("carrying") && Hand.Read(state, "carrying") is { IsNull: false } partner)
                required.Add((partner.C, partner.I));
            if (carrier.Ai is not { } ai) continue;
            // A queued positive pickup/loot may name a dead player or a remote NPC whose ordinary zone is not loaded yet.
            const string prefix = "meitou tjobK";
            foreach (var (key, kind) in ai.Ints)
                if (kind is 2 or 3 or 4 && key.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(key.AsSpan(prefix.Length), out int index) && index >= 0
                    && Hand.Read(ai, "tjobT" + index) is { IsNull: false } target)
                    required.Add((target.C, target.I));
        }
        foreach (var platoon in save.Platoons)
        {
            bool standalone = platoon.Record.Bools.GetValueOrDefault(TorsoCarrySave.StandaloneKey)
                || platoon.Record.Bools.GetValueOrDefault(HeadCutSave.CorpseKey);
            foreach (var sc in platoon.Characters)
            {
                var handle = (sc.Handle.C, sc.Handle.I);
                bool headCorpse = sc.GameState?.Bools.GetValueOrDefault(HeadCutSave.CorpseKey) == true;
                if ((!standalone && !headCorpse && !required.Contains(handle)) || context.Spawned.ContainsKey(handle)) continue;
                if (SavedCharacters.Read(sc, platoon.Name, data, options.Appearances, races, parts, loaded.Notes.Add) is not { Medical.Dead: true } prepared) continue;
                var probe = new CharacterCold { HeadCut = prepared.HeadCut, Save = new() { Source = sc, PlatoonName = platoon.Name, Slot = sc.Handle.I, Ragdoll = RagdollSave.Read(sc.GameState) } };
                TorsoCarrySave.Read(sc.GameState, probe);
                if (probe.TorsoBisection is not { IsValid: true } && probe.HeadCut is not { IsValid: true }) continue;
                int faction = data.FactionIndex(sc.OwnerFactionId.Length > 0 ? sc.OwnerFactionId : platoon.FactionId);
                var squad = new Squad { Id = -1, TemplateId = platoon.SquadTemplate, Faction = faction, Position = sc.Position };
                var id = SavedCharacters.Make(world, prepared, platoon.Name, squad, faction, playerIndex, false, sc.Position,
                    SaveRotation.YawOf(sc.Rotation), CharacterTask.Idle, SpeedMode.Free, data, assembly, context, null, loaded.Notes);
                context.Spawned[handle] = id;
                LastSpawned[handle] = id;
                loaded.Characters.Add(id);
                if (!context.ByName.TryGetValue(platoon.Name, out var saved))
                {
                    saved = new SavedPlatoon { Source = platoon, Pinned = false, Touched = true, Anchor = new Vector2(platoon.Position.X, platoon.Position.Z) };
                    context.Platoons.Add(saved);
                    context.ByName[platoon.Name] = saved;
                }
                var member = saved.Members.FirstOrDefault(m => m.Slot == sc.Handle.I);
                if (member is null) saved.Members.Add(member = new SavedMember { Source = sc });
                member.Live = id;
                member.Dead = true; // already made here: later activation must not create a duplicate corpse.
                saved.Touched = true;
            }
        }
    }
}
