using Meitou.Game.Ui;

namespace Meitou.KillCounter;

/// <summary>A one-line label under the toolbar with the selected character's tally.</summary>
public sealed class KillCounterUi : IUiExtension
{
    public string Id => "dxseba.killcounter.ui";

    public void Register(UiRegistration registration)
    {
        string text = registration.RegisterStyle("text", new() { Background = new(.08f, .07f, .06f, .85f), Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 2, 6, 2) });
        registration.Contribute(UiSlots.Overlay, frame =>
        {
            string line = frame.Text(HudAdapter.Key);
            if (line.Length == 0) return new("killcounter-hidden") { Visible = false };

            var root = new UiNode("killcounter") { Placement = UiPlacement.Fill, Pickable = false };
            root.Children.Add(new("killcounter-line", UiNodeKind.Label) { Placement = new(320, 24, Left: 12, Top: 44), Text = line, Style = text });
            return root;
        }, order: 40);
    }
}
