using System.Diagnostics;
using System.Globalization;

namespace Meitou.SentientSands;

/// <summary>
/// Radiant talks (decompile: <c>/radiant</c> with <c>participants</c>, <c>npcs</c>, <c>player_context</c>, <c>events</c>, <c>changed_towns</c>,
/// <c>unique_states</c>, <c>player_towns</c>): every <c>g_radiantIntervalSeconds</c> of unpaused play the player's characters near the lead
/// (within <c>g_proximityRadius</c>) and the NPCs near it are sent; the server picks who talks and plays the lines through the pipe. The server
/// sets the clock and the radius with <c>SET_CONFIG</c> (its defaults: on, 10 minutes, a talk radius of 150).
/// </summary>
sealed partial class ChatWindow
{
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _radiantSeconds = 600, _elapsed;
    float _radius = 150;
    bool _radiantOn = true;
    long _lastMs;

    void Radiant(bool paused)
    {
        long now = _clock.ElapsedMilliseconds;
        if (!paused) _elapsed += (now - _lastMs) / 1000.0;
        _lastMs = now;
        if (!settings.Radiant || !_radiantOn || _open || _elapsed < _radiantSeconds) return;

        _elapsed = 0;
        SendRadiant();
    }

    void SendRadiant()
    {
        var facts = SentientSystem.Facts;
        if (facts.Of(facts.Lead) is not { } lead) return;

        var near = facts.Near(lead.Position, Math.Max(_radius, 1000)).ToList();
        var body = new Dictionary<string, object?>
        {
            ["participants"] = near.Where(c => c.IsPlayer && !c.Down
                && Within(c, lead, _radius)).Select(c => c.Context(facts.Day, facts.Hour, facts.Minute)).ToList(),
            ["npcs"] = near.Where(c => !c.IsPlayer && !c.Down).Take(20).Select(c => c.Context(facts.Day, facts.Hour, facts.Minute)).ToList(),
        };
        bridge.Listen();
        bridge.Request("/radiant", Report(body, "player_context"), null);
    }

    static bool Within(CharacterFacts c, CharacterFacts lead, float radius) => System.Numerics.Vector3.Distance(c.Position, lead.Position) <= radius;

    /// <summary><c>SET_CONFIG: name: value</c> for the settings this side keeps.</summary>
    void Config(string rest)
    {
        int colon = rest.IndexOf(':');
        if (colon < 0) return;

        string name = rest[..colon].Trim(), value = rest[(colon + 1)..].Trim();
        bool number = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        switch (name)
        {
            case "g_enableRadiant": _radiantOn = value == "1"; break;
            case "g_radiantIntervalSeconds" when number && v > 0: _radiantSeconds = v; break;
            case "g_proximityRadius" when number && v > 0: _radius = (float)v; break;
        }
    }
}
