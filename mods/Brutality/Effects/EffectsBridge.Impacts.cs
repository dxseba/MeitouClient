using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Engine;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

internal static partial class EffectsBridge
{
    sealed class OrdinaryImpactState
    {
        public long Sequence;
        public readonly Dictionary<(CharacterId, int), ImpactEffectEvent> Active = [];
        public readonly Queue<(ImpactEffectEvent Impact, int Bone)> Splinters = [];
    }

    const int SplinterMeshCount = 15, PendingSplinterLimit = 64;
    const string MeatSplinter = "Item_Meat_Raw.mesh";
    sealed record SplinterAsset(string Mesh, string? Diffuse, Vector3? Extent, Vector3 Centre);
    sealed record SplinterStyle(LimbLook Look, Vector3 Centre);
    sealed class SplinterAssets
    {
        public readonly SplinterAsset?[] Meshes = new SplinterAsset?[SplinterMeshCount];
        public int Next;
        public long NextWarmFrame = 8;
    }
    static readonly ConditionalWeakTable<object, SplinterStyle> splinterStyles = new();
    static readonly ConditionalWeakTable<DebrisRenderer, SplinterAssets> splinterAssets = new();
    static readonly Dictionary<Type, FieldInfo?> debrisLists = [];
    static readonly Dictionary<Type, Type?> debrisTypes = [];
    static readonly Dictionary<(Type, string), FieldInfo?> debrisFields = [];

