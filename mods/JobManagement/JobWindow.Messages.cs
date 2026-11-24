using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.JobManagement;

/// <summary>
/// The status line and the original's "Recent Job Manager Messages": the 10 most recent non-empty messages of the session, timestamped, oldest
/// first; with <see cref="JobSettings.Debug"/> each message and each step's commands also go to the mod log.
/// </summary>
public sealed partial class JobWindow
{
    const int KeptMessages = 10;

    readonly Queue<string> _messages = new();

    string _statusText = "";

    /// <summary>The header's status line; a non-empty one is also kept in the message log.</summary>
    string Status
    {
        get => _statusText;
        set
        {
            _statusText = value;
            if (value.Length > 0) Note(value);
        }
    }

    void Note(string message)
    {
        _messages.Enqueue($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}");
        while (_messages.Count > KeptMessages) _messages.Dequeue();

        if (settings.Debug) log(message);
    }

    /// <summary>The commands of one step, for the debug log.</summary>
    void Trace(IReadOnlyList<SimCommand> commands)
    {
        if (settings.Debug) log($"step sent {commands.Count} command(s): {string.Join(", ", commands.Select(c => c.GetType().Name))}");
    }

    UiNode MessageLog(float width, float height)
    {
        var column = UiKit.Column("jm-messages", width, height, UiKit.Gap);
        column.Children.Add(Label("log.title", "Recent Job Manager Messages", width));
        column.Children.Add(Label("log.help", "The 10 most recent non-empty timestamped messages from this game session (oldest to newest).", width,
            _styles!.Dim));

        if (_messages.Count == 0) column.Children.Add(Label("log.none", "No recent messages in this session.", width, _styles.Dim));

        int i = 0;
        foreach (string message in _messages) column.Children.Add(Label($"log.{i++}", message, width));
        return column;
    }
}
