namespace Meitou.KenshiAffixes;

/// <summary>
/// The shared <c>ModTag</c> format of dxseba's item mods, so several can mark one item: <c>dxseba;key=value;key=value</c>. An empty tag is free;
/// a tag in any other format belongs to another mod and is left alone. (Same code in each mod: they ship as separate assemblies.)
/// </summary>
static class ItemTag
{
    const string Head = "dxseba";

    /// <summary>Whether this format may write the tag (empty, or already in it).</summary>
    public static bool Ours(string tag) => tag.Length == 0 || tag == Head || tag.StartsWith(Head + ";", StringComparison.Ordinal);

    public static string? Get(string tag, string key)
    {
        if (!Ours(tag)) return null;

        foreach (var part in tag.Split(';').Skip(1))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && part.AsSpan(0, eq).SequenceEqual(key)) return part[(eq + 1)..];
        }

        return null;
    }

    /// <summary>The tag with <paramref name="key"/> set to <paramref name="value"/>; the tag unchanged when it is another mod's.</summary>
    public static string Put(string tag, string key, string value)
    {
        if (!Ours(tag)) return tag;

        var parts = tag.Split(';', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Where(p => !p.StartsWith(key + "=", StringComparison.Ordinal)).ToList();
        parts.Add(key + "=" + value);
        parts.Sort(StringComparer.Ordinal);
        return Head + ";" + string.Join(';', parts);
    }

    /// <summary>The tag without <paramref name="key"/>; the tag unchanged when it is another mod's or has no such key.</summary>
    public static string Remove(string tag, string key)
    {
        if (!Ours(tag) || Get(tag, key) is null) return tag;

        var parts = tag.Split(';', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(p => !p.StartsWith(key + "=", StringComparison.Ordinal));
        return Head + ";" + string.Join(';', parts);
    }
}
