using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Engine;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

internal static partial class EffectsBridge
{
    sealed class OrdinaryImpactState
    {
        public long Sequence;
        public readonly Dictionary<(CharacterId, int), ImpactEffectEvent> Active = [];
    }
    sealed record SplinterStyle(uint BloodColour);
    static readonly ConditionalWeakTable<object, SplinterStyle> splinterStyles = new();
    static readonly Dictionary<(string, uint), LimbLook?> splinterLooks = [];
    static readonly Dictionary<Type, FieldInfo?> debrisLists = [];
    static readonly Dictionary<Type, Type?> debrisTypes = [];
    static readonly Dictionary<(Type, string), FieldInfo?> debrisFields = [];

    static void CaptureImpacts(object host, WorldSession session, ParticleRenderer particles, FxWorld effects, double now, bool enabled)
    {
        if (resources is null || drawKey is null) return;
        var world = session.World;
        if (ImpactEffects.ForWorld(world)?.FrameAt(session.ShownTick) is { } frame)
            foreach (var impact in frame.Events)
            {
                if (impact.Sequence <= effects.Impacts.Sequence) continue;
                effects.Impacts.Sequence = impact.Sequence;
                double born = impact.Hit.Tick * world.TickSeconds;
                double age = now - born;
                if (age < 0 || age > (impact.Robot ? 30 : 1)) continue;
                var hit = impact.Hit;
                var at = ImpactContact(host, effects, impact)?.Position ?? hit.VictimPosition + Vector3.UnitY * impact.FallbackHeight;
                var travel = new Vector2(hit.Travel.X, hit.Travel.Z);
                if (enabled && (impact.Effects & (ImpactEffectKind.RobotShortCircuit | ImpactEffectKind.RobotScrap)) != 0)
                {
                    ulong seed = Rng.Mix(world.Seed ^ Rng.Key(hit.Victim) ^ (ulong)hit.Tick ^ ((ulong)(uint)hit.Part << 32));
                    bool rupture = impact.RuptureKind >= 0;
                    var cut = new GoreEffectCut(hit.Victim, hit.Part, rupture ? impact.RuptureKind : 3, -1, hit.Tick, travel,
                        hit.Damage.Cut, impact.BloodColour, impact.Seed, true, at)
                    { Rupture = rupture, Discharge = (impact.Effects & ImpactEffectKind.RobotShortCircuit) != 0, ExplosionSeed = (uint)(seed ^ seed >> 32) };
                    effects.Scene.Begin(cut, born);
                    if (!effects.Recent.ContainsKey((cut.Owner, cut.Part)) && effects.Recent.Count >= 128)
                        effects.Recent.Remove(effects.Recent.MinBy(row => row.Value.Born).Key);
                    effects.Recent[(cut.Owner, cut.Part)] = (cut, born);
                    if (effects.Impacts.Active.Count >= 128) effects.Impacts.Active.Remove(effects.Impacts.Active.Keys.First());
                    effects.Impacts.Active[(hit.Victim, hit.Part)] = impact;
                }
                if (enabled && (impact.Effects & ImpactEffectKind.OrganicSpray) != 0) Spray(particles, impact);
                if ((impact.Effects & ImpactEffectKind.OrganicSplinters) != 0) Splinters(host, impact);
            }
        foreach (var (key, impact) in effects.Impacts.Active.ToArray())
        {
            if (!enabled || !effects.Recent.TryGetValue(key, out var recent) || recent.Cut.Tick != impact.Hit.Tick
                || now - recent.Born >= 30 || !world.Characters.IsAlive(impact.Hit.Victim))
            { effects.Impacts.Active.Remove(key); continue; }
            if (ImpactContact(host, effects, impact) is { } contact) effects.Contacts[(key.Item1, key.Item2, 0)] = contact;
        }
    }

