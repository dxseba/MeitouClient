using Meitou.Game.Rendering.Objects;
using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

internal sealed record RobotScrapMesh(string Path, Vector3 Extent);

/// <summary>Installed mechanical item meshes, with the pin's 3–7-piece burst and bounded ordinary debris motion.</summary>
internal sealed class RobotScrapSimulation
{
#if BRUTALITY_STOCK_MASTER
    public const int PieceLimit = 96, HistoryLimit = 64;
#else
    public const int PieceLimit = 400, HistoryLimit = 64;
#endif
    sealed class Piece
    {
        public required RobotScrapMesh Mesh;
        public Vector3 Position, Velocity, Spin, Extent;
        public Quaternion Rotation;
        public float Scale, Floor;
        public double Born;
        public bool Resting;
    }
    readonly List<Piece> pieces = [];
    readonly HashSet<(CharacterId, int, int)> seen = [];
    readonly Queue<(CharacterId, int, int)> history = [];
    double clock;
    bool clockSet;
    public int Count => pieces.Count;
#if BRUTALITY_STOCK_MASTER
    public IEnumerable<DebrisPiece> Drawn => pieces.Select(p => new DebrisPiece(p.Mesh.Path, p.Scale, p.Rotation, p.Position));
#else
    static readonly Vector3 SteelTint = new(.27f, .31f, .35f);
    public IEnumerable<DebrisPiece> Drawn => pieces.Select(p => new DebrisPiece(p.Mesh.Path, p.Scale, p.Rotation, p.Position) { AlbedoTint = SteelTint });
#endif

    public bool Begin(in GoreEffectCut cut, Vector3 at, float floor, double now, ulong worldSeed, IReadOnlyList<RobotScrapMesh> choices)
    {
        if (!cut.Robot || choices.Count == 0 || !float.IsFinite(at.LengthSquared()) || !float.IsFinite(floor)) return false;
        var marker = (cut.Owner, cut.Part, cut.Kind);
        if (seen.Contains(marker)) return false;
        ulong random = RobotVisualRandom.ScrapSeed(cut, worldSeed);
        int count = 3 + (int)(RobotVisualRandom.Mix(random) % 5);
        var travel = cut.Travel.LengthSquared() > 1e-6f ? Vector2.Normalize(cut.Travel) : Vector2.UnitX;
        for (int i = 0; i < count; i++)
        {
            var mesh = choices[Math.Min((int)(Next(ref random) * choices.Count), choices.Count - 1)];
            float radius = .18f + Next(ref random) * .28f;
            float scale = radius / MathF.Max(mesh.Extent.X, MathF.Max(mesh.Extent.Y, mesh.Extent.Z));
            float angle = Next(ref random) * MathF.Tau;
            var aside = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var flat = aside * (4 + Next(ref random) * 9) + travel * (4 + Next(ref random) * 5);
            var axis = new Vector3(Next(ref random) - .5f, Next(ref random) - .5f, Next(ref random) - .5f);
            axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitY;
            pieces.Add(new Piece { Mesh = mesh, Scale = scale, Extent = mesh.Extent * scale, Floor = floor, Born = now,
                Position = at + new Vector3(aside.X * .25f, Next(ref random) * .3f, aside.Y * .25f), Velocity = new(flat.X, 8 + Next(ref random) * 11, flat.Y),
                Rotation = Quaternion.CreateFromYawPitchRoll(Next(ref random) * MathF.Tau, Next(ref random) * MathF.Tau, Next(ref random) * MathF.Tau),
                Spin = axis * (3 + Next(ref random) * 6) });
        }
        if (pieces.Count > PieceLimit) pieces.RemoveRange(0, pieces.Count - PieceLimit);
        if (history.Count >= HistoryLimit) seen.Remove(history.Dequeue()); seen.Add(marker); history.Enqueue(marker);
        return true;
    }

    public void Advance(double now, double lifetime)
    {
        if (!double.IsFinite(now)) return;
        float dt = clockSet ? (float)Math.Clamp(now - clock, 0, .5) : 0;
        clock = now; clockSet = true;
        pieces.RemoveAll(p => now - p.Born >= lifetime);
        int steps = Math.Clamp((int)MathF.Ceiling(dt * 60), 1, 8);
        float h = dt / steps;
        foreach (var p in pieces)
            for (int i = 0; i < steps && h > 0 && !p.Resting; i++)
            {
                p.Velocity.Y -= 98 * h; p.Position += p.Velocity * h;
                float spin = p.Spin.Length();
                if (spin > 0) p.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.Spin / spin, spin * h) * p.Rotation);
                float thin = MathF.Min(p.Extent.X, MathF.Min(p.Extent.Y, p.Extent.Z));
                if (p.Position.Y - thin > p.Floor) continue;
                p.Position.Y = p.Floor + thin;
                if (MathF.Abs(p.Velocity.Y) < 15 && new Vector2(p.Velocity.X, p.Velocity.Z).Length() < 12)
                {
                    p.Resting = true; p.Velocity = p.Spin = Vector3.Zero;
                    var axis = p.Extent.X <= p.Extent.Y && p.Extent.X <= p.Extent.Z ? Vector3.UnitX : p.Extent.Y <= p.Extent.Z ? Vector3.UnitY : Vector3.UnitZ;
                    var up = Vector3.Transform(axis, p.Rotation); if (up.Y < 0) up = -up;
                    var tilt = Vector3.Cross(up, Vector3.UnitY); float sine = tilt.Length();
                    if (sine >= 1e-5f) p.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(tilt / sine, MathF.Atan2(sine, up.Y)) * p.Rotation);
                }
                else { p.Velocity = new(p.Velocity.X * .6f, MathF.Abs(p.Velocity.Y) * .35f, p.Velocity.Z * .6f); p.Spin *= .6f; }
            }
    }
    static float Next(ref ulong value) => RobotVisualRandom.Next(ref value);
    public void Clear() { pieces.Clear(); seen.Clear(); history.Clear(); clockSet = false; }
}
