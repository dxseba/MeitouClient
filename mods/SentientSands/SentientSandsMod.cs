using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SentientSands;

/// <summary>
/// Port of the in-game side of Sentient Sands Rebirth (Workshop 3819590113): free-text chat with an NPC through the original's own server, which
/// the player starts themselves (the port never runs it), and the actions its replies carry. README "Facts" says what follows the original.
/// </summary>
public sealed class SentientSandsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SentientSettings();
        context.AddSettings(settings);

        var bridge = new ServerBridge(settings, context.Log);
        var chat = new ChatWindow(context, bridge, settings);
        var hub = new HubWindow(context, bridge, settings);
        context.AddSystem("chat", _ => new SentientSystem(settings, context.Population, context.Log), SystemAnchor.End);
        context.AddUi(chat);
        context.AddUi(hub);
        context.AddFrame(chat.Frame);
        context.AddKeyAction("hub", "Open the SSR hub", _ => hub.Toggle(), Binding.Of(Key.F8));
        context.AddKeyAction("chat", "Chat with the nearest NPC", chat.Toggle, Binding.Of(Key.Backslash));
        context.Log("Sentient Sands: start the original's server yourself; this side talks to " + settings.ServerUrl);
    }
}
