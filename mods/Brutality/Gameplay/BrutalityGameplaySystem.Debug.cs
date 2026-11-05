using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;

namespace Meitou.Brutality;

public enum GorePreviewKind { Torso, VerticalTorso, Head, Limb, SevereHead, ChestHole, StomachHole }

public sealed partial class BrutalityGameplaySystem
{
    /// <summary>Explicit developer-only scene control; ordinary combat never calls this.</summary>
    public bool ApplyDebugCut(World world, CharacterId target, GorePreviewKind kind, int variant = 0)
    {
        if (Find(world, target) is not { } actor || !world.Characters.TryResolveNext(target, out int slot)
            || world.Characters.Cold(slot) is not { Medical: { } medical, Race: { } race } cold) return false;
        if (kind == GorePreviewKind.SevereHead)
        {
            actor.Severe = new(4, (variant % 3) switch { 0 => SevereHeadWoundKind.JawLost, 1 => SevereHeadWoundKind.Scalp, _ => SevereHeadWoundKind.BrokenSkull });
        }
        else
        {
            var context = new MedicalContext(constants, options, race);
            int chest = medical.Parts.ToList().FindIndex(p => p.Template.Type == BodyPartType.Torso);
            if (chest < 0) return false;
            // Punch-through holes 0-2 leave the target standing so the opening can be inspected; 3-5 are the same holes on a corpse.
            bool hole = kind is GorePreviewKind.ChestHole or GorePreviewKind.StomachHole;
            if (!hole || variant >= 3) medical.ApplyHit(chest, new(10000, 0, 0), context);
            switch (kind)
            {
                case GorePreviewKind.Torso: actor.Torso = new(new Vector4(new Vector3(.17f, 1, .08f), -12.7f) / new Vector3(.17f, 1, .08f).Length()); break;
                case GorePreviewKind.VerticalTorso: actor.Torso = new(new Vector4(Vector3.Normalize(new Vector3(1, .18f, .12f)), -.15f), true); break;
                case GorePreviewKind.Head:
                    // Use the same normalized head shapes as real gameplay, including a genuinely longitudinal split and a low neck cut.
                    actor.Head = HeadCutPolicy.Preview((variant % 3) switch { 0 => "oblique", 1 => "split", _ => "neck" });
                    break;
                case GorePreviewKind.Limb: actor.Limbs = Array.AsReadOnly(new[] { new LimbCutState(Math.Abs(variant) % 4, .5f, new(.25f, -.15f)) }); break;
                case GorePreviewKind.ChestHole: actor.ChestHole = ChestHolePolicy.Preview(variant); break;
                case GorePreviewKind.StomachHole: actor.ChestHole = ChestHolePolicy.Preview(variant, ChestHoleState.Abdomen); break;
            }
        }
        int part = medical.Parts.ToList().FindIndex(p => kind is GorePreviewKind.Head or GorePreviewKind.SevereHead
            ? p.Template.Type == BodyPartType.Head : kind == GorePreviewKind.Limb ? p.Template.LimbSlot == Math.Abs(variant) % 4 : p.Template.Type == BodyPartType.Torso);
        var hit = new Meitou.Simulation.Combat.CombatHit(world.Tick, Meitou.Simulation.Combat.CombatHitSource.Melee,
            CharacterId.None, target, 0, 0, part, new(200, 0, 0), default, world.Characters.Next[slot].Position,
            Vector3.UnitX, world.Characters.Next[slot].Yaw, 2, false, true, true, false);
        switch (kind)
        {
            case GorePreviewKind.Torso: case GorePreviewKind.VerticalTorso: Add(hit, GoreCutKind.Torso, torso: actor.Torso); break;
            case GorePreviewKind.Head: Add(hit, GoreCutKind.Head, head: actor.Head); break;
            case GorePreviewKind.Limb: Add(hit, GoreCutKind.Limb, limb: actor.Limbs[0], limbSlot: actor.Limbs[0].LimbSlot); break;
            case GorePreviewKind.SevereHead: Add(hit, GoreCutKind.SevereHead, severe: actor.Severe); break;
            case GorePreviewKind.ChestHole or GorePreviewKind.StomachHole: Add(hit, GoreCutKind.ChestHole, chestHole: actor.ChestHole); break;
        }
        changed = true;
        return true;
    }
}

