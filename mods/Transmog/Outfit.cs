using System.Collections.Immutable;
using System.Numerics;

namespace Meitou.Transmog;

/// <summary>
/// One appearance shown in a slot: the item record whose look is drawn (empty in a limb slot: the natural limb), its two dye colours (0xRRGGBB, -1:
/// the item's own) and its model adjustment (position in units, rotation in degrees about x, y and z, uniform scale).
/// </summary>
sealed record Layer(string Item)
{
    public int Dye1 { get; init; } = -1;
    public int Dye2 { get; init; } = -1;
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public float Scale { get; init; } = 1;

    /// <summary>A weapon slot's "Enable rendering": false hides the weapon.</summary>
    public bool Rendered { get; init; } = true;

    /// <summary>A weapon slot's "Use original weapon material": the real weapon's manufacturer and model; else <see cref="Finish"/>.</summary>
    public bool OriginalFinish { get; init; } = true;

    /// <summary>A weapon slot's WEAPON_MANUFACTURER (empty: the item's plain look) when <see cref="OriginalFinish"/> is off.</summary>
    public string Finish { get; init; } = "";

    /// <summary>Whether a dye colour is set.</summary>
    public bool Dyed => Dye1 >= 0 || Dye2 >= 0;

    /// <summary>Whether the model is moved, turned or scaled.</summary>
    public bool Adjusted => Position != Vector3.Zero || Rotation != Vector3.Zero || Scale != 1;

    /// <summary>The adjustment as a matrix applied in the part's own frame (scale, then the turns about x, y and z, then the move).</summary>
    public Matrix4x4 Transform()
    {
        const float radians = MathF.PI / 180;
        return Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationX(Rotation.X * radians) * Matrix4x4.CreateRotationY(Rotation.Y * radians)
            * Matrix4x4.CreateRotationZ(Rotation.Z * radians) * Matrix4x4.CreateTranslation(Position);
    }
}

/// <summary>
/// A character's transmog: whether it shows, and the appearances by slot (an inventory section, or <c>limb0</c> to <c>limb3</c>), at most
/// <see cref="MaxLayers"/> each. Immutable: a change makes a new outfit, so readers on other threads see a whole one.
/// </summary>
sealed record Outfit(bool On, ImmutableDictionary<string, ImmutableList<Layer>> Slots)
{
    /// <summary>The Weapons tab's own switch, used when "Enable independent weapon transmog" is on.</summary>
    public bool WeaponsOn { get; init; }

    /// <summary>The original's limit of appearances in one slot (its "This slot already has 32 appearances").</summary>
    public const int MaxLayers = 32;

    public static readonly Outfit Empty = new(false, ImmutableDictionary<string, ImmutableList<Layer>>.Empty);

    /// <summary>The layers of <paramref name="slot"/> (empty when none).</summary>
    public ImmutableList<Layer> Of(string slot) => Slots.TryGetValue(slot, out var layers) ? layers : [];

    /// <summary>This outfit with <paramref name="slot"/>'s layers replaced (an empty list removes the slot).</summary>
    public Outfit With(string slot, ImmutableList<Layer> layers) =>
        this with { Slots = layers.Count == 0 ? Slots.Remove(slot) : Slots.SetItem(slot, layers) };

    /// <summary>Whether it changes nothing: off, or on with no slot set while only set slots are taken over.</summary>
    public bool Idle(bool selectedSlotsOnly, bool independentWeapons) =>
        !(On || independentWeapons && WeaponsOn) || selectedSlotsOnly && Slots.IsEmpty;
}

/// <summary>
/// The slots the original's workbench lists (its slot table: the inventory sections <c>head</c>, <c>shirt</c>, <c>armour</c>, <c>legs</c>,
/// <c>boots</c>, <c>belt</c>, <c>backpack_attach</c> and the limbs <c>limb0</c> to <c>limb3</c>), with their captions.
/// </summary>
static class Slots
{
    public static readonly (string Id, string Caption)[] All =
    [
        ("head", "Head"), ("shirt", "Shirt"), ("armour", "Armor"), ("legs", "Pants"), ("boots", "Boots"), ("belt", "Belt"),
        ("backpack_attach", "Backpack"), ("limb0", "Left arm"), ("limb1", "Right arm"), ("limb2", "Left leg"), ("limb3", "Right leg"),
        ("weapon0", "Inventory weapon 1"), ("weapon1", "Inventory weapon 2"),
    ];

    /// <summary>The worn clothing sections (ARMOUR records).</summary>
    public static readonly string[] Clothing = ["head", "shirt", "armour", "legs", "boots", "belt"];

    public const string Backpack = "backpack_attach";

    /// <summary>The limb index 0 to 3 of a limb slot; -1 for another slot.</summary>
    public static int WeaponOf(string slot) => slot is ['w', 'e', 'a', 'p', 'o', 'n', var d] && d is >= '0' and <= '1' ? d - '0' : -1;

    public static int LimbOf(string slot) => slot is ['l', 'i', 'm', 'b', var d] && d is >= '0' and <= '3' ? d - '0' : -1;
}
