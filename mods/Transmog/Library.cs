using System.Text.Json;
using Meitou.Modding;

namespace Meitou.Transmog;

/// <summary>
/// The player's favorites (item ids) and outfit presets (named outfits), kept across saves as the original keeps its <c>favorites.json</c> and
/// <c>presets.json</c> under <c>%LOCALAPPDATA%\Kenshi\Transmog</c>; the port's live under <c>%LOCALAPPDATA%\Meitou\Transmog</c> (the
/// original's format is its own). Read once, written on each change, on the host thread only: nothing here reaches the world.
/// </summary>
sealed class Library(ModContext context)
{
    readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meitou", "Transmog");

    HashSet<string>? _favorites;
    List<(string Name, Outfit Outfit)>? _presets;

    public IReadOnlySet<string> Favorites => _favorites ??= ReadFavorites();

    public IReadOnlyList<(string Name, Outfit Outfit)> Presets => _presets ??= ReadPresets();

    /// <summary>Adds or removes a favorite; true when it is one now.</summary>
    public bool ToggleFavorite(string item)
    {
        var set = (HashSet<string>)Favorites;
        bool added = set.Add(item) || !set.Remove(item);
        Save("favorites.json", json =>
        {
            json.WriteStartArray();
            foreach (string id in set.Order(StringComparer.Ordinal)) json.WriteStringValue(id);
            json.WriteEndArray();
        });
        return added;
    }

    /// <summary>Adds a preset under a new, non-empty name; false when the name is empty or taken.</summary>
    public bool AddPreset(string name, Outfit outfit)
    {
        name = name.Trim();
        var list = (List<(string Name, Outfit Outfit)>)Presets;
        if (name.Length == 0 || list.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase))) return false;

        list.Add((name, outfit));
        return SavePresets(list);
    }

    public void DeletePreset(int index)
    {
        var list = (List<(string Name, Outfit Outfit)>)Presets;
        if (index < 0 || index >= list.Count) return;

        list.RemoveAt(index);
        SavePresets(list);
    }

    bool SavePresets(List<(string Name, Outfit Outfit)> list) => Save("presets.json", json =>
    {
        json.WriteStartArray();
        foreach (var (name, outfit) in list)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            OutfitJson.Write(json, outfit);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    });

    HashSet<string> ReadFavorites()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (Read("favorites.json") is { } doc)
        {
            using (doc) foreach (var e in doc.RootElement.EnumerateArray()) if (e.GetString() is { Length: > 0 } id) set.Add(id);
        }

        return set;
    }

    List<(string, Outfit)> ReadPresets()
    {
        var list = new List<(string, Outfit)>();
        if (Read("presets.json") is not { } doc) return list;

        using (doc)
        {
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name) list.Add((name, OutfitJson.Read(e)));
            }
        }

        return list;
    }

    JsonDocument? Read(string file)
    {
        string path = Path.Combine(_folder, file);
        try
        {
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            context.Log($"{file} not read: {e.Message}");
            return null;
        }
    }

    bool Save(string file, Action<Utf8JsonWriter> write)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            using var stream = File.Create(Path.Combine(_folder, file));
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            write(json);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"{file} not written: {e.Message}");
            return false;
        }
    }
}
