using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;

namespace Meitou.Transmog;

/// <summary>
/// The transmogrified look of a character: its own look's loadout with each set slot's worn item swapped for the first appearance chosen there
/// (or emptied, when every slot is taken over), the further appearances of a slot laid over it from looks built with that item alone, then the
/// dye colours and model adjustments of each appearance. Built from the look the engine made, never written back to the character: what it
/// wears, its stats and its trades stay its real items.
/// </summary>
sealed class LookBuilder(GameDatabase db, PopulationData population, TransmogSettings settings)
{
    /// <summary>The skinned parts' adjustments by look (the renderer applies them in the character's frame; a rigid part takes its own).</summary>
    public static readonly ConditionalWeakTable<CharacterAppearance, Dictionary<CharacterPart, Matrix4x4>> Skinned = new();

    /// <summary>The adjusted skinned head pieces by look, drawn rigidly on the head bone with the adjustment in its frame.</summary>
    public static readonly ConditionalWeakTable<CharacterAppearance, Dictionary<CharacterPart, Matrix4x4>> OnHead = new();

    static readonly ConcurrentDictionary<(int, int), GameRecord> Dyes = new();

    static readonly Func<string, FcsRecordType, string, string, GameRecord>? MakeRecord = RecordMaker();

    static readonly Func<GameRecord, Dictionary<string, int>>? IntsOf =
        AccessTools.PropertyGetter(typeof(GameRecord), "IntTable") is { } getter
            ? AccessTools.MethodDelegate<Func<GameRecord, Dictionary<string, int>>>(getter)
            : null;

