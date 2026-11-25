using Meitou.Modding;

namespace Meitou.Nitoryu;

/// <summary>
/// The player's switches on the Settings screen's Mods page (the original's Mod Config Menu pages; its balance stays in the data and the
/// original's <c>Nitoryu.ini</c>).
/// </summary>
public sealed class NitoryuSettings
{
    [Setting("Dual wielding", Hint = "Fighters carrying an allowed pair draw both and fight with one in each hand.")]
    public bool Enabled { get; set; } = true;

    [Setting("Only my strikes", Hint = "A dual fighter attacks with the pair's own strikes; off: the game's swings are mixed in ([Main] only_my_strikes).")]
    public bool OnlyMyStrikes { get; set; } = true;

    [Setting("Second blade parries", Hint = "From Bushi the second blade parries hits the main one missed ([Guard] guard).")]
    public bool Guard { get; set; } = true;

    [Setting("Flow", Hint = "From Kenshi, three attacks in 5 s give 4 s of faster, harder cuts ([Flow] flow).")]
    public bool Flow { get; set; } = true;

    [Setting("Own animations", Hint = "The pair's own strikes, specials and stances play the original's animations.")]
    public bool Animations { get; set; } = true;

    [Setting("Mirror second-blade swings", Hint = "Experimental: a second-blade strike without its own animation plays the game's swing mirrored onto the left side.")]
    public bool Mirror { get; set; }

    [Setting("Second blade in the left hand", Hint = "Draw the second blade in the left hand (display only).")]
    public bool ShowBlade { get; set; } = true;

    [Setting("Rank-up message", Hint = "A message when one of your characters reaches a new rank ([Stats] rank_up_message).")]
    public bool RankUpMessage { get; set; } = true;
}
