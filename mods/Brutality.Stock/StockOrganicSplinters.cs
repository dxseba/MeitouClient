using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ogre;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Objects;

namespace Meitou.Brutality.Effects;

/// <summary>Original meat and bone splinters, with bounded admission and native debris materials.</summary>
internal sealed class StockOrganicSplinters(Type host, Type graphics, Type characters)
{
    const int MaxPending = 48, MaxPieces = 96;
    const double MaxAge = 8;
    readonly FieldInfo debris = AccessTools.Field(graphics, "Debris");
    readonly FieldInfo output = AccessTools.Field(graphics, "DebrisPieces");
    readonly FieldInfo art = AccessTools.Field(host, "Assets");
    readonly FieldInfo context = AccessTools.Field(typeof(DebrisRenderer), "_gpu");
    readonly FieldInfo content = AccessTools.Field(characters, "_content");
    readonly FieldInfo materials = AccessTools.Field(AccessTools.Field(characters, "_content").FieldType, "_resolver");
    readonly Queue<Request> pending = [];
    readonly List<Piece> pieces = [];
    readonly ConditionalWeakTable<DebrisRenderer, Meshes> meshes = new();
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    double? previous;
    bool shownExit;

    sealed record Request(CutFluidContact Contact, uint Blood, ulong Seed, int Bone, bool Exit, double Born);
    sealed record Asset(string Mesh, string? Diffuse, Vector3 Extent, Vector3 Centre);
    sealed class Meshes
    {
        public readonly Dictionary<int, Asset?> Assets = [];
        public long Prepared = -1;
    }
    sealed class Piece(Asset asset, LimbLook? look, Vector3 position, Vector3 velocity, float floor, float scale, double born)
    {
        public readonly Asset Asset = asset;
        public readonly LimbLook? Look = look;
        public readonly float Floor = floor, Scale = scale;
        public readonly double Born = born;
        public Vector3 Position = position, Velocity = velocity;
        public Quaternion Rotation = Quaternion.Identity;
        public bool Resting;
    }

    internal void Queue(CutFluidContact contact, uint blood, ulong seed, bool exit, double born)
    {
        if (!float.IsFinite(contact.Position.LengthSquared()) || !float.IsFinite(contact.Normal.LengthSquared()) || !double.IsFinite(born)) return;
        if (pending.Count >= MaxPending) pending.Dequeue();
        pending.Enqueue(new(contact, blood, seed, new Random(unchecked((int)seed)).Next(1, 15), exit, born));
    }

    internal void Advance(object host, object frame, object renderer, double now, Func<Vector3, float> ground)
    {
        if (pending.Count == 0 && pieces.Count == 0) { previous = null; return; }
        if (debris.GetValue(frame) is not DebrisRenderer draw || output.GetValue(frame) is not List<DebrisPiece> drawn) return;
        float dt = (float)Math.Clamp(now - (previous ?? now), 0, .1);
        previous = now;
        pieces.RemoveAll(piece => now - piece.Born > MaxAge);
        foreach (var piece in pieces)
        {
            if (!piece.Resting && dt > 0) Step(piece, dt);
            var position = piece.Look is null ? piece.Position
                : piece.Position - Vector3.Transform(piece.Asset.Centre * piece.Scale, piece.Rotation);
            drawn.Add(new(piece.Asset.Mesh, piece.Scale, piece.Rotation, position, Limb: piece.Look));
        }
        if (!pending.TryPeek(out _) || context.GetValue(draw) is not IGpuContext gpu) return;
        var cache = meshes.GetOrCreateValue(draw);
        int emitted = 0;
        while (emitted < 8 && pending.TryPeek(out var request))
        {
            if (now - request.Born > 1) { pending.Dequeue(); continue; }
            if (!Prepare(host, renderer, draw, gpu, cache, 0) || !Prepare(host, renderer, draw, gpu, cache, request.Bone)) return;
            pending.Dequeue();
            emitted++;
            Spawn(cache, request, ground);
        }
    }