    static bool CaptureImpacts(object host, WorldSession session, ParticleRenderer particles, FxWorld effects, double now, bool enabled)
    {
        if (resources is null || drawKey is null) return false;
        bool organicHit = false;
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
                if (bindings.TryGetValue(particles, out var binding))
                    LegacyBloodSprites.Emit(impact, born, now, !ReadBool(Member(host, "o"), "NoParticles"), binding.Sprites,
                        burst => particles.Burst(burst.System, burst.Position, burst.Orientation, seconds: burst.Seconds,
                            size: burst.Size, lifeScale: burst.LifeScale, recolour: burst.Recolour));
                if ((impact.Effects & ImpactEffectKind.OrganicSplinters) == 0) continue;
                organicHit = true;
                if (Debris(host) is not { } renderer) continue;
                var assets = splinterAssets.GetValue(renderer, static _ => new());
                int bone = new Random(unchecked((int)impact.Seed)).Next(1, SplinterMeshCount);
                if (assets.Meshes[0] is { } meat && assets.Meshes[bone] is { } shard) Splinters(host, impact, shard, meat);
                else
                {
                    if (effects.Impacts.Splinters.Count >= PendingSplinterLimit) effects.Impacts.Splinters.Dequeue();
                    effects.Impacts.Splinters.Enqueue((impact, bone));
                }
            }
        foreach (var (key, impact) in effects.Impacts.Active.ToArray())
        {
            if (!enabled || !effects.Recent.TryGetValue(key, out var recent) || recent.Cut.Tick != impact.Hit.Tick
                || now - recent.Born >= 30 || !world.Characters.IsAlive(impact.Hit.Victim))
            { effects.Impacts.Active.Remove(key); continue; }
            if (ImpactContact(host, effects, impact) is { } contact) effects.Contacts[(key.Item1, key.Item2, 0)] = contact;
        }
        return organicHit;
    }

    static DebrisRenderer? Debris(object host) => hostGpu?.GetValue(host) is { } gpu
        ? AccessTools.Field(gpu.GetType(), "Debris")?.GetValue(gpu) as DebrisRenderer : null;

    static void PrepareSplinters(object host, IGpuContext? context, OrdinaryImpactState impacts,
        double now, double tickSeconds, bool organicHit)
    {
        if (context is null || resources is not { } source || Debris(host) is not { } renderer) return;
        var assets = splinterAssets.GetValue(renderer, static _ => new());
        var pending = impacts.Splinters;
        if (assets.Next >= SplinterMeshCount && pending.Count == 0) return;
        long frame = context.Frame.Number;
        if (organicHit)
        {
            assets.NextWarmFrame = Math.Max(assets.NextWarmFrame, frame + 1);
            return;
        }
        // Prefer the exact assets requested by an early hit. Never import more than one mesh per GPU frame.
        while (pending.TryPeek(out var stale) && now - stale.Impact.Hit.Tick * tickSeconds > 1) pending.Dequeue();
        if (frame >= assets.NextWarmFrame)
        {
            while (assets.Next < SplinterMeshCount && assets.Meshes[assets.Next] is not null) assets.Next++;
            int index = assets.Next;
            if (pending.TryPeek(out var queued))
            {
                if (assets.Meshes[0] is null) index = 0;
                else if (assets.Meshes[queued.Bone] is null) index = queued.Bone;
            }
            if (index < SplinterMeshCount)
            {
                context.EnsureFrame();
                assets.Meshes[index] = PrepareSplinter(source, renderer, index);
                assets.NextWarmFrame = frame + (pending.Count > 0 ? 1 : 3);
            }
        }
        while (pending.TryPeek(out var request) && assets.Meshes[0] is { } meat && assets.Meshes[request.Bone] is { } shard)
        {
            pending.Dequeue();
            Splinters(host, request.Impact, shard, meat);
        }
    }

    static SplinterAsset PrepareSplinter(RenderResources source, DebrisRenderer renderer, int index)
    {
        bool flesh = index == 0;
        string mesh = flesh ? MeatSplinter : $"bone{index}.mesh";
        string? materialOf = flesh ? null : "bone1.mesh";
        var material = source.Mesh(mesh)?.SubMeshes.FirstOrDefault() is { } part
            ? source.Materials.Candidates(materialOf ?? mesh, part).FirstOrDefault() : null;
        if (material?.Diffuse is { Length: > 0 } diffuse)
        {
            // The renderer caches by mesh and diffuse. Actual pieces supply their own blood colour.
            LimbLook look = new(diffuse, null, Vector3.Zero, Vector3.One);
            var extent = renderer.Extent(mesh, materialOf, look);
            if (extent.HasValue && renderer.Centre(mesh, look) is { } centre)
                return new(mesh, diffuse, extent, centre);
        }
        return new(mesh, null, renderer.Extent(mesh, materialOf), Vector3.Zero);
    }

    static CutFluidContact? ImpactContact(object host, FxWorld effects, in ImpactEffectEvent impact)
    {
        if (resources is null || drawKey is null || !effects.Drawn.TryGetValue(drawKey(host, impact.Hit.Victim), out var instance)
            || resources.Rigs.For(instance.Appearance) is not { } setup || setup.Rig.Handle(impact.ImpactBone) is not { } bone) return null;
        var skin = Skin(effects, instance, setup);
        var world = SkinWorld(effects, instance, setup);
        var at = Vector3.Transform(Vector3.Transform(setup.Rig.BindPosition[bone], skin[bone]), world);
        var direction = impact.Hit.Travel.LengthSquared() > 1e-6f ? Vector3.Normalize(impact.Hit.Travel) : Vector3.UnitY;
        return new(at, direction);
    }

    static void Splinters(object host, in ImpactEffectEvent impact, SplinterAsset bone, SplinterAsset meat)
    {
        var type = host.GetType();
        if (!debrisLists.TryGetValue(type, out var listField)) debrisLists[type] = listField = AccessTools.Field(type, "pieces");
        if (!debrisTypes.TryGetValue(type, out var pieceType)) debrisTypes[type] = pieceType = type.GetNestedType("Piece", BindingFlags.NonPublic);
        if (pieceType is null || listField?.GetValue(host) is not IList pieces) return;
        var random = new Random(unchecked((int)impact.Seed));
        _ = random.Next(1, SplinterMeshCount); // The bone choice has already been made from this seed.
        var flat = new Vector2(impact.Hit.Travel.X, impact.Hit.Travel.Z);
        var along = flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
        foreach (bool flesh in new[] { false, true })
        {
            var asset = flesh ? meat : bone;
            if (asset.Extent is not { } size) continue;
            float scale = flesh ? .16f : .035f;
            var piece = Activator.CreateInstance(pieceType, nonPublic: true)!;
            Set("Mesh", asset.Mesh); Set("Scale", scale); Set("Extent", size * scale); Set("Floor", float.NaN);
            Set("Born", impact.Hit.Tick); Set("Flesh", flesh); Set("Colour", 0u);
            Set("Bloody", true); Set("BloodColour", impact.BloodColour);
            Set("Position", impact.Hit.VictimPosition + new Vector3(0, 6 + random.NextSingle() * 8, 0));
            Set("Velocity", new Vector3(along.X * 18 + random.NextSingle() * 12 - 6, 18 + random.NextSingle() * 16, along.Y * 18 + random.NextSingle() * 12 - 6));
            Set("Rotation", Quaternion.Identity); Set("Spin", new Vector3(5, 3, 7));
            Vector3? recolour = impact.BloodColour == 0x800000 ? null
                : new Vector3(impact.BloodColour >> 16 & 255, impact.BloodColour >> 8 & 255, impact.BloodColour & 255) / 128;
            Set("Recolour", recolour!);
            if (asset.Diffuse is { } diffuse)
            {
                var blood = impact.BloodColour;
                var colour = new Vector3(blood >> 16 & 255, blood >> 8 & 255, blood & 255) / 255;
                splinterStyles.Add(piece, new(new LimbLook(diffuse, null, Vector3.Zero, colour), asset.Centre));
            }
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
            drawn[i] = piece with { Limb = style.Look, Position = piece.Position - Vector3.Transform(style.Centre * piece.Scale, piece.Rotation) };
        }
    }

    static Quaternion Turn(Vector3 from, Vector3 to)
    {
        var axis = Vector3.Cross(from, to); float length = axis.Length();
        if (length < 1e-5f) return Vector3.Dot(from, to) > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return Quaternion.CreateFromAxisAngle(axis / length, MathF.Atan2(length, Vector3.Dot(from, to)));
    }
}
