using Meitou.Modding;

namespace Meitou.BetterLooting;

/// <summary>
/// The original's five preset slots (its MCM page "Preset slots": a name, Save, Apply, Clear; <b>Verified</b>, decompiled DLL strings): each slot
/// is a named preset of the settings group (<see cref="ModSettingsGroup.SavePreset"/>), kept by the game in <c>meitou.user.json</c>. Applying a
/// slot sets every rule back as saved but leaves the slots' names as they are.
/// </summary>
public sealed partial class LootSettings
{
    const string Slots = "Preset slots";

    /// <summary>The settings group the slots save into; set once the settings are registered.</summary>
    internal ModSettingsGroup? Group { get; set; }

    [Setting("Slot 1", Hint = "A name for this slot, for your own use.", SubGroup = Slots, MaxLength = 64)]
    public string Slot1Name { get; set; } = "";

    [Setting("Save", Hint = "Keeps the current rules in slot 1.", SubGroup = Slots, Button = "Save")]
    public Action Slot1Save => () => Save(1);

    [Setting("Apply", Hint = "Sets the rules saved in slot 1.", SubGroup = Slots, Button = "Apply")]
    public Action Slot1Apply => () => Apply(1);

    [Setting("Clear", Hint = "Empties slot 1; the current rules stay.", SubGroup = Slots, Button = "Clear")]
    public Action Slot1Clear => () => Clear(1);

    [Setting("Slot 2", Hint = "A name for this slot, for your own use.", SubGroup = Slots, MaxLength = 64)]
    public string Slot2Name { get; set; } = "";

    [Setting("Save", Hint = "Keeps the current rules in slot 2.", SubGroup = Slots, Button = "Save")]
    public Action Slot2Save => () => Save(2);

    [Setting("Apply", Hint = "Sets the rules saved in slot 2.", SubGroup = Slots, Button = "Apply")]
    public Action Slot2Apply => () => Apply(2);

    [Setting("Clear", Hint = "Empties slot 2; the current rules stay.", SubGroup = Slots, Button = "Clear")]
    public Action Slot2Clear => () => Clear(2);

    [Setting("Slot 3", Hint = "A name for this slot, for your own use.", SubGroup = Slots, MaxLength = 64)]
    public string Slot3Name { get; set; } = "";

    [Setting("Save", Hint = "Keeps the current rules in slot 3.", SubGroup = Slots, Button = "Save")]
    public Action Slot3Save => () => Save(3);

    [Setting("Apply", Hint = "Sets the rules saved in slot 3.", SubGroup = Slots, Button = "Apply")]
    public Action Slot3Apply => () => Apply(3);

    [Setting("Clear", Hint = "Empties slot 3; the current rules stay.", SubGroup = Slots, Button = "Clear")]
    public Action Slot3Clear => () => Clear(3);

    [Setting("Slot 4", Hint = "A name for this slot, for your own use.", SubGroup = Slots, MaxLength = 64)]
    public string Slot4Name { get; set; } = "";

    [Setting("Save", Hint = "Keeps the current rules in slot 4.", SubGroup = Slots, Button = "Save")]
    public Action Slot4Save => () => Save(4);

    [Setting("Apply", Hint = "Sets the rules saved in slot 4.", SubGroup = Slots, Button = "Apply")]
    public Action Slot4Apply => () => Apply(4);

    [Setting("Clear", Hint = "Empties slot 4; the current rules stay.", SubGroup = Slots, Button = "Clear")]
    public Action Slot4Clear => () => Clear(4);

    [Setting("Slot 5", Hint = "A name for this slot, for your own use.", SubGroup = Slots, MaxLength = 64)]
    public string Slot5Name { get; set; } = "";

    [Setting("Save", Hint = "Keeps the current rules in slot 5.", SubGroup = Slots, Button = "Save")]
    public Action Slot5Save => () => Save(5);

    [Setting("Apply", Hint = "Sets the rules saved in slot 5.", SubGroup = Slots, Button = "Apply")]
    public Action Slot5Apply => () => Apply(5);

    [Setting("Clear", Hint = "Empties slot 5; the current rules stay.", SubGroup = Slots, Button = "Clear")]
    public Action Slot5Clear => () => Clear(5);

    static string PresetOf(int slot) => "slot " + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    void Save(int slot)
    {
        if (Group?.SavePreset(PresetOf(slot)) == false) throw new InvalidOperationException("The preset could not be saved.");
    }

    /// <summary>Sets the slot's rules; the slots' names are put back as they were (a preset keeps every value setting, names included).</summary>
    void Apply(int slot)
    {
        if (Group is not { } group) return;

        var names = group.Settings.Where(IsSlotName).Select(s => (Setting: s, Value: s.Get())).ToList();
        if (!group.ApplyPreset(PresetOf(slot))) throw new InvalidOperationException("This slot is empty.");

        foreach (var (setting, value) in names)
        {
            if (!Equals(setting.Get(), value)) setting.Set(value!);
        }
    }

    void Clear(int slot) => Group?.DeletePreset(PresetOf(slot));

    static bool IsSlotName(ModSetting setting) =>
        setting.Key.StartsWith("Slot", StringComparison.Ordinal) && setting.Key.EndsWith("Name", StringComparison.Ordinal);
}
