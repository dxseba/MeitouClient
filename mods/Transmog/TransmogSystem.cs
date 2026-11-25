using System.Collections.Immutable;
using System.Text.Json;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Transmog;

/// <summary>The window's change: <paramref name="Character"/> shows <paramref name="Outfit"/> from now on (null clears its transmog).</summary>
sealed record SetOutfitOrder(CharacterId Character, Outfit? Outfit) : SimCommand, IPausedCommand;

/// <summary>
/// The outfits of the world, keyed by the characters' lasting ids and saved with the game. Only the window's orders change them, in the serial
/// input step (also while paused), so the look and the saved data follow the commands; nothing in the simulation reads them (display only).
/// </summary>
sealed class TransmogSystem : IPausedCommandHandler, IModSaveData
{
    const int Version = 1;

    /// <summary>The outfits by lasting id, swapped whole on a change so the publish, the renderer and the window read a consistent set.</summary>
    public static volatile ImmutableDictionary<ulong, Outfit> Outfits = ImmutableDictionary<ulong, Outfit>.Empty;

    public TransmogSystem() => Outfits = ImmutableDictionary<ulong, Outfit>.Empty;

    /// <summary>The outfit of a character with lasting id <paramref name="id"/>; null without one.</summary>
    public static Outfit? Of(LastingId id) => !id.IsNone && Outfits.TryGetValue(id.Value, out var outfit) ? outfit : null;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SetOutfitOrder order) Apply(world, order);
        }
    }

    static void Apply(World world, SetOutfitOrder order)
    {
        int slot = order.Character.Slot;
        var table = world.Characters;
        if (slot < 0 || slot >= table.Previous.Length || table.Previous[slot].Generation != order.Character.Generation) return;

        if (table.Cold(slot) is not { } cold) return;

        if (order.Outfit is null)
        {
            if (!cold.LastingId.IsNone) Outfits = Outfits.Remove(cold.LastingId.Value);
            return;
        }

        var id = world.LastingIds.GetOrGive(order.Character);
        Outfits = Outfits.SetItem(id.Value, order.Outfit);
    }

    #region Save

    public void Write(Stream stream)
    {
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WriteNumber("version", Version);
        json.WriteStartArray("characters");
        foreach (var (id, outfit) in Outfits.OrderBy(p => p.Key))
        {
            json.WriteStartObject();
            json.WriteNumber("id", id);
            OutfitJson.Write(json, outfit);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    public void Read(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var read = ImmutableDictionary.CreateBuilder<ulong, Outfit>();
        if (document.RootElement.TryGetProperty("characters", out var characters))
        {
            foreach (var c in characters.EnumerateArray())
            {
                read[c.GetProperty("id").GetUInt64()] = OutfitJson.Read(c);
            }
        }

        Outfits = read.ToImmutable();
    }

    #endregion
}
