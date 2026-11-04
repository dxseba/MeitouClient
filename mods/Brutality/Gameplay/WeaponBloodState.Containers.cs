using System.Globalization;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

public sealed partial class WeaponBloodState
{
    internal const string ContainerTokenPrefix = "brutality-item:v1:";
    string containerNamespace = Guid.NewGuid().ToString("N");
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
        if (!IsContainerToken(key) || !key.AsSpan(ContainerTokenPrefix.Length, 32).SequenceEqual(containerNamespace)) return;
        if (long.TryParse(key.AsSpan(ContainerTokenPrefix.Length + 33), NumberStyles.None, CultureInfo.InvariantCulture, out long serial))
            containerSerial = Math.Max(containerSerial, serial);
    }
}
