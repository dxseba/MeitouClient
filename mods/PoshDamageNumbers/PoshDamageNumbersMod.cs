using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// Port of Posh Damage Numbers (workshop 3821781631), rebuilt from its description: floating numbers of the damage each landed hit dealt after
/// armour, fast hits on one target added into one growing number, KNOCKOUT and KILLED when they happen; warm white to orange by size, red for
/// your own. Fed by the display-only hit feed (<c>AddCombatHitFeed</c>), drawn as Level Up Notify's floating text.
/// </summary>
public sealed class PoshDamageNumbersMod : IMod
{
    internal static readonly DamageSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddCombatHitFeed(Numbers.Feed);
        Numbers.Install(context);
        context.AddUi(new NumbersUi());
        context.Log("Posh Damage Numbers initialized");
    }
}

public sealed class DamageSettings
{
    [Setting("Enable damage numbers", Hint = "Floating damage numbers over characters hit. Turn the game's own damage floaters off to see one set.")]
    public bool Enabled { get; set; } = true;

    [Setting("Merge window (seconds)", Hint = "Hits on one target within this time add into one number.", Min = 0, Max = 3, Step = 0.1)]
    public float MergeSeconds { get; set; } = 0.6f;

    [Setting("Seconds", Hint = "How long a number floats.", Min = 0.5, Max = 5, Step = 0.1)]
    public float Seconds { get; set; } = 1.4f;

    [Setting("Show knockouts and kills", Hint = "KNOCKOUT and KILLED pop up after the number.")]
    public bool Words { get; set; } = true;
}

/// <summary>One floating number: the victim, its total, the tick it started and was last added to, its state before the first hit.</summary>
sealed class Number(CharacterId victim, float damage, long tick, bool wasAlive, bool wasDown)
{
    public CharacterId Victim { get; } = victim;
    public float Damage { get; set; } = damage;
    public long Start { get; } = tick;
    public long Last { get; set; } = tick;
    public bool WasAlive { get; } = wasAlive;
    public bool WasDown { get; } = wasDown;
}

/// <summary>The numbers: added from the hit feed and projected each frame by a postfix on <c>PlayerInterface.BindUi</c>.</summary>
static class Numbers
{
    public const string Key = "posh.dmg.";
    public const int Slots = 16;
    const float Head = 20, Rise = 9;

    static readonly Lock Gate = new();
    static readonly List<Number> Shown = [];
    static FieldInfo? s_sessionField;
    static MethodInfo? s_project;

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(WorldSession));
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Numbers), nameof(Bound)));
    }

    /// <summary>The hit feed (host thread, once a frame): each hit adds to its victim's open number or starts one.</summary>
    public static void Feed(IReadOnlyList<CombatHit> hits)
    {
        if (!PoshDamageNumbersMod.Settings.Enabled) return;

        lock (Gate)
        {
            foreach (var hit in hits) Add(hit);
        }
    }

    static void Add(CombatHit hit)
    {
        float damage = (hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce) * hit.Damage.Scale;
        if (damage <= 0) return;

        long merge = (long)(PoshDamageNumbersMod.Settings.MergeSeconds * 30);
        var victim = hit.Victim;
        if (Shown.FindLast(n => n.Victim == victim && hit.Tick - n.Last <= merge) is { } open)
        {
            open.Damage += damage;
            open.Last = hit.Tick;
            return;
        }

        Shown.Add(new(victim, damage, hit.Tick, hit.VictimWasAlive, hit.VictimWasIncapacitated));
        if (Shown.Count > 64) Shown.RemoveAt(0);
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (!PoshDamageNumbersMod.Settings.Enabled || s_sessionField?.GetValue(__instance) is not WorldSession session) return;

        var world = session.World;
        float life = PoshDamageNumbersMod.Settings.Seconds / world.TickSeconds;
        var characters = session.CurrentSnapshot.Characters.ToDictionary(c => c.Id);
        int slot = 0;
        lock (Gate)
        {
            Shown.RemoveAll(n => (world.Tick - n.Last) / life > 1 || world.Tick < n.Start);
            for (int i = Shown.Count - 1; i >= 0 && slot < Slots; i--)
            {
                var n = Shown[i];
                if (!characters.TryGetValue(n.Victim, out var c)) continue;

                float age = Math.Clamp((world.Tick - n.Last) / life, 0, 1);
                if (s_project!.Invoke(__instance, [c.Position + new Vector3(0, Head + Rise * age, 0)]) is not Vector2 at) continue;

                __result.Text[Key + slot++] = string.Create(CultureInfo.InvariantCulture, $"{at.X:0}|{at.Y:0}|{Tone(n, c)}|{Text(n, c)}");
            }
        }
    }

    /// <summary>0 warm white, 1 gold, 2 orange (by the total), 3 red for the player's own.</summary>
    static int Tone(Number n, CharacterSnapshot c) => c.IsPlayer ? 3 : n.Damage < 15 ? 0 : n.Damage < 40 ? 1 : 2;

    static string Text(Number n, CharacterSnapshot c)
    {
        string number = ((int)MathF.Round(n.Damage)).ToString(CultureInfo.InvariantCulture);
        if (!PoshDamageNumbersMod.Settings.Words || c.Body is not { } body) return number;
        if (n.WasAlive && body.Dead) return number + "  KILLED";
        if (!n.WasDown && body.Unconscious && !body.Dead) return number + "  KNOCKOUT";
        return number;
    }
}

/// <summary>Draws the bound numbers centred on their points.</summary>
public sealed class NumbersUi : IUiExtension
{
    const float Width = 200, Height = 24;

    public string Id => "dxseba.poshdamagenumbers.ui";

    public void Register(UiRegistration registration)
    {
        string[] tones =
        [
            registration.RegisterStyle("white", new() { Text = new(1f, .95f, .85f, 1), TextPadding = new(0, 0, 0, 0) }),
            registration.RegisterStyle("gold", new() { Text = new(1f, .82f, .30f, 1), TextPadding = new(0, 0, 0, 0) }),
            registration.RegisterStyle("orange", new() { Text = new(1f, .55f, .15f, 1), TextPadding = new(0, 0, 0, 0) }),
            registration.RegisterStyle("red", new() { Text = new(.95f, .20f, .15f, 1), TextPadding = new(0, 0, 0, 0) }),
        ];
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, tones), order: 40);
    }

    static UiNode Build(UiFrameContext frame, string[] tones)
    {
        var root = new UiNode("posh-dmg") { Placement = UiPlacement.Fill, Pickable = false };
        for (int i = 0; i < Numbers.Slots; i++)
        {
            var parts = frame.Text(Numbers.Key + i).Split('|', 4);
            if (parts.Length != 4 || !float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y)) continue;
            if (!int.TryParse(parts[2], CultureInfo.InvariantCulture, out int tone)) continue;

            var placement = new UiPlacement(Width, Height, Left: x - Width / 2, Top: y - Height / 2);
            root.Children.Add(new("posh-dmg-" + i, UiNodeKind.Label)
            {
                Placement = placement, Text = parts[3], Style = tones[Math.Clamp(tone, 0, 3)], Pickable = false,
            });
        }

        return root;
    }
}