    static CutFluidContact? ImpactContact(object host, FxWorld effects, in ImpactEffectEvent impact)
    {
        if (resources is null || drawKey is null || !effects.Drawn.TryGetValue(drawKey(host, impact.Hit.Victim), out var instance)
            || resources.Rigs.For(instance.Appearance) is not { } setup || setup.Rig.Handle(impact.ImpactBone) is not { } bone) return null;
        var skin = Skin(effects, instance, setup);
        var world = instance.World ?? (instance.Rotation is { } q ? CharacterRigs.Placement(setup, instance.Position, q, instance.Scale)
            : CharacterRigs.Placement(setup, instance.Position, instance.Yaw, instance.Scale));
        var at = Vector3.Transform(Vector3.Transform(setup.Rig.BindPosition[bone], skin[bone]), world);
        var direction = impact.Hit.Travel.LengthSquared() > 1e-6f ? Vector3.Normalize(impact.Hit.Travel) : Vector3.UnitY;
        return new(at, direction);
    }

    static void Spray(ParticleRenderer particles, in ImpactEffectEvent impact)
    {
        var random = new Random(unchecked((int)(impact.Seed ^ impact.Seed >> 32)));
        float force = MathF.Min((impact.Hit.Damage.Cut + impact.Hit.Damage.Pierce) / 20, 1);
        var flat = new Vector2(impact.Hit.Travel.X, impact.Hit.Travel.Z);
        var along = flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
        var from = impact.Hit.VictimPosition + new Vector3(0, 7 + random.NextSingle() * 7, 0);
        Vector3? recolour = impact.BloodColour == 0x800000 ? null : new Vector3(impact.BloodColour >> 16 & 255, impact.BloodColour >> 8 & 255, impact.BloodColour & 255) / 128;
        for (int i = 0; i < 1 + (int)(force * 3); i++)
        {
            var spray = Vector3.Normalize(new Vector3(along.X + random.NextSingle() - .5f, .3f + random.NextSingle() * .6f, along.Y + random.NextSingle() - .5f));
            particles.Burst("HackBlood-system", from, Turn(Vector3.UnitZ, spray), .1f + force * .2f, 1, 1.8f, recolour);
        }
    }

    static void Splinters(object host, in ImpactEffectEvent impact)
    {
        if (hostGpu?.GetValue(host) is not { } gpu || AccessTools.Field(gpu.GetType(), "Debris")?.GetValue(gpu) is not DebrisRenderer renderer) return;
        var type = host.GetType();
        if (!debrisLists.TryGetValue(type, out var listField)) debrisLists[type] = listField = AccessTools.Field(type, "pieces");
        if (!debrisTypes.TryGetValue(type, out var pieceType)) debrisTypes[type] = pieceType = type.GetNestedType("Piece", BindingFlags.NonPublic);
        if (pieceType is null || listField?.GetValue(host) is not IList pieces) return;
        var random = new Random(unchecked((int)impact.Seed));
        var flat = new Vector2(impact.Hit.Travel.X, impact.Hit.Travel.Z);
        var along = flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
        foreach (bool flesh in new[] { false, true })
        {
            string mesh = flesh ? "Item_Meat_Raw.mesh" : $"bone{random.Next(1, 15)}.mesh";
            if (renderer.Extent(mesh, flesh ? null : "bone1.mesh") is not { } size) continue;
            float scale = flesh ? .16f : .035f;
            var piece = Activator.CreateInstance(pieceType, nonPublic: true)!;
            Set("Mesh", mesh); Set("Scale", scale); Set("Extent", size * scale); Set("Floor", float.NaN);
            Set("Born", impact.Hit.Tick); Set("Flesh", flesh); Set("Colour", 0u);
            Set("Bloody", true); Set("BloodColour", impact.BloodColour);
            Set("Position", impact.Hit.VictimPosition + new Vector3(0, 6 + random.NextSingle() * 8, 0));
            Set("Velocity", new Vector3(along.X * 18 + random.NextSingle() * 12 - 6, 18 + random.NextSingle() * 16, along.Y * 18 + random.NextSingle() * 12 - 6));
            Set("Rotation", Quaternion.Identity); Set("Spin", new Vector3(5, 3, 7));
            Vector3? recolour = impact.BloodColour == 0x800000 ? null
                : new Vector3(impact.BloodColour >> 16 & 255, impact.BloodColour >> 8 & 255, impact.BloodColour & 255) / 128;
            Set("Recolour", recolour!);
            splinterStyles.Add(piece, new(impact.BloodColour));
            pieces.Add(piece);
            void Set(string name, object value)
            {
                if (!debrisFields.TryGetValue((pieceType, name), out var field)) debrisFields[(pieceType, name)] = field = AccessTools.Field(pieceType, name);
                field?.SetValue(piece, value);
            }
        }
        while (pieces.Count > 400) pieces.RemoveAt(0);
    }

