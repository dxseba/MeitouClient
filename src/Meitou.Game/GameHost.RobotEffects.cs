using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class GameHost
{
    int robotSparkBursts;
    readonly RobotDischargeTracker robotDischarges = new();
    readonly Dictionary<CharacterId, CharacterSnapshot> dischargeOwners = new();
    readonly HashSet<CharacterId> dischargeOwnerIds = [];
    readonly List<RobotDischarge> orderedDischarges = new(RobotDischargeTracker.Capacity);

    double RobotDischargeClock => (session.ShownTick + session.SimulationAlpha) * session.World.TickSeconds;

    void EmitRobotCutSparks(in CombatEvent hit, CharacterCold? victim)
    {
        // A follow-through has one gameplay Hit, but each newly broken limb needs its own electrical source.
        var contact = hit;
        if (hit.Kind == CombatEventKind.PartSevered && !robotDischarges.Contains(hit.Victim, hit.Part))
            contact = hit with { Kind = CombatEventKind.Hit, Damage = new(70, 0, 0) };
        if (!RobotCutEffects.EmitsSparks(contact, victim?.Race?.IsRobot == true)
            || gpu?.Particles is null || victim?.Medical is not { } medical
            || contact.Part < 0 || contact.Part >= medical.Parts.Count) return;
        var part = medical.Parts[contact.Part].Template;
        var at = gpu.Characters?.BonePosition(DrawKey(contact.Victim), RobotCutEffects.ImpactBone(part, contact.LowStrike))
            ?? contact.Position + new Vector3(0, RobotCutEffects.FallbackHeight(part), 0);
        if (!robotDischarges.Observe(contact, true, RobotDischargeClock, at, session.World.Seed)) return;
        // The bounded analytic source supplies the impact sparks too; the legacy 4x4 burst is visibly pixelated.
        robotSparkBursts++;
    }

    void UpdateRobotDischarges(World world)
    {
        robotDischarges.Prune(RobotDischargeClock, world.Characters.IsAlive);
        orderedDischarges.Clear();
        dischargeOwnerIds.Clear();
        dischargeOwners.Clear();
        if (gpu.Particles is not { } particles) return;
        particles.RobotDischarges.Clear();
        if (robotDischarges.Count == 0) return;
        foreach (var discharge in robotDischarges.Active)
        {
            orderedDischarges.Add(discharge);
            dischargeOwnerIds.Add(discharge.Victim);
        }
        orderedDischarges.Sort(static (a, b) => b.Serial.CompareTo(a.Serial));
        foreach (var character in session.CurrentSnapshot.Characters)
            if (dischargeOwnerIds.Contains(character.Id)) dischargeOwners[character.Id] = character;
        foreach (var discharge in orderedDischarges)
        {
            if (particles.RobotDischarges.Count >= RobotDischargeGeometry.AnchorLimit) break;
            if (dischargeOwners.TryGetValue(discharge.Victim, out var owner)) AddRobotDischarge(discharge, owner);
        }
    }

    void AddRobotDischarge(in RobotDischarge discharge, CharacterSnapshot owner)
    {
        if (gpu.Characters is not { } renderer) return;
        var medical = session.World.Characters.Cold(owner.Id.Slot)?.Medical;
        if (medical is null || (uint)discharge.Part >= (uint)medical.Parts.Count) return;
        var part = medical.Parts[discharge.Part].Template;
        long key = DrawKey(owner.Id);
        if (owner.TorsoBisection is not null)
        {
            AddRobotDischargeAnchor(discharge, TorsoDrawKey(owner.Id, 0), "Bip01 Pelvis", 0);
            AddRobotDischargeAnchor(discharge, TorsoDrawKey(owner.Id, 1), "Bip01 Spine2", 1);
            return;
        }
        bool detached = AddDetachedRobotDischarges(discharge, owner, part, key);
        string bodyBone = detached && part.Type == BodyPartType.Head ? "Bip01 Neck"
            : RobotCutEffects.ImpactBone(part, !detached && discharge.LowStrike);
        AddRobotDischargeAnchor(discharge, key, bodyBone, 0);
    }

    bool AddDetachedRobotDischarges(in RobotDischarge discharge, CharacterSnapshot owner, BodyPartTemplate part, long key)
    {
        int detachedPart = part.Type == BodyPartType.Head ? -1 : part.LimbSlot;
        if (part.Type != BodyPartType.Head && detachedPart < 0) return false;
        bool detached = false;
        foreach (var piece in owner.DetachedRagdolls)
            if (piece.Part == detachedPart)
            {
                AddRobotDischargeAnchor(discharge, DetachedDrawKey(key, piece.Part, piece.FragmentId),
                    RobotCutEffects.ImpactBone(part, discharge.LowStrike), piece.FragmentId + 1);
                detached = true;
            }
        if (detached) return true;
        foreach (var piece in heads)
        {
            if (piece.From != key || (part.Type == BodyPartType.Head ? piece.PartOnly != 0 : piece.PartOnly >> 8 != (uint)(detachedPart + 1))) continue;
            AddRobotDischargeAnchor(discharge, piece.Key, RobotCutEffects.ImpactBone(part, discharge.LowStrike), piece.FragmentId + 1);
            detached = true;
        }
        return detached;
    }

    void AddRobotDischargeAnchor(in RobotDischarge discharge, long key, string fallbackBone, int fragment)
    {
        if (gpu.Particles is not { } particles || particles.RobotDischarges.Count >= RobotDischargeGeometry.AnchorLimit) return;
        var contact = gpu.Characters?.CutContact(key);
        var medical = session.World.Characters.Cold(discharge.Victim.Slot)?.Medical;
        if (contact is null && medical is not null && (uint)discharge.Part < (uint)medical.Parts.Count
            && medical.Parts[discharge.Part].Template.LimbSlot is >= 0 and <= 3 and var limb)
            contact = gpu.Characters?.LimbCutContact(key, limb);
        var at = contact is { } surface ? surface.Position + surface.Normal * 0.06f
            : gpu.Characters?.BonePosition(key, fallbackBone) ?? discharge.FallbackPosition;
        if (!float.IsFinite(at.LengthSquared())) return;
        uint seed = (uint)(discharge.Seed ^ discharge.Seed >> 32) ^ (uint)fragment * 0x9E3779B9u;
        particles.RobotDischarges.Add(new(at, (float)(RobotDischargeClock - discharge.Born),
            (float)(discharge.Expires - discharge.Born), seed, 0.9f + discharge.Force * 0.7f));
    }

    void ClearRobotDischarges()
    {
        robotSparkBursts = 0;
        ClearRobotBreakExplosions();
        robotDischarges.Clear();
        robotCables.Clear();
        dischargeOwners.Clear();
        dischargeOwnerIds.Clear();
        orderedDischarges.Clear();
        gpu?.Particles?.RobotDischarges.Clear();
    }

    void ReportRobotScene(int frame)
    {
        if (sandboxFoe.IsNone || !session.World.Characters.IsAlive(sandboxFoe)
            || session.World.Characters.Cold(sandboxFoe.Slot)?.Race?.IsRobot != true) return;
        Console.WriteLine($"robot cut frame {frame}: spark bursts {robotSparkBursts}, discharges {robotDischarges.Count}, "
            + $"anchors {gpu.Particles?.RobotDischarges.Count ?? 0}, electrical quads {gpu.Particles?.DrawnRobotDischargeQuads ?? 0}, "
            + $"explosion bursts {robotExplosionBursts}, active explosions {robotExplosions.Count}, explosion quads {gpu.Particles?.DrawnRobotExplosionQuads ?? 0}, "
            + $"smoke puffs {gpu.Particles?.DrawnRobotSmokePuffs ?? 0}, scrap bursts {robotScrapBursts}, "
            + $"scrap pieces {robotScrapPieces}, organic bowel props {gutFalls.Count}, audible debris {characterSounds?.DebrisSounds ?? 0}, cables {robotCables.Count}, spasms {session.World.Systems.OfType<Meitou.Simulation.Ragdolls.RagdollSystem>().FirstOrDefault()?.ElectricalSpasms ?? 0}");
    }
}
