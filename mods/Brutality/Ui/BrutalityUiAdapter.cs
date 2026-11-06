using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality.Ui;

public readonly record struct BrutalityPartLabel(CharacterId Owner, int Piece, string Part, Vector3 Position);
/// <summary>Optional mod-system source of immutable, tick-matched organ labels.</summary>
public interface IBrutalityPartLabels { IReadOnlyList<BrutalityPartLabel> LabelsAt(long tick); }

/// <summary>Adapts the pinned private UI seam; simulation state is read only through published presentation lists.</summary>
public static partial class BrutalityUiAdapter
{
    static FieldInfo? sessionField, projectionField, widthField, heightField, eyeField, focusField, statField;
    static MethodInfo? leadMethod, selectedMethod, hiddenMethod, rayMethod;
    static PropertyInfo? editorProperty, titleProperty, hiddenProperty, floorProperty, scaleProperty;
    public static BrutalityUiPreferences Preferences { get; private set; } = new();
    const int MaximumLabels = 64;

    public static BrutalityUiPreferences Install(ModContext context)
    {
        Preferences = new(context.Folder);
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        if (type is null) return Preferences;
        sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(type.FullName, "WorldSession");
        FieldInfo Field(string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        MethodInfo Method(string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        projectionField = Field("ViewProjection"); widthField = Field("Width"); heightField = Field("Height"); eyeField = Field("Eye");
        focusField = Field("HighlightFocus"); statField = Field("selectedStat");
        leadMethod = Method("Lead"); selectedMethod = Method("Selected"); hiddenMethod = Method("Hidden"); rayMethod = Method("Ray");
        editorProperty = AccessTools.Property(type, "EditorOpen"); titleProperty = AccessTools.Property(type, "TitleOpen");
        hiddenProperty = AccessTools.Property(type, "HiddenInWorld"); floorProperty = AccessTools.Property(type, "AboveViewedFloor");
        scaleProperty = AccessTools.Property(type, "InventoryScale");
        context.Harmony.Patch(Method("BindUi"), postfix: new HarmonyMethod(typeof(BrutalityUiAdapter), nameof(Bound)));
        context.Harmony.Patch(Method("AddFrameControls"), postfix: new HarmonyMethod(typeof(BrutalityUiAdapter), nameof(FrameControls)));
        context.Harmony.Patch(Method("AddStatsContent"), postfix: new HarmonyMethod(typeof(BrutalityUiAdapter), nameof(StatsContent)));
        context.Harmony.Patch(Method("AddStatDescription"), prefix: new HarmonyMethod(typeof(BrutalityUiAdapter), nameof(StatDescription)));
        InstallTargetMenu(context, type);
        return Preferences;
    }
    static WorldSession? Session(object ui) => sessionField?.GetValue(ui) as WorldSession;
    static CharacterSnapshot? Lead(object ui) => leadMethod?.Invoke(ui, null) as CharacterSnapshot;
    static CharacterSnapshot? SelectedLead(object ui) => (selectedMethod?.Invoke(ui, null) as IReadOnlyList<CharacterSnapshot>)?.FirstOrDefault();
    static BrutalityActorView? Actor(WorldSession session, CharacterId id) => BrutalityRuntime.For(session.World)?.ActorViews.FirstOrDefault(actor => actor.Character == id);
    static void Bound(object __instance, UiBindings __result)
    {
        if (Session(__instance) is not { } session || titleProperty?.GetValue(__instance) is true || editorProperty?.GetValue(__instance) is true) return;
        __result.Text["brutality.available"] = "1";
        if (Lead(__instance) is not { } lead) return;
        var actor = Actor(session, lead.Id);
        float level = actor?.Level ?? 0, progress = actor?.Progress ?? 0;
        __result.Text["brutality.caption"] = $"Brutality  {MathF.Floor(level).ToString("0", CultureInfo.InvariantCulture)}";
        __result.Text["brutality.progress"] = level >= 100 ? "Mastered" : $"Next level: {(progress * 100).ToString("0", CultureInfo.InvariantCulture)}%";
        __result.Text["brutality.chance"] = "Cleave chance on eligible hits: " + BrutalityPolicy.BisectionChance(level).ToString("P0", CultureInfo.InvariantCulture);
    }
    static Vector2? Project(Vector3 position, Matrix4x4 projection, int width, int height)
    {
        var clip = Vector4.Transform(new Vector4(position, 1), projection);
        if (!float.IsFinite(clip.LengthSquared()) || clip.W <= .1f) return null;
        var at = new Vector2((clip.X / clip.W * .5f + .5f) * width, (.5f - clip.Y / clip.W * .5f) * height);
        return at.X >= 0 && at.X <= width && at.Y >= 0 && at.Y <= height ? at : null;
    }
    static void FrameControls(object __instance, List<UiControl> controls, bool hud)
    {
        if (!hud || !Preferences.ShowPartLabels || focusField?.GetValue(__instance) is not Vector3 focus || Session(__instance) is not { } session
            || editorProperty?.GetValue(__instance) is true || titleProperty?.GetValue(__instance) is true
            || projectionField?.GetValue(__instance) is not Matrix4x4 projection || widthField?.GetValue(__instance) is not int width
            || heightField?.GetValue(__instance) is not int height || width <= 0 || height <= 0) return;
        var snapshot = session.CurrentSnapshot;
        var physics = session.World.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault();
        var frame = physics?.FrameAt(snapshot.Tick);
        var before = physics?.FrameAt(session.PreviousSnapshot.Tick);
        var characters = snapshot.Characters.ToDictionary(character => character.Id);
        var parts = new List<BrutalityPartLabel>();
        if (frame is not null)
            foreach (var character in frame.Characters.Values)
            {
                if (!characters.TryGetValue(character.Character, out var owner)) continue;
                var oldCharacter = before?.Characters.GetValueOrDefault(owner.Id);
                RenderBridge.TryAcceptedGeometry(session.World, character, out var drawn);
                if (character.State.Torso is { } torso)
                    AddPiece(0, TorsoName(torso, positive: true), owner.Position, character.RetainedDefinition, character.Retained,
                        oldCharacter?.RetainedDefinition, oldCharacter?.Retained);
                else if (owner.Body is { Dead: true }) parts.Add(new(owner.Id, 0, "Body", owner.Position));
                foreach (var piece in character.Detached)
                {
                    string name = piece.StableKey switch
                    {
                        1 when character.State.Torso is { } cut => TorsoName(cut, positive: false),
                        2 when character.State.Head is { Mode: HeadCutMode.NeckDetach } => "Head",
                        2 => "Head fragment 1", 3 => "Head fragment 2",
                        10 => "Left arm", 11 => "Right arm", 12 => "Left leg", 13 => "Right leg", _ => "Body fragment",
                    };
                    var previous = oldCharacter?.Detached.FirstOrDefault(p => p.StableKey == piece.StableKey);
                    var position = previous is null ? piece.Position : Vector3.Lerp(previous.Position, piece.Position, session.SimulationAlpha);
                    if (piece.StableKey is 1 or >= 10 and <= 13)
                    {
                        if (drawn?.Detached.Any(fragment => fragment.StableKey == piece.StableKey) != true) continue;
                        AddPiece(piece.StableKey, name, position, piece.Definition, piece.Bones, previous?.Definition, previous?.Bones);
                    }
                    else parts.Add(new(owner.Id, piece.StableKey, name, position));
                }

                void AddPiece(int key, string name, Vector3 position, RagdollDefinition? definition, BonePose? pose,
                    RagdollDefinition? oldDefinition, BonePose? oldPose)
                {
                    bool rootNamed = false;
                    if (drawn is not null && definition is not null && pose is { Components: { } components })
                    {
                        foreach (var limb in FragmentLimbLabels.For(definition, components))
                        {
                            if (limb.Position(pose, character.Placement) is not { } at) continue;
                            if (oldCharacter is not null && ReferenceEquals(definition, oldDefinition) && oldPose is not null
                                && limb.Position(oldPose, oldCharacter.Placement) is { } previous)
                                at = Vector3.Lerp(previous, at, session.SimulationAlpha);
                            // A torso half can contain disconnected limbs. Its root retains the original label key.
                            int labelKey = limb.Root ? key : 200 + limb.Bone * 16 + key;
                            parts.Add(new(owner.Id, labelKey, limb.Name, at));
                            rootNamed |= limb.Root;
                        }
                    }
                    if (!rootNamed) parts.Add(new(owner.Id, key, name, position));
                }
            }
        foreach (var source in session.World.Systems.OfType<IBrutalityPartLabels>()) parts.AddRange(source.LabelsAt(snapshot.Tick));
        var hidden = hiddenProperty?.GetValue(__instance) as Func<CharacterId, bool>;
        var aboveFloor = floorProperty?.GetValue(__instance) as Func<Vector3, bool>;
        Vector3 eye = eyeField?.GetValue(__instance) is Vector3 camera ? camera : focus;
        var visible = parts.Where(p => float.IsFinite(p.Position.LengthSquared()) && Vector3.DistanceSquared(focus, p.Position) <= 500 * 500
            && hidden?.Invoke(p.Owner) != true && aboveFloor?.Invoke(p.Position) != true)
            .OrderBy(p => Vector3.DistanceSquared(eye, p.Position)).Take(MaximumLabels);
        float scale = Math.Clamp(height / 1080f, .6f, 1.5f), labelHeight = 24 * scale;
        var occupied = controls.Where(c => c.Name.StartsWith("itemlabel-", StringComparison.Ordinal)).Select(c => c.Bounds).ToList();
        var labels = new List<UiControl>();
        foreach (var label in visible)
        {
            if (Project(label.Position + new Vector3(0, .5f, 0), projection, width, height) is not { } at) continue;
            if (rayMethod?.Invoke(__instance, [at]) is ValueTuple<Vector3, Vector3> ray
                && hiddenMethod?.Invoke(__instance, [ray, Vector3.Distance(ray.Item1, label.Position)]) is true) continue;
            string owner = characters.GetValueOrDefault(label.Owner)?.Name ?? "Unknown";
            string caption = owner + " — " + label.Part;
            float labelWidth = Math.Min(width - 8, (caption.Length * 7.3f + 14) * scale);
            var rect = new UiRect(Math.Clamp(at.X - labelWidth / 2, 4, MathF.Max(4, width - labelWidth - 4)), at.Y - labelHeight - 5 * scale, labelWidth, labelHeight);
            for (int step = 0; step < MaximumLabels && occupied.Any(other => rect.Intersect(other) is { Width: > 0, Height: > 0 }); step++) rect = rect with { Y = rect.Y - labelHeight - 1 };
            if (rect.Y < 0 || rect.Y + rect.Height > height) continue;
            occupied.Add(rect);
            labels.Add(new($"brutality-label-{label.Owner.Slot}-{label.Owner.Generation}-{label.Piece}", "TextBox", rect, caption, "", "Kenshi_GenericTextBoxFlatSmallSkin", true,
                new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "Center", ["TextColour"] = "0.9 0.8 0.77" },
                new(.12f, .04f, .035f, .8f), Pickable: false, TextScale: scale));
        }
        controls.InsertRange(0, labels);
    }
    static string TorsoName(TorsoBisectionState cut, bool positive)
    {
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float side = positive ? 1 : -1;
        if (!cut.FullBody || MathF.Abs(normal.Y) >= .75f && MathF.Abs(normal.Y) > MathF.Abs(normal.X) * 1.5f && MathF.Abs(normal.Y) > MathF.Abs(normal.Z) * 1.5f)
            return normal.Y * side > 0 ? "Upper body" : "Lower body";
        if (MathF.Abs(normal.Y) < .35f && MathF.Abs(normal.Z) >= .8f) return normal.Z * side > 0 ? "Front body half" : "Back body half";
        return positive ? "Body half 1" : "Body half 2";
    }
}
