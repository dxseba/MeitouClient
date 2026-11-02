using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;

namespace Meitou.Simulation.Saving;

public static partial class SaveCapture
{
    /// <summary>Cut corpses outside ordinary NPC platoons still have one saved owner and one inventory.</summary>
    static void WriteUnwrittenTorsoOwners(SaveBuilder builder, World world, PopulationData data, SaveCaptureOptions options, SaveClock clock,
        IReadOnlyDictionary<string, FcsRecord> buildings, Dictionary<string, RaceData?> races)
    {
        if (memoryPass is not { } pass) return;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Previous[slot].Alive || table.Cold(slot) is not { Medical.Dead: true } cold
                || cold.TorsoBisection is not { IsValid: true } && cold.HeadCut is not { IsValid: true }) continue;
            var id = table.IdOf(slot);
            if (pass.Handles.ContainsKey(id)) continue;
            var faction = (uint)cold.Faction < (uint)data.Factions.Count ? data.Factions[cold.Faction] : null;
            if (faction is null) continue;
            var platoon = cold.Save is { } link ? builder.Game.Platoons.FirstOrDefault(p => p.Name == link.PlatoonName) : null;
            SaveCharacter? sc = platoon?.Characters.FirstOrDefault(c => c.Handle.I == cold.Save?.Slot);
            if (platoon is null)
            {
                var state = builder.Game.Factions.FirstOrDefault(f => f.Id == faction.Id);
                platoon = builder.AddPlatoon(FreshName(builder.Game, state, faction.Name), faction.Name, faction.Id, cold.RecordId, table.Previous[slot].Position);
                platoon.Record.Bools[cold.TorsoBisection is { IsValid: true } ? TorsoCarrySave.StandaloneKey : HeadCutSave.CorpseKey] = true;
            }
            WriteCharacter(builder, world, data, options, clock, buildings, races, platoon, sc, id, faction.Id, false, false);
            SetCharCount(platoon);
        }
    }
}
