using Meitou.Modding;

namespace Meitou.SentientSands;

/// <summary>The port's settings (the original keeps them on its server's dashboard).</summary>
public sealed class SentientSettings
{
    [Setting("Server address", Hint = "Where the Sentient Sands server you started listens (the original's default).")]
    public string ServerUrl { get; set; } = "http://127.0.0.1:5000/";

    [Setting("Hold the NPC while talking", Hint = "The NPC stops and stays while the chat window is open on it.")]
    public bool HoldInDialogue { get; set; } = true;

    [Setting("Radiant talks", Hint = "Your characters and nearby NPCs talk among themselves now and then (the server also switches it).")]
    public bool Radiant { get; set; } = true;

    [Setting("Hire contract package", Hint = "AI package a hired NPC's squad runs (string id; Bodyguard by default).")]
    public string HirePackage { get; set; } = "5090-gamedata.base";
}
