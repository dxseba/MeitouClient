using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;

namespace Meitou.Brutality;

/// <summary>Resolves the feature owners extracted from the master client host.</summary>
internal static class StockHostParts
{
    static readonly Type host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
    static readonly ConcurrentDictionary<Type, FieldInfo> owners = new();
    static readonly FieldInfo saves = Field(host, "Saves"), epoch = Field(saves.FieldType, "WorldEpoch");

    internal static readonly FieldInfo Decapitation = Field(host, "Decapitation");
    internal static readonly FieldInfo Debris = Field(host, "Debris");
    internal static readonly FieldInfo GroundItems = Field(host, "GroundItemSync");

    internal static int WorldEpoch(object instance) => (int)epoch.GetValue(saves.GetValue(instance))!;

    internal static object? HostOf(object feature) => owners.GetOrAdd(feature.GetType(), static type => Field(type, "<host>P")).GetValue(feature);

    internal static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

    internal static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);
}
