using System.Globalization;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ui;
using Meitou.Game.Host.Input;
using Meitou.Game.Rendering.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The Crafting tab's A/B comparison (README "What it does"): a right click on a line of the Items list marks it A (green), a second B (blue), a
/// marked line's right click unmarks it, a third replaces B; with both marked, the panel shows beside the list: B against A. A mark keeps to its
/// recipe (the row's craft key), not to the line, and shows as the original's tinted row fill. Armour, crossbows and prosthetics compare at
/// Masterwork, weapons at the line's model. The marks
/// are dropped when the overview closes. Harmony: a prefix on <c>PlayerInterface.MouseDown</c> takes the right press on a craft line; a postfix on
/// <c>ProductionWindows.AddCraftContent</c> marks the lines and places the panel. Host thread only.
/// </summary>
static class CraftCompare
{
    /// <summary>The original's marks: A (0.36, 0.78, 0.36), B (0.36, 0.62, 0.95); the row's fill at alpha 0.28 and a solid bar 4 pixels wide at its
    /// left, both of <c>gui/gfx/gearcompare_fill.png</c> tinted.</summary>
    static readonly Vector4 Green = new(0.36f, 0.78f, 0.36f, 1), Blue = new(0.36f, 0.62f, 0.95f, 1);

    const float FillAlpha = 0.28f, BarWidth = 4;

    static GearCompareAltMod? s_mod;
    static string? s_a, s_b;
    static (string?, string?, int)? s_key;
    static IReadOnlyList<Game.Ui.TooltipLine>? s_lines;
    static int s_failed;

    public static void Use(GearCompareAltMod mod) => s_mod = mod;

    /// <summary>Drops the marks (the overview closed).</summary>
    public static void Clear() => (s_a, s_b) = (null, null);

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool MouseDownPrefix(object __instance, MouseButton button, Vector2 p, ref bool __result)
    {
        try
        {
            if (button != MouseButton.Right || s_mod is not { Settings: { Enabled: true, CraftCompare: true } } mod) return true;
            if (GameUi.HitTest(__instance, p) is not ("overview", { } control) || !control.Name.StartsWith("craft-row-", StringComparison.Ordinal))
                return true;
            if (KeyOf(control.Action) is not { } key || GearReader.KindOf(mod.Data.Find(RecordOf(key))) is null) return true;

            Toggle(key);
            __result = true;
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
            return true;
        }
    }