    static readonly Action<CharacterAppearance, int>? SetHiddenParts =
        AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.HiddenParts)) is { } setter
            ? AccessTools.MethodDelegate<Action<CharacterAppearance, int>>(setter)
            : null;

    /// <summary>The look <paramref name="outfit"/> gives <paramref name="cold"/> over its own <paramref name="own"/>; null to keep its own.</summary>
    public CharacterAppearance? Build(CharacterCold cold, CharacterAppearance own, Outfit outfit)
    {
        if (own.Loadout is not { } worn || own.Animal is not null || population.Appearances is not { } source) return null;

        var kit = SavedKit.Copy(worn);
        var shown = new List<(Layer Layer, GameRecord Record)>();
        var over = new List<(Layer Layer, GameRecord Record)>();
        if (outfit.On)
        {
            foreach (string slot in Slots.Clothing) SwapClothing(kit, outfit, slot, shown, over);
            SwapBackpack(kit, outfit, shown, over);
            for (int k = 0; k < 4; k++) SwapLimb(kit, outfit, k, shown);
        }

        if (settings.IndependentWeapons ? outfit.WeaponsOn : outfit.On) SwapWeapons(kit, outfit, shown);

        if (source.Create(cold.RecordId, kit, cold.Shaved) is not { } look) return null;

        foreach (var (layer, record) in over) LayOver(source, cold, worn, look, record);
        foreach (var (layer, record) in shown.Concat(over)) Adjust(look, layer, record);
        return look;
    }

    #region Slots

    /// <summary>The records of a slot's appearances that the data has, of the type the slot takes.</summary>
    List<(Layer Layer, GameRecord Record)> Resolve(Outfit outfit, string slot, Func<GameRecord, bool> fits)
    {
        var found = new List<(Layer, GameRecord)>();
        foreach (var layer in outfit.Of(slot))
        {
            if (layer.Item.Length > 0 && db.Find(layer.Item) is { } record && fits(record)) found.Add((layer, record));
        }

        return found;
    }

    void SwapClothing(Loadout kit, Outfit outfit, string slot, List<(Layer, GameRecord)> shown, List<(Layer, GameRecord)> over)
    {
        var layers = Resolve(outfit, slot, r => r.Type == FcsRecordType.Armour && SectionOf(r.GetInt("slot", 5)) == slot);
        if (layers.Count == 0 && settings.SelectedSlotsOnly) return;

        kit.Clothing.RemoveAll(c => (c.Section ?? SectionOf(c.Record.GetInt("slot", 5))) == slot);
        if (slot == "belt") kit.Carried.Clear();
        if (layers.Count == 0) return;

        kit.Clothing.Add(new LoadoutItem(layers[0].Record));
        shown.Add(layers[0]);
        over.AddRange(layers.Skip(1));
    }

    void SwapBackpack(Loadout kit, Outfit outfit, List<(Layer, GameRecord)> shown, List<(Layer, GameRecord)> over)
    {
        var layers = Resolve(outfit, Slots.Backpack, r => r.Type == FcsRecordType.Container);
        if (layers.Count == 0 && settings.SelectedSlotsOnly) return;

        kit.Backpack = layers.Count > 0 ? new LoadoutItem(layers[0].Record) : null;
        if (layers.Count == 0) return;

        shown.Add(layers[0]);
        over.AddRange(layers.Skip(1));
    }

    /// <summary>A limb slot's first appearance: a prosthetic of that limb, or the natural limb (an appearance with no item).</summary>
    void SwapLimb(Loadout kit, Outfit outfit, int limb, List<(Layer, GameRecord)> shown)
    {
        if (outfit.Of("limb" + (char)('0' + limb)) is not [var layer, ..]) return;

        if (layer.Item.Length == 0)
        {
            kit.Limbs[limb] = null;
            return;
        }

        if (db.Find(layer.Item) is not { Type: FcsRecordType.LimbReplacement } record || !FitsLimb(record, limb)) return;

        kit.Limbs[limb] = new LoadoutLimb(2, record);
        shown.Add((layer, record));
    }

    /// <summary>
    /// The weapon slots: WEAPON I and II dress the character's first and second carried weapon (hip first, then back, then the crossbow), each
    /// kept at its place (section and row); "Enable rendering" off hides it; the look takes the real weapon's manufacturer and model with "Use
    /// original weapon material", else the chosen manufacturer's first model (none: the item's plain look). A slot with no real weapon there
    /// shows nothing.
    /// </summary>
    void SwapWeapons(Loadout kit, Outfit outfit, List<(Layer, GameRecord)> shown)
    {
        var held = kit.Weapons.Concat(kit.Crossbow is { } bow ? [bow] : Array.Empty<LoadoutWeapon>()).ToList();
        var dressed = new List<LoadoutWeapon?>(held);
        for (int k = 0; k < 2 && k < held.Count; k++)
        {
            if (outfit.Of("weapon" + (char)('0' + k)) is not [var layer, ..]) continue;

            dressed[k] = layer.Rendered ? Dress(held[k], layer, shown) : null;
        }

        kit.Weapons.Clear();
        kit.Crossbow = null;
        foreach (var weapon in dressed.OfType<LoadoutWeapon>())
        {
            if (weapon.Weapon.Type == FcsRecordType.Crossbow && kit.Crossbow is null) kit.Crossbow = weapon;
            else if (weapon.Weapon.Type != FcsRecordType.Crossbow) kit.Weapons.Add(weapon);
        }
    }

    LoadoutWeapon Dress(LoadoutWeapon real, Layer layer, List<(Layer, GameRecord)> shown)
    {
        if (db.Find(layer.Item) is not { Type: FcsRecordType.Weapon or FcsRecordType.Crossbow } record) return real;

        var (maker, model, level) = layer.OriginalFinish ? (real.Manufacturer, real.Model, real.Level) : FinishOf(layer.Finish);
        shown.Add((layer, record));
        return new LoadoutWeapon(record) { Section = real.Section, Row = real.Row, Manufacturer = maker, Model = model, Level = level };
    }

    /// <summary>A WEAPON_MANUFACTURER and its first <c>weapon models</c> entry (MATERIAL_SPECS_WEAPON and its level).</summary>
    (GameRecord?, GameRecord?, int) FinishOf(string manufacturer)
    {
        if (manufacturer.Length == 0 || db.Find(manufacturer) is not { } maker) return (null, null, 0);

        var first = maker.GetReferences("weapon models").FirstOrDefault();
        return (maker, first.TargetStringId is { } id ? db.Find(id) : null, first.Values.Value0);
    }

    /// <summary>Whether a LIMB_REPLACEMENT goes on limb slot <paramref name="limb"/> (its <c>slot</c> 50 to 53).</summary>
    public static bool FitsLimb(GameRecord record, int limb) =>
        record.GetInt("slot") - Data.Gameplay.Bodies.LimbReplacement.FirstSlot == limb;

    /// <summary>The worn section of an ARMOUR <c>slot</c> (the game's section types); null for one no listed section takes.</summary>
    public static string? SectionOf(int slot) => (AttachSlot)slot switch
    {
        AttachSlot.Hat => "head",
        AttachSlot.Body => "armour",
        AttachSlot.Legs => "legs",
        AttachSlot.Shirt => "shirt",
        AttachSlot.Boots => "boots",
        AttachSlot.Belt => "belt",
        _ => null,
    };

    #endregion

    #region Layers over

    /// <summary>
    /// A further appearance of a slot: the parts and body layers of a look built with that item alone are added to <paramref name="look"/>, with
    /// the body regions it hides.
    /// </summary>
    static void LayOver(IAppearanceSource source, CharacterCold cold, Loadout worn, CharacterAppearance look, GameRecord record)
    {
        var alone = SavedKit.Copy(worn);
        alone.Clothing.Clear();
        alone.Carried.Clear();
        alone.Weapons.Clear();
        alone.Crossbow = null;
        alone.Backpack = record.Type == FcsRecordType.Container ? new LoadoutItem(record) : null;
        if (record.Type != FcsRecordType.Container) alone.Clothing.Add(new LoadoutItem(record));

        if (source.Create(cold.RecordId, alone, cold.Shaved) is not { } built) return;

        look.Parts.AddRange(built.Parts.Where(p => p.Record == record));
        look.BodyLayers.AddRange(built.BodyLayers.Where(l => l.Record == record));
        SetHiddenParts?.Invoke(look, look.HiddenParts | record.GetInt("hide parts"));
    }

    #endregion

    #region Dye and adjustment

    /// <summary>An appearance's dye on its parts and body layers, and its adjustment on its parts.</summary>
    void Adjust(CharacterAppearance look, Layer layer, GameRecord record)
    {
        for (int i = 0; i < look.Parts.Count; i++)
        {
            var part = look.Parts[i];
            if (part.Record != record) continue;

            if (layer.Dyed && DyeOf(layer, part.Colour ?? ItemColours.Of(db, record)) is { } dye) part = part with { Colour = dye };
            if (layer.Adjusted && part.Mode == AttachMode.Bone) part = part with { Local = (part.Local ?? Matrix4x4.Identity) * layer.Transform() };
            look.Parts[i] = part;
            if (layer.Adjusted && part.Mode == AttachMode.SharedSkeleton) AdjustSkinned(look, part, layer.Transform());
        }

        if (!layer.Dyed) return;

        for (int i = 0; i < look.BodyLayers.Count; i++)
        {
            var body = look.BodyLayers[i];
            if (body.Record == record && DyeOf(layer, body.Colour ?? ItemColours.Of(db, record)) is { } dye) look.BodyLayers[i] = body with { Colour = dye };
        }
    }

    /// <summary>
    /// A skinned part's adjustment: a head piece (hat or eyes slot) is drawn on the head bone instead, so it follows the head and shows in
    /// portraits (<see cref="RenderPatch"/> converts it when its asset is built); other gear in the character's frame.
    /// </summary>
    static void AdjustSkinned(CharacterAppearance look, CharacterPart part, Matrix4x4 transform)
    {
        var table = part.Slot is AttachSlot.Hat or AttachSlot.Eyes ? OnHead : Skinned;
        table.GetValue(look, _ => new Dictionary<CharacterPart, Matrix4x4>(ReferenceEqualityComparer.Instance))[part] = transform;
    }

    /// <summary>
    /// A COLOR_DATA of the layer's two colours (a channel left unset takes <paramref name="was"/>'s), made once per pair and kept out of the
    /// data; null when records cannot be made on this game.
    /// </summary>
    static GameRecord? DyeOf(Layer layer, GameRecord? was)
    {
        int c1 = layer.Dye1 >= 0 ? layer.Dye1 : was?.GetInt("color 1") ?? 0x808080;
        int c2 = layer.Dye2 >= 0 ? layer.Dye2 : was?.GetInt("color 2") ?? 0x808080;
        if (MakeRecord is null || IntsOf is null) return null;

        return Dyes.GetOrAdd((c1 & 0xFFFFFF, c2 & 0xFFFFFF), key =>
        {
            var record = MakeRecord($"transmog-dye-{key.Item1:X6}-{key.Item2:X6}", FcsRecordType.ColorData, $"#{key.Item1:X6} #{key.Item2:X6}",
                "dxseba.transmog");
            var ints = IntsOf(record);
            ints["color 1"] = key.Item1;
            ints["color 2"] = key.Item2;
            return record;
        });
    }

    static Func<string, FcsRecordType, string, string, GameRecord>? RecordMaker()
    {
        var ctor = AccessTools.Constructor(typeof(GameRecord), [typeof(string), typeof(FcsRecordType), typeof(string), typeof(string)]);
        if (ctor is null) return null;

        return (id, type, name, by) => (GameRecord)ctor.Invoke([id, type, name, by]);
    }

    #endregion
}