internal sealed class GoreVisualCheckSystem(string profile) : ITickSystem
{
    bool applied, carryRequested, putDownRequested, nativeInputs;
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        var fields = profile.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (fields.FirstOrDefault()?.Equals("head", StringComparison.OrdinalIgnoreCase) != true
            || !fields.Any(field => field.Equals("native", StringComparison.OrdinalIgnoreCase))) return;
        nativeInputs = true;
        try { SlowWorld(world); }
        finally { nativeInputs = false; }
    }
    CharacterId carrier = CharacterId.None, owner = CharacterId.None, carryTarget = CharacterId.None;
    long appliedTick, heldSince = -1;
    public void SlowWorld(World world)
    {
        var fields = profile.Split(':', StringSplitOptions.RemoveEmptyEntries);
        bool negative = fields.Any(field => field.Equals("carry-negative", StringComparison.OrdinalIgnoreCase));
        bool carry = negative || fields.Any(field => field.Equals("carry", StringComparison.OrdinalIgnoreCase));
        bool nativeHead = fields.FirstOrDefault()?.Equals("head", StringComparison.OrdinalIgnoreCase) == true
            && fields.Any(field => field.Equals("native", StringComparison.OrdinalIgnoreCase));
        if (!applied)
        {
            if (nativeHead && !nativeInputs) return;
            int firstTick = nativeHead ? 120 : 3;
            string? after = fields.FirstOrDefault(field => field.StartsWith("after=", StringComparison.OrdinalIgnoreCase));
            if (after is not null && int.TryParse(after.AsSpan(6), out int delayed)) firstTick = Math.Max(firstTick, delayed);
            if (world.Tick < firstTick || BrutalityRuntime.For(world) is not { } gameplay) return;
            var targets = Enumerable.Range(0, world.Characters.HighWater).Where(slot => world.Characters.Next[slot].Alive
                && world.Characters.Cold(slot) is { Appearance: not null, Medical: not null }).ToArray();
            if (targets.Length < 2) return;
            var kind = fields.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "vertical" => GorePreviewKind.VerticalTorso, "head" => GorePreviewKind.Head, "limb" => GorePreviewKind.Limb,
                "severe" => GorePreviewKind.SevereHead, "chest" => GorePreviewKind.ChestHole, "stomach" => GorePreviewKind.StomachHole,
                _ => GorePreviewKind.Torso,
            };
            int variant = fields.Length > 1 && int.TryParse(fields[1], out int number) ? number : 1;
            owner = world.Characters.IdOf(targets[1]);
            carrier = world.Characters.IdOf(targets.FirstOrDefault(slot => world.Characters.Cold(slot)?.IsPlayer == true, targets[0]));
            if (carry && owner == carrier) owner = world.Characters.IdOf(targets.First(slot => slot != carrier.Slot));
            if (fields.Any(field => field.Equals("all", StringComparison.OrdinalIgnoreCase)))
            {
                int changed = 0;
                foreach (int slot in targets)
                    if (nativeHead ? gameplay.ApplyMatchedNativeHeadCut(world, world.Characters.IdOf(slot), carrier, variant)
                        : gameplay.ApplyDebugCut(world, world.Characters.IdOf(slot), kind, variant)) changed++;
                applied = true;
                Console.WriteLine($"Brutality visual crowd: {kind}/{variant}, targets={changed}");
            }
            else applied = nativeHead ? gameplay.ApplyMatchedNativeHeadCut(world, owner, carrier, variant)
                : gameplay.ApplyDebugCut(world, owner, kind, variant);
            appliedTick = world.Tick;
            Console.WriteLine($"Brutality visual check: {kind}/{variant}, target {owner}, applied={applied}");
            return;
        }
        if (!carry || !world.Characters.TryResolveNext(carrier, out int carrierSlot) || world.Characters.Cold(carrierSlot) is not { } holder) return;
        if (!carryRequested && world.Tick > appliedTick + 1)
        {
            carryTarget = owner;
            if (negative && (BodyFragmentProxySystem.ForWorld(world) is not { } proxies || !proxies.TryProxy(owner, 1, out carryTarget))) return;
            if (!world.Characters.TryResolveNext(carryTarget, out int bodySlot) || world.Characters.Cold(bodySlot) is not { } body
                || !CarrySystem.CanBeLifted(world, body) || !CarrySystem.CanCarry(world, holder, ordered: true)) return;
            world.Commands.Enqueue(new PickUpOrder([carrier], carryTarget) { Tick = world.Tick + 1 });
            carryRequested = true;
            Console.WriteLine($"Brutality visual carry: requested {(negative ? "negative" : "positive")}, carrier {carrier}, body {carryTarget}");
        }
        if (holder.Carrying == carryTarget && !carryTarget.IsNone && heldSince < 0)
        {
            heldSince = world.Tick;
            Console.WriteLine($"Brutality visual carry: linked at tick {heldSince}");
        }
        string? putDown = fields.FirstOrDefault(field => field.Equals("putdown", StringComparison.OrdinalIgnoreCase)
            || field.StartsWith("putdown=", StringComparison.OrdinalIgnoreCase));
        int delay = putDown is not null && putDown.Contains('=') && int.TryParse(putDown.Split('=')[1], out int parsed) ? Math.Max(1, parsed) : 600;
        if (!putDownRequested && putDown is not null && heldSince >= 0 && world.Tick >= heldSince + delay)
        {
            world.Commands.Enqueue(new PutDownOrder([carrier]) { Tick = world.Tick + 1 }); putDownRequested = true;
            Console.WriteLine($"Brutality visual carry: put-down queued at tick {world.Tick}");
        }
    }
}
