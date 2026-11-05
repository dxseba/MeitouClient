using System.Globalization;
using System.Text.Json;
using Meitou.Data.Save;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

public sealed partial class WeaponBloodState
{
    internal const string ContainerTokenPrefix = "brutality-item:v1:";
    const string EmptyNamespace = "00000000000000000000000000000000";
    string containerNamespace = EmptyNamespace;
    bool namespaceInitialized;
    readonly HashSet<string> reservedNamespaces = new(StringComparer.Ordinal);
    internal void ReserveContainerNamespaces(SaveGame save)
    {
        foreach (var (_, file) in save.AllFiles())
            foreach (var record in file.Records)
                foreach (var text in record.Strings.Values) ReserveContainerNamespaces(text);
    }
    internal void ReserveContainerNamespaces(string text)
    {
        if (!text.Contains(ContainerTokenPrefix, StringComparison.Ordinal)) return;
        try
        {
            using var document = JsonDocument.Parse(text);
            Reserve(document.RootElement);
        }
        catch (JsonException) { }
    }
    void Reserve(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) Reserve(item);
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var field in value.EnumerateObject())
            {
                if (field.NameEquals(CutEquipmentAdapter.ContainerTokenProperty) && field.Value.ValueKind == JsonValueKind.String
                    && field.Value.GetString() is { } token && IsContainerToken(token))
                    reservedNamespaces.Add(token.Substring(ContainerTokenPrefix.Length, 32));
                else if (field.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Reserve(field.Value);
            }
    }
    long containerSerial;
    internal string? ContainerToken(ItemInstance item)
    {
        if (!TryGet(item, out _)) return null;
        if (CutEquipmentAdapter.ItemKey(item) is { } existing && IsContainerToken(existing)) return existing;
        string key;
        do
        {
            if (containerSerial == long.MaxValue) throw new InvalidOperationException("Brutality item identity space exhausted.");
            key = ContainerTokenPrefix + containerNamespace + ":" + (++containerSerial).ToString(CultureInfo.InvariantCulture);
        }
        while (durable.ContainsKey(key));
        CutEquipmentAdapter.BindItemIdentity(item, key); AssignIdentity(item, key); return key;
    }
    internal static bool IsContainerToken(string? key) => key is not null && key.StartsWith(ContainerTokenPrefix, StringComparison.Ordinal)
        && key.Length <= 96 && key.AsSpan(ContainerTokenPrefix.Length).IndexOf(':') == 32
        && Guid.TryParseExact(key.AsSpan(ContainerTokenPrefix.Length, 32), "N", out _)
        && long.TryParse(key.AsSpan(ContainerTokenPrefix.Length + 33), NumberStyles.None, CultureInfo.InvariantCulture, out long serial) && serial > 0;
    void ObserveContainerSerial(string key)
    {
        if (!IsContainerToken(key)) return;
        reservedNamespaces.Add(key.Substring(ContainerTokenPrefix.Length, 32));
        if (!key.AsSpan(ContainerTokenPrefix.Length, 32).SequenceEqual(containerNamespace)) return;
        if (long.TryParse(key.AsSpan(ContainerTokenPrefix.Length + 33), NumberStyles.None, CultureInfo.InvariantCulture, out long serial))
            containerSerial = Math.Max(containerSerial, serial);
    }
}