    static void AddCraftContentPostfix(object __instance, List<UiControl> controls)
    {
        if (s_mod is not { Settings: { Enabled: true, CraftCompare: true } } mod || s_a is null && s_b is null) return;

        try
        {
            Mark(mod, __instance, controls);
            if (s_a is { } a && s_b is { } b && Lines(mod, __instance, a, b) is { } lines) Place(__instance, controls, lines);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    static void Toggle(string key)
    {
        if (key == s_a) s_a = null;
        else if (key == s_b) s_b = null;
        else if (s_a is null) s_a = key;
        else s_b = key;
    }

    /// <summary>The craft key of a row's action (<c>craft-queue:KEY</c> or <c>craft-focus:KEY</c>).</summary>
    static string? KeyOf(string action) => action.IndexOf(':') is var i and > 0 ? action[(i + 1)..] : null;

    /// <summary>The item record of a craft key (<c>item</c> or <c>item|model</c>).</summary>
    static string RecordOf(string key) => key.Split('|')[0];

    /// <summary>
    /// Marks the A and B lines: the original's picture under each, filled at <see cref="FillAlpha"/> with a solid bar at the left, in the line's
    /// colour; while the picture is not loaded (or the original is not installed) the line's text takes the colour instead.
    /// </summary>
    static void Mark(GearCompareAltMod mod, object production, List<UiControl> controls)
    {
        string? fill = mod.Original is { } folder ? Path.Combine(folder, "gui", "gfx", "gearcompare_fill.png") : null;
        var painter = GameUi.PlayerOf(production) is { } player ? GameUi.Painter(player) : null;
        bool image = fill is not null && File.Exists(fill) && painter?.IconReady(fill) == true;

        for (int i = controls.Count - 1; i >= 0; i--)
        {
            var c = controls[i];
            if (!c.Name.StartsWith("craft-row-", StringComparison.Ordinal) || KeyOf(c.Action) is not { } key) continue;

            Vector4? colour = key == s_a ? Green : key == s_b ? Blue : null;
            if (colour is not { } tint) continue;

            if (!image)
            {
                controls[i] = c with { TextColour = tint };
                continue;
            }

            string texture = UiPainter.IconTexture(fill!);
            var bar = c.Bounds with { Width = Math.Min(BarWidth, c.Bounds.Width) };
            controls.Insert(i, Fill(c.Name + "-gc-bar", bar, texture, tint, 1, c.Clip));
            controls.Insert(i, Fill(c.Name + "-gc-fill", c.Bounds, texture, tint, FillAlpha, c.Clip));
        }
    }

    static UiControl Fill(string name, UiRect at, string texture, Vector4 tint, float alpha, UiRect? clip)
    {
        string Channel(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var properties = new Dictionary<string, string>
        {
            ["ImageTexture"] = texture,
            ["Colour"] = $"{Channel(tint.X)} {Channel(tint.Y)} {Channel(tint.Z)} {Channel(alpha)}",
        };
        return new UiControl(name, "ImageBox", at, "", "", "", true, properties, Vector4.Zero, Pickable: false, Clip: clip);
    }

    static IReadOnlyList<Game.Ui.TooltipLine>? Lines(GearCompareAltMod mod, object production, string a, string b)
    {
        int settingsKey = HashCode.Combine(mod.Settings.ShowTotal, mod.Settings.TotalPerProperty, mod.Settings.TotalPerKg,
            mod.Settings.ColourBetter, mod.Settings.ColourWorse);
        if (s_key is { } last && last == (a, b, settingsKey)) return s_lines;

        s_key = (a, b, settingsKey);
        s_lines = null;
        if (GameUi.PlayerOf(production) is not { } player || GameUi.TooltipsOf(player) is not { } tooltips
            || GameUi.Combat(tooltips) is not { } combat)
            return null;

        if (Stats(mod.Data, combat, player, a) is not { } sa || Stats(mod.Data, combat, player, b) is not { } sb || sa.Kind != sb.Kind) return null;

        s_lines = PanelLines.Build(mod.Tr("B against A"), Comparison.Of(sa, sb, mod.Tr), mod.Settings, mod.Tr);
        return s_lines;
    }

    static void Place(object production, List<UiControl> controls, IReadOnlyList<Game.Ui.TooltipLine> lines)
    {
        if (GameUi.PlayerOf(production) is not { } player || GameUi.TooltipsOf(player) is not { } tooltips
            || GameUi.Painter(player) is not { } painter
            || controls.FirstOrDefault(c => c.Name == "QueueItemsPanel") is not { } list)
            return;

        GameUi.Place(tooltips, controls, painter, lines, list.Bounds, "gearcompare-craft");
    }

    /// <summary>A recipe's item: Masterwork for armour, crossbows and prosthetics; a weapon at its model's level.</summary>
    static GearStats? Stats(GameDatabase db, CombatDatabase combat, object player, string key)
    {
        var parts = key.Split('|');
        string record = parts[0], model = parts.Length > 1 ? parts[1] : "";
        int masterwork = ArmourTooltip.GradeQualities[^1];
        int level = model.Length > 0 ? ModelLevel(combat, model) : masterwork;
        float weight = Weight(db, combat, record, model, level);
        string name = db.Find(record)?.ShownName() ?? record;
        var view = new InventoryItemView(-1, record, name, "", 0, 0, 1, 1, 1, weight, level) { Level = level, Material = model };
        return GearReader.Read(db, combat, new GearItem(record, model, "", level, weight, name) { Price = GameUi.Price(player, GameUi.TipOf(view)) });
    }

    /// <summary>A model's level in the manufacturers' model lists (the first that names it); Masterwork's quality when none does.</summary>
    static int ModelLevel(CombatDatabase combat, string model)
    {
        foreach (var maker in combat.Manufacturers)
        {
            foreach (var entry in maker.Models)
            {
                if (entry.ModelStringId == model) return entry.Level;
            }
        }

        return ArmourTooltip.GradeQualities[^1];
    }

    static float Weight(GameDatabase db, CombatDatabase combat, string record, string model, int level)
    {
        if (combat.Armour(record) is { } armour) return armour.WeightAt(level) ?? armour.WeightKg;

        if (combat.Weapon(record) is { } weapon)
        {
            var material = combat.WeaponMaterial(model) ?? WeaponMaterial.None;
            return WeaponStats.Compute(level * 0.01f, weapon, WeaponManufacturer.Neutral, material, combat.Constants).Weight;
        }

        return db.Find(record)?.GetFloat("weight kg") ?? 0;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_mod?.LogError($"crafting comparison failed ({e.GetType().Name}: {e.Message})");
    }
}
