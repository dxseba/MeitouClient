using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Ui;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality.Ui
{
    public readonly record struct BrutalityPartLabel(CharacterId Owner, int Piece, string Part, Vector3 Position);
}

namespace Meitou.Brutality
{
    /// <summary>Uses the stock ground-item highlight key and native controls to label visible detached anatomy.</summary>
    internal sealed class StockPartLabels(Type player)
    {
        static readonly ConditionalWeakTable<World, Dictionary<string, IReadOnlyList<BrutalityPartLabel>>> labels = new();
        static StockPartLabels? installed;
#if BRUTALITY_STOCK_SAVE_PROBE
        static readonly bool labelProbe = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_LABEL_PROBE") == "1";
        static readonly HashSet<string> reported = new(StringComparer.Ordinal);
#endif
        readonly FieldInfo? session = AccessTools.GetDeclaredFields(player).SingleOrDefault(field => field.FieldType == typeof(WorldSession));
        readonly FieldInfo? focus = AccessTools.Field(player, "HighlightFocus");
        readonly FieldInfo? width = AccessTools.Field(player, "Width"), height = AccessTools.Field(player, "Height");
        readonly FieldInfo? projection = AccessTools.Field(player, "ViewProjection");
        readonly PropertyInfo? editor = AccessTools.Property(player, "EditorOpen");
        readonly PropertyInfo? title = AccessTools.Property(player, "TitleOpen");
        readonly PropertyInfo? aboveFloor = AccessTools.Property(player, "AboveViewedFloor");

        internal static void Install(ModContext context)
        {
            if (AccessTools.TypeByName("Meitou.Game.PlayerInterface") is not { } player
                || AccessTools.Method(player, "AddFrameControls", [typeof(List<UiControl>), typeof(bool), typeof(int)]) is not { } draw) return;
            var adapter = new StockPartLabels(player);
            if (adapter.session is null || adapter.focus is null || adapter.projection is null) return;
            context.Harmony.Patch(draw, postfix: new HarmonyMethod(typeof(StockPartLabels), nameof(Draw)));
            StockNativeLimbLabels.Install(context, player, adapter.session);
            installed = adapter;
        }

        internal static void Publish(World world, string source, IReadOnlyList<BrutalityPartLabel> parts) =>
            labels.GetOrCreateValue(world)[source] = parts;

        static void Draw(object __instance, List<UiControl> controls, bool hud) => installed?.Append(__instance, controls, hud);

        void Append(object player, List<UiControl> controls, bool hud)
        {
            if (!hud || !BrutalityUiAdapter.Preferences.ShowPartLabels || session?.GetValue(player) is not WorldSession shown
                || editor?.GetValue(player) is true || title?.GetValue(player) is true
                || !labels.TryGetValue(shown.World, out var available)
                || projection?.GetValue(player) is not Matrix4x4 view || width?.GetValue(player) is not int w || height?.GetValue(player) is not int h
                || w <= 8 || h <= 8) return;
            var highlight = focus?.GetValue(player) as Vector3?;
#if BRUTALITY_STOCK_SAVE_PROBE
            if (labelProbe) highlight ??= shown.Camera.Current.Target;
#endif
            if (highlight is not { } point) return;
            var actors = shown.CurrentSnapshot.Characters;
            var floor = aboveFloor?.GetValue(player) as Func<Vector3, bool>;
            float scale = Math.Clamp(h / 1080f, .6f, 1.5f), line = 24 * scale;
            var occupied = controls.Where(control => control.Name.StartsWith("itemlabel-", StringComparison.Ordinal)).Select(control => control.Bounds).ToList();
            var extras = new List<UiControl>();
            foreach (var label in available.Values.SelectMany(parts => parts)
                         .Where(label => Vector3.DistanceSquared(label.Position, point) < 500 * 500 && floor?.Invoke(label.Position) != true)
                         .OrderBy(label => Vector3.DistanceSquared(label.Position, point)).Take(64))
            {
                var clip = Vector4.Transform(new Vector4(label.Position + Vector3.UnitY * .5f, 1), view);
                if (clip.W <= .1f || !float.IsFinite(clip.LengthSquared())) continue;
                var pixel = new Vector2((clip.X / clip.W * .5f + .5f) * w, (.5f - clip.Y / clip.W * .5f) * h);
                if (pixel.X < 0 || pixel.X > w || pixel.Y < 0 || pixel.Y > h) continue;
                string owner = actors.FirstOrDefault(actor => actor.Id == label.Owner)?.Name ?? "";
                string caption = owner.Length == 0 ? label.Part : $"{owner} ({label.Part})";
                float wide = Math.Min(w - 8, (caption.Length * 7.3f + 14) * scale);
                var box = new UiRect(Math.Clamp(pixel.X - wide / 2, 4, Math.Max(4, w - wide - 4)), pixel.Y - line, wide, line);
                while (box.Y >= 0 && occupied.Any(other => box.Intersect(other) is { Width: > 0, Height: > 0 }))
                    box = box with { Y = box.Y - line - 1 };
                if (box.Y < 0) continue;
                occupied.Add(box);
#if BRUTALITY_STOCK_SAVE_PROBE
                if (labelProbe && reported.Add(label.Part)) Console.WriteLine($"Brutality stock highlight label {label.Part}");
#endif
                extras.Add(new($"brutality-label-{label.Owner.Slot}-{label.Owner.Generation}-{label.Piece}", "TextBox",
                    box, caption, "", "Kenshi_GenericTextBoxFlatSmallSkin", true,
                    new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "Center", ["TextColour"] = "0.9 0.8 0.77" },
                    new(.12f, .04f, .035f, .8f), Pickable: false, TextScale: scale));
            }
            controls.InsertRange(0, extras);
        }
    }
}
