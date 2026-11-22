using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.KillCounter;

/// <summary>
/// The original's three lines of the character stats window ("Kills", "KO's", "Limbs Removed"), here under the stats of the HUD's character panel,
/// for whichever character the panel shows.
/// </summary>
public sealed class KillCounterUi : IUiExtension
{
    public string Id => "dxseba.killcounter.ui";

    public void Register(UiRegistration registration) => registration.AddCharacterPanelLines(Lines);

    static IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
    {
        var id = context.Character.LastingId;
        var tally = id.IsNone || KillCounterSystem.Current is not { } counter ? default : counter.Published.GetValueOrDefault(id.Value);

        return
        [
            new("Kills", tally.Kills.ToString(CultureInfo.InvariantCulture)),
            new("KO's", tally.Knockouts.ToString(CultureInfo.InvariantCulture)),
            new("Limbs Removed", tally.Limbs.ToString(CultureInfo.InvariantCulture)),
        ];
    }
}
