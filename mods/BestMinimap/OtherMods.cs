using System.Collections;
using System.Numerics;
using System.Reflection;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.BestMinimap;

/// <summary>A mark of another mod: its world point (X, Z), colour, short glyph (Map-markers' type letter, "" none) and label.</summary>
readonly record struct OtherMark(Vector2 At, Vector4 Colour, string Glyph, string Label);

/// <summary>
/// The marks of the Map-markers and WantedMap ports, optional dependencies (the original reads Map-markers' <c>Map-markers.json</c> of the current
/// save and WantedMap's <c>WantedMap_GetMarkers</c> export). Read by reflection so the port builds and runs without them: Map-markers'
/// <c>MapMarkersMod.Running.Snapshot()</c> (its map points 0..1, its kinds' colours and glyphs from <c>MarkerKinds</c>), WantedMap's public
/// <c>WantedMapMod.GetMarkers()</c> (as of the last look at the big map, as the original). Host thread.
/// </summary>
sealed class OtherMods
{
    readonly Action<string> _log;
    readonly Func<IEnumerable?>? _mapMarkers;
    readonly Func<IEnumerable?>? _wanted;
    readonly MethodInfo? _colour, _glyph;
    bool _failed;

    public OtherMods(ModContext context)
    {
        _log = context.Log;
        if (context.HasDependency("dxseba.mapmarkers") && context.Dependency<IMod>("dxseba.mapmarkers") is { } markers)
        {
            var assembly = markers.GetType().Assembly;
            var running = markers.GetType().GetField("Running", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var kinds = assembly.GetType("Meitou.MapMarkers.MarkerKinds");
            _colour = kinds?.GetMethod("Colour", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _glyph = kinds?.GetMethod("Glyph", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (running is not null) _mapMarkers = () => Snapshot(running.GetValue(null));
        }

        if (context.HasDependency("dxseba.wantedmap") && context.Dependency<IMod>("dxseba.wantedmap") is { } wanted
            && wanted.GetType().GetMethod("GetMarkers") is { } get)
            _wanted = () => get.Invoke(wanted, null) as IEnumerable;
    }

    /// <summary>Map-markers' markers; none without it.</summary>
    public List<OtherMark> MapMarkers() => Read(_mapMarkers, FromMapMarker);

    /// <summary>WantedMap's targets; none without it.</summary>
    public List<OtherMark> Wanted() => Read(_wanted, FromWanted);

    List<OtherMark> Read(Func<IEnumerable?>? source, Func<object, OtherMark> convert)
    {
        var list = new List<OtherMark>();
        if (_failed || source is null) return list;

        try
        {
            if (source() is { } items)
            {
                foreach (object item in items) list.Add(convert(item));
            }
        }
        catch (Exception e)
        {
            _failed = true;
            _log($"reading the other mods' marks failed, left off for the session: {e.Message}");
        }

        return list;
    }

    static IEnumerable? Snapshot(object? book) => book?.GetType().GetMethod("Snapshot")?.Invoke(book, null) as IEnumerable;

    OtherMark FromMapMarker(object marker)
    {
        var type = marker.GetType();
        var at = (Vector2)type.GetProperty("At")!.GetValue(marker)!;
        object kind = type.GetProperty("Kind")!.GetValue(marker)!;
        string label = type.GetProperty("Label")!.GetValue(marker) as string ?? "";
        var colour = _colour?.Invoke(null, [kind, false]) is Vector4 c ? c : Vector4.One;
        string glyph = _glyph?.Invoke(null, [kind]) as string ?? "";
        return new(UiWorldMapView.MapToWorld(at), colour, glyph, label);
    }

    static OtherMark FromWanted(object marker)
    {
        var type = marker.GetType();
        var at = (Vector2)type.GetProperty("At")!.GetValue(marker)!;
        var colour = (Vector4)type.GetProperty("Colour")!.GetValue(marker)!;
        string label = type.GetProperty("Label")!.GetValue(marker) as string ?? "";
        return new(at, colour, "", label);
    }
}
