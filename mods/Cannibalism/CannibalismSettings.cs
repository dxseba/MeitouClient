using Meitou.Modding;

namespace Meitou.Cannibalism;

/// <summary>The mod's settings, under Mod settings.</summary>
public sealed class CannibalismSettings
{
    [Setting("Enable Cannibalism", Hint = "Shows Eat Alive on unconscious, caged or racked characters.")]
    public bool Enabled { get; set; } = true;

    [Setting("Hunger per hour", Hint = "Hunger an eater regains per in-game hour of biting (3 is full).", Min = 0, Max = 3, Step = 0.05)]
    public float HungerPerHour { get; set; } = 0.7f;
}
