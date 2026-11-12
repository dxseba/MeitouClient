using Meitou.Game.Host;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Ui;
using Meitou.Data.Ui;
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
        const int MaximumLabels = 64;
        static readonly ConditionalWeakTable<World, Dictionary<string, IReadOnlyList<BrutalityPartLabel>>> labels = new();
        static StockPartLabels? installed;
#if BRUTALITY_STOCK_SAVE_PROBE
        static readonly bool labelProbe = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_LABEL_PROBE") == "1";
        static readonly HashSet<(CharacterId Owner, int Piece, string Part)> reported = [];
#endif
        readonly FieldInfo? session = AccessTools.Field(player, "Session");
        readonly PropertyInfo ground = StockHostParts.Property(player, "GroundItemPicks");
        readonly FieldInfo focus = StockHostParts.Field(StockHostParts.Property(player, "GroundItemPicks").PropertyType, "HighlightFocus");
        readonly FieldInfo? width = AccessTools.Field(player, "Width"), height = AccessTools.Field(player, "Height");
        readonly FieldInfo? projection = AccessTools.Field(player, "ViewProjection");
        readonly PropertyInfo editor = StockHostParts.Property(player, "CharacterEditor");
        readonly PropertyInfo editorOpen = StockHostParts.Property(StockHostParts.Property(player, "CharacterEditor").PropertyType, "EditorOpen");
        readonly PropertyInfo? title = AccessTools.Property(player, "TitleOpen");
        readonly PropertyInfo furniture = StockHostParts.Property(player, "Furniture");
        readonly PropertyInfo aboveFloor = StockHostParts.Property(StockHostParts.Property(player, "Furniture").PropertyType, "AboveViewedFloor");

        internal static void Install(ModContext context)
        {
            if (AccessTools.TypeByName("Meitou.Game.PlayerInterface") is not { } player
                || AccessTools.Method(player, "AddFrameControls", [typeof(List<UiControl>), typeof(bool)]) is not { } draw) return;
            var adapter = new StockPartLabels(player);
            if (adapter.session is null || adapter.projection is null) return;
            context.Harmony.Patch(draw, postfix: new HarmonyMethod(typeof(StockPartLabels), nameof(Draw)));
            StockNativeLimbLabels.Install(context, adapter.ground.PropertyType, adapter.session);
            installed = adapter;
        }

        internal static void Publish(World world, string source, IReadOnlyList<BrutalityPartLabel> parts) =>
            labels.GetOrCreateValue(world)[source] = parts;

        static void Draw(object __instance, List<UiControl> controls, bool hud) => installed?.Append(__instance, controls, hud);

        void Append(object player, List<UiControl> controls, bool hud)
        {
            if (!hud || !BrutalityUiAdapter.Preferences.ShowPartLabels || session?.GetValue(player) is not WorldSession shown
                || editorOpen.GetValue(editor.GetValue(player)) is true || title?.GetValue(player) is true
                || !labels.TryGetValue(shown.World, out var available)
                || projection?.GetValue(player) is not Matrix4x4 view || width?.GetValue(player) is not int w || height?.GetValue(player) is not int h
                || w <= 8 || h <= 8) return;
            var highlight = focus.GetValue(ground.GetValue(player)) as Vector3?;
#if BRUTALITY_STOCK_SAVE_PROBE
            if (labelProbe) highlight ??= shown.Camera.Current.Target;
#endif
            if (highlight is not { } point) return;
            var actors = shown.CurrentSnapshot.Characters;
            var floor = aboveFloor.GetValue(furniture.GetValue(player)) as Func<Vector3, bool>;
            float scale = Math.Clamp(h / 1080f, .6f, 1.5f), line = 24 * scale;
            var occupied = controls.Where(control => control.Name.StartsWith("itemlabel-", StringComparison.Ordinal)).Select(control => control.Bounds).ToList();
            var extras = new List<UiControl>();
            foreach (var label in available.Values.SelectMany(parts => parts)
                         .Where(label => Vector3.DistanceSquared(label.Position, point) < 500 * 500 && floor?.Invoke(label.Position) != true)
                         .OrderBy(label => Vector3.DistanceSquared(label.Position, point)))
            {
                if (extras.Count == MaximumLabels) break;
                var clip = Vector4.Transform(new Vector4(label.Position + Vector3.UnitY * .5f, 1), view);
                if (clip.W <= .1f || !float.IsFinite(clip.LengthSquared())) continue;
                var pixel = new Vector2((clip.X / clip.W * .5f + .5f) * w, (.5f - clip.Y / clip.W * .5f) * h);
                if (pixel.X < 0 || pixel.X > w || pixel.Y < 0 || pixel.Y > h) continue;
                string owner = actors.FirstOrDefault(actor => actor.Id == label.Owner)?.Name ?? "";
                string caption = owner.Length == 0 ? label.Part : $"{owner} ({label.Part})";
                float wide = Math.Min(w - 8, (caption.Length * 7.3f + 14) * scale);
                if (PlaceLabel(pixel, wide, line, w, h, occupied) is not { } box) continue;
                occupied.Add(box);
#if BRUTALITY_STOCK_SAVE_PROBE
                if (labelProbe && reported.Add((label.Owner, label.Piece, label.Part)))
                    Console.WriteLine($"Brutality stock highlight label {label.Part} owner={label.Owner.Slot}:{label.Owner.Generation} piece={label.Piece}");
#endif
                extras.Add(new($"brutality-label-{label.Owner.Slot}-{label.Owner.Generation}-{label.Piece}", "TextBox",
                    box, caption, "", "Kenshi_GenericTextBoxFlatSmallSkin", true,
                    new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "Center", ["TextColour"] = "0.9 0.8 0.77" },
                    new(.12f, .04f, .035f, .8f), Pickable: false, TextScale: scale));
            }
            controls.InsertRange(0, extras);
        }

        internal static UiRect? PlaceLabel(Vector2 pixel, float width, float height, int screenWidth, int screenHeight, List<UiRect> occupied)
        {
            const float margin = 4, gap = 2;
            float rightEdge = screenWidth - width - margin, bottomEdge = screenHeight - height - margin;
            if (bottomEdge < margin) return null;
            float x = Math.Clamp(pixel.X - width / 2, margin, rightEdge);
            float y = Math.Clamp(pixel.Y - height, margin, bottomEdge);
            float left = Math.Max(margin, x - width - gap), right = Math.Min(rightEdge, x + width + gap);
            float step = height + gap;

            if (FreeAt(x, y) is { } first) return first;
            // Keep labels near their source by trying the next rows and neighboring columns together.
            for (float offset = 0; y - offset >= margin || y + offset <= bottomEdge; offset += step)
            {
                var box = FreeAt(x, y - offset - step) ?? FreeAt(x, y + offset + step) ?? ToEitherSide(y - offset);
                if (offset > 0) box ??= ToEitherSide(y + offset);
                if (box is not null) return box;
            }
            return null;

            UiRect? ToEitherSide(float top) =>
                (left < x ? FreeAt(left, top) : null) ?? (right > x ? FreeAt(right, top) : null);

            UiRect? FreeAt(float left, float top)
            {
                if (top < margin || top > bottomEdge) return null;
                var candidate = new UiRect(left, top, width, height);
                foreach (var other in occupied)
                    if (candidate.Intersect(other) is { Width: > 0, Height: > 0 }) return null;
                return candidate;
            }
        }
    }
}
