using System.Numerics;
using Meitou.Data.Fcs;

namespace Meitou.ItemMarkers;

/// <summary>
/// What a grade provider is asked about: the item's record, material and manufacturer ids, its record type (null for a record the data lacks) and
/// level (gear only, else 0).
/// </summary>
public readonly record struct GradeQuery(string Record, string Material, string Company, FcsRecordType? Type, int Level);

/// <summary>
/// The C# form of the original's grade-provider SDK (<c>sdk/ItemMarkersApi.h</c>, v1): another code mod that declares a dependency on
/// <c>dxseba.itemmarkers</c> registers a provider that gives an item instance a grade of its own. Providers are asked before the profile rules and
/// the game's grades, the highest <c>priority</c> first (ties in registration order); null passes the item on. At most 32, each name once. A provider
/// runs on the interface's thread each time a cell is laid out, so it must be quick and must not touch the world; one that throws or answers a rank
/// outside 0 to 5 or a colour that is not finite is dropped until registered again, with a log line. The player's style, strength and minimum grade
/// still apply; item stats and tooltips are the provider's own business.
/// </summary>
public static class ItemMarkersApi
{
    public const int MaxProviders = 32;

    sealed record Provider(string Name, int Priority, int Order, Func<GradeQuery, ItemGrade?> Ask);

    static readonly Lock Gate = new();
    static Provider[] s_providers = [];
    static int s_order;

    internal static Action<string>? Log { get; set; }

    /// <summary>
    /// Adds <paramref name="provider"/> under <paramref name="name"/>; dispose the result to remove it. False (and null) when the name is taken,
    /// empty, or 32 are registered.
    /// </summary>
    public static IDisposable? RegisterProvider(string name, int priority, Func<GradeQuery, ItemGrade?> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name) || s_providers.Length >= MaxProviders || s_providers.Any(p => p.Name == name)) return null;

            var added = new Provider(name, priority, s_order++, provider);
            s_providers = [.. s_providers.Append(added).OrderByDescending(p => p.Priority).ThenBy(p => p.Order)];
            return new Registration(added);
        }
    }

    /// <summary>Removes the provider named <paramref name="name"/>; false when there is none.</summary>
    public static bool UnregisterProvider(string name)
    {
        lock (Gate)
        {
            int before = s_providers.Length;
            s_providers = [.. s_providers.Where(p => p.Name != name)];
            return s_providers.Length != before;
        }
    }

    /// <summary>The first provider's answer for <paramref name="item"/>; null when none answers.</summary>
    internal static ItemGrade? Ask(in ItemFacts item, FcsRecordType? type)
    {
        var providers = s_providers;
        if (providers.Length == 0) return null;

        var query = new GradeQuery(item.Record, item.Material, item.Company, type, Grades.IsGear(type) ? item.Level : 0);
        foreach (var p in providers)
        {
            if (Answer(p, query) is { } grade) return grade;
        }

        return null;
    }

    static ItemGrade? Answer(Provider p, GradeQuery query)
    {
        try
        {
            var grade = p.Ask(query);
            if (grade is not { } g) return null;
            if (g.Rank is >= 0 and <= 5 && Finite(g.Colour)) return g;

            Drop(p, "returned an invalid appearance");
        }
        catch (Exception e)
        {
            Drop(p, $"failed: {e.Message}");
        }

        return null;
    }

    static bool Finite(Vector4 c) => float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z) && float.IsFinite(c.W);

    static void Remove(Provider p)
    {
        lock (Gate) s_providers = [.. s_providers.Where(x => !ReferenceEquals(x, p))];
    }

    static void Drop(Provider p, string why)
    {
        Remove(p);
        Log?.Invoke($"grade provider '{p.Name}' {why} - provider disabled");
    }

    sealed class Registration(Provider provider) : IDisposable
    {
        bool _done;

        public void Dispose()
        {
            if (_done) return;

            _done = true;
            Remove(provider);
        }
    }
}
