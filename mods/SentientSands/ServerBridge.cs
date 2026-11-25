using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Meitou.SentientSands;

/// <summary>
/// The link to the original's server, which the player starts themselves (the port never runs it): requests go as JSON POSTs to its HTTP
/// address (<c>http://127.0.0.1:5000/</c>), and its replies come back as single messages on the named pipe <c>\\.\pipe\SentientSands</c>, which
/// this side serves as the original's plugin does (one instance, made again after each message). Background threads only; messages wait in
/// <see cref="Incoming"/> for the host's frame.
/// </summary>
sealed class ServerBridge(SentientSettings settings, Action<string> log)
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    Thread? _pipe;
    volatile bool _postFailed;

    /// <summary>The server's messages (<c>NPC_SAY: ...</c>, <c>NPC_ACTION: ...</c>, <c>NOTIFY: ...</c>, <c>CMD: ...</c>), in order.</summary>
    public ConcurrentQueue<string> Incoming { get; } = new();

    /// <summary>Starts serving the pipe (once).</summary>
    public void Listen()
    {
        if (_pipe is not null) return;

        _pipe = new Thread(Serve) { IsBackground = true, Name = "SentientSands pipe" };
        _pipe.Start();
    }

    void Serve()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream("SentientSands", PipeDirection.In, 1, PipeTransmissionMode.Byte);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8);
                string message = reader.ReadToEnd();
                if (message.Length > 0) Incoming.Enqueue(message);
            }
            catch (IOException e)
            {
                log($"pipe: {e.Message}");
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>The answers of <see cref="Request"/>, run by the host's frame (<see cref="RunReplies"/>).</summary>
    readonly ConcurrentQueue<Action> _replies = new();

    /// <summary>POSTs <paramref name="json"/> to <paramref name="path"/> of the server; a failure is logged once until one succeeds.</summary>
    public void Post(string path, string json) => Request(path, json, null);

    /// <summary>POSTs <paramref name="body"/> as JSON; <paramref name="done"/> gets the answer's JSON (null on a failure) on the host's
    /// thread.</summary>
    public void Request(string path, object body, Action<JsonElement?>? done) =>
        Request(path, JsonSerializer.Serialize(body), done);

    void Request(string path, string json, Action<JsonElement?>? done)
    {
        string url = settings.ServerUrl.TrimEnd('/') + path;
        _ = Task.Run(async () =>
        {
            JsonElement? answer = null;
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(url, content);
                string text = await response.Content.ReadAsStringAsync();
                answer = text.Length > 0 && text[0] == '{' ? JsonDocument.Parse(text).RootElement.Clone() : null;
                _postFailed = false;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (!_postFailed) Incoming.Enqueue($"NOTIFY: The Sentient Sands server does not answer at {settings.ServerUrl} (start it yourself).");
                _postFailed = true;
                log($"POST {path}: {e.Message}");
            }

            if (done is not null) _replies.Enqueue(() => done(answer));
        });
    }

    /// <summary>Runs the answers that came back (host's thread).</summary>
    public void RunReplies()
    {
        while (_replies.TryDequeue(out var reply)) reply();
    }
}
