using System.Globalization;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalRead;

/// <summary>
/// The original's <c>debugLogging</c>, ported to the mod log: each portrait member's state as it changes, and two of its probe keys, the ones
/// about the game rather than its interface widgets: Ctrl+Alt+F11 (the selected squad members) and Ctrl+Alt+F12 (the member states), each
/// writing every member's state and published facts. Both do nothing while debug logging is off. Host thread only.
/// </summary>
sealed class StateLog
{
    readonly ModContext _context;

    readonly Dictionary<CharacterId, VitalState> _last = [];

    public StateLog(ModContext context)
    {
        _context = context;
        var probe = KeyModifiers.Ctrl | KeyModifiers.Alt;
        context.AddKeyAction("probe-selected", "Vital Read: log selected members", Selected, Binding.Of(Key.F11).With(probe));
        context.AddKeyAction("probe-members", "Vital Read: log member states", Members, Binding.Of(Key.F12).With(probe));
    }

    /// <summary>
    /// Logs <paramref name="member"/>'s state when it is not the one last seen.
    /// </summary>
    public void Seen(CharacterSnapshot member, VitalState state)
    {
        if (_last.TryGetValue(member.Id, out var last) && last == state) return;

        _last[member.Id] = state;
        _context.Log($"{member.Name}: {Name(state)}");
    }

    void Selected(ModKeyContext press) => Probe(press, press.Snapshot.Characters.Where(c => c.IsPlayer && press.Selected.Contains(c.Id)), "selected");

    void Members(ModKeyContext press) => Probe(press, press.Snapshot.Characters.Where(c => c.IsPlayer), "members");

    void Probe(ModKeyContext press, IEnumerable<CharacterSnapshot> members, string what)
    {
        if (!VitalReadMod.Settings.DebugLogging || VitalReadMod.Running is not { } system) return;

        var list = members.ToList();
        _context.Log(string.Create(CultureInfo.InvariantCulture, $"probe {what}: {list.Count} at tick {press.Tick}"));
        foreach (var member in list) _context.Log(Line(member, system.Facts));
    }

    static string Line(CharacterSnapshot member, IReadOnlyDictionary<CharacterId, VitalFacts> all)
    {
        var facts = all.GetValueOrDefault(member.Id);
        return $"  {member.Name}: {Name(PortraitBadges.StateOf(member, all))}"
            + $" dying={facts.Dying} recovery_coma={facts.RecoveryComa} crippled_leg={facts.LegCrippled} crippled_arm={facts.ArmCrippled}"
            + $" bed_function={facts.Bed}";
    }

    static string Name(VitalState state) => state == VitalState.None ? "none" : PortraitBadges.Caption(state);
}