    // Reuse the native blood-capable debris material without introducing gore fields into the engine.
    static void StyleOrganicDebris(object __instance)
    {
        if (resources is null || hostGpu?.GetValue(__instance) is not { } gpu
            || AccessTools.Field(gpu.GetType(), "Debris")?.GetValue(gpu) is not DebrisRenderer renderer
            || AccessTools.Field(gpu.GetType(), "DebrisPieces")?.GetValue(gpu) is not List<DebrisPiece> drawn
            || AccessTools.Field(__instance.GetType(), "pieces")?.GetValue(__instance) is not IList pieces) return;
        var styles = new Dictionary<(string, Vector3, float, Quaternion), SplinterStyle>();
        foreach (var piece in pieces)
        {
            if (piece is null || !splinterStyles.TryGetValue(piece, out var style)) continue;
            var type = piece.GetType();
            styles[((string)AccessTools.Field(type, "Mesh").GetValue(piece)!,
                (Vector3)AccessTools.Field(type, "Position").GetValue(piece)!,
                (float)AccessTools.Field(type, "Scale").GetValue(piece)!,
                (Quaternion)AccessTools.Field(type, "Rotation").GetValue(piece)!)] = style;
        }
        if (styles.Count == 0) return;
        for (int i = 0; i < drawn.Count; i++)
        {
            var piece = drawn[i];
            if (!styles.TryGetValue((piece.Mesh, piece.Position, piece.Scale, piece.Rotation), out var style)) continue;
            var key = (piece.Mesh, style.BloodColour);
            if (!splinterLooks.TryGetValue(key, out var look))
            {
                var mesh = resources.Mesh(piece.Mesh);
                var resolver = new MaterialResolver(resources.Rigs.Database, resources.Assets.Materials, resources.Assets);
                var material = mesh?.SubMeshes.FirstOrDefault() is { } part
                    ? resolver.Candidates(piece.Mesh == "Item_Meat_Raw.mesh" ? piece.Mesh : "bone1.mesh", part).FirstOrDefault() : null;
                look = material?.Diffuse is { Length: > 0 } diffuse
                    ? new(diffuse, null, Vector3.Zero,
                        new Vector3(style.BloodColour >> 16 & 255, style.BloodColour >> 8 & 255, style.BloodColour & 255) / 255) : null;
                splinterLooks[key] = look;
            }
            if (look is null || renderer.Extent(piece.Mesh, limb: look) is null || renderer.Centre(piece.Mesh, look) is not { } centre) continue;
            drawn[i] = piece with { Limb = look, Position = piece.Position - Vector3.Transform(centre * piece.Scale, piece.Rotation) };
        }
    }

    static Quaternion Turn(Vector3 from, Vector3 to)
    {
        var axis = Vector3.Cross(from, to); float length = axis.Length();
        if (length < 1e-5f) return Vector3.Dot(from, to) > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return Quaternion.CreateFromAxisAngle(axis / length, MathF.Atan2(length, Vector3.Dot(from, to)));
    }
}