    bool Prepare(object host, object renderer, DebrisRenderer draw, IGpuContext gpu, Meshes cache, int index)
    {
        if (cache.Assets.ContainsKey(index)) return true;
        long frame = gpu.Frame.Number;
        if (cache.Prepared == frame) return false;
        cache.Prepared = frame;
        string mesh = index == 0 ? "Item_Meat_Raw.mesh" : $"bone{index}.mesh";
        string materialOf = index == 0 ? mesh : "bone1.mesh";
        Asset? asset = null;
        try
        {
            gpu.EnsureFrame();
            string? diffuse = null;
            if (content.GetValue(renderer) is { } source && materials.GetValue(source) is MaterialResolver resolver)
            {
                diffuse = resolver.Candidates(materialOf, new OgreSubMesh()).FirstOrDefault()?.Diffuse;
                if (diffuse is null && art.GetValue(host) is AssetLocator assets && assets.Find(mesh) is { } file
                    && OgreMeshReader.ReadFile(file).SubMeshes.FirstOrDefault() is { } part)
                    diffuse = resolver.Candidates(materialOf, part).FirstOrDefault()?.Diffuse;
            }
            LimbLook? look = string.IsNullOrEmpty(diffuse) ? null : new(diffuse, null, Vector3.Zero, Vector3.One);
            if (draw.Extent(mesh, materialOf, look) is { } extent && draw.Centre(mesh, look) is { } centre)
                asset = new(mesh, diffuse, extent, centre);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or NotSupportedException)
        {
            if (trace) Console.WriteLine($"Brutality stock organic splinter skipped: {error.Message}");
        }
        cache.Assets.Add(index, asset);
        return true;
    }

    void Spawn(Meshes cache, Request request, Func<Vector3, float> ground)
    {
        float floor = ground(request.Contact.Position);
        if (!float.IsFinite(floor)) return;
        var random = new Random(unchecked((int)request.Seed));
        _ = random.Next(1, 15);
        var direction = Unit(request.Contact.Normal);
        var colour = new Vector3((request.Blood >> 16) & 255, (request.Blood >> 8) & 255, request.Blood & 255) / 255;
        int count = request.Exit ? 5 : 2, spawned = 0;
        for (int i = 0; i < count; i++)
        {
            bool flesh = request.Exit ? i < 3 : i == 1;
            if (cache.Assets[flesh ? 0 : request.Bone] is not { } asset) continue;
            float speed = request.Exit ? 22 + random.NextSingle() * 14 : 18;
            var velocity = direction * speed + new Vector3(random.NextSingle() * 8 - 4,
                request.Exit ? 6 + random.NextSingle() * 12 : 18 + random.NextSingle() * 16, random.NextSingle() * 8 - 4);
            float scale = flesh ? request.Exit ? .1f + random.NextSingle() * .08f : .16f : .035f;
            LimbLook? look = asset.Diffuse is { } diffuse ? new(diffuse, null, Vector3.Zero, colour) : null;
            if (pieces.Count >= MaxPieces) pieces.RemoveAt(0);
            pieces.Add(new(asset, look, request.Contact.Position, velocity, floor, scale, request.Born));
            spawned++;
        }
        if (trace && request.Exit && !shownExit && spawned > 0)
        {
            shownExit = true;
            Console.WriteLine($"Brutality stock organic exit splinters={spawned} at={request.Contact.Position}");
        }
    }

    static void Step(Piece piece, float dt)
    {
        piece.Position += piece.Velocity * dt - Vector3.UnitY * (49.05f * dt * dt);
        piece.Velocity -= Vector3.UnitY * (98.1f * dt);
        piece.Rotation = Quaternion.Normalize(piece.Rotation * Quaternion.CreateFromYawPitchRoll(3 * dt, 5 * dt, 7 * dt));
        float support = piece.Floor + piece.Scale * piece.Asset.Extent.Length() * .55f;
        if (piece.Position.Y > support) return;
        piece.Position = piece.Position with { Y = support };
        piece.Velocity = new(piece.Velocity.X * .6f, MathF.Abs(piece.Velocity.Y) * .35f, piece.Velocity.Z * .6f);
        piece.Resting = piece.Velocity.LengthSquared() < 5;
    }

    static Vector3 Unit(Vector3 direction) => direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.UnitX;

    internal void Clear()
    {
        pending.Clear();
        pieces.Clear();
        previous = null;
        shownExit = false;
    }
}
