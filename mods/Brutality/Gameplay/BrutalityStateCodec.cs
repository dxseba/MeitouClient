using System.Text.Json;

namespace Meitou.Brutality;

/// <summary>State payload for a mod sidecar; the host bridge supplies durable identity resolution.</summary>
public static class BrutalityStateCodec
{
    sealed record Payload(int Version, BrutalityDurableState[] Actors);
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    public static void Write(Stream stream, IEnumerable<BrutalityDurableState> actors) =>
        JsonSerializer.Serialize(stream, new Payload(1, actors.Where(Valid).OrderBy(a => a.Key, StringComparer.Ordinal).ToArray()), Json);
    public static IReadOnlyList<BrutalityDurableState> Read(Stream stream)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Payload>(stream, Json);
            return saved is { Version: 1, Actors: not null } ? saved.Actors.Where(Valid).ToArray() : [];
        }
        catch (JsonException) { return []; }
    }
    public static bool Valid(BrutalityDurableState? actor) => actor is { Key.Length: > 0, Limbs: not null }
        && actor.Key.Length <= 256 && float.IsFinite(actor.Experience) && actor.Experience is >= 0 and <= BrutalityProgress.MaximumExperience
        && actor.SevereHead.IsValid && (actor.Head is null || actor.Head.Value.IsValid) && (actor.Torso is null || actor.Torso.Value.IsValid)
        && actor.Wounds is { IsValid: true } && actor.Limbs.Length <= 4 && actor.Limbs.All(c => c.IsValid) && actor.Limbs.Select(c => c.LimbSlot).Distinct().Count() == actor.Limbs.Length;
}
