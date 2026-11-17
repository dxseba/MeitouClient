using Meitou.Simulation.Aggro;
using Meitou.Game.Host;
using System.Collections.Frozen;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CharacterHighlight;

/// <summary>
/// Recolours the outlines the held Highlight key (Alt) draws round nearby characters by relation: blue the player's, red hostile, green allied, yellow
/// neutral. Uses the game's own hover outline (Settings: Hover outline on); the character under the mouse keeps its pointer colour.
/// </summary>
public sealed class CharacterHighlightMod : IMod
{
    static readonly Vector4 Blue = new(.30f, .55f, 1, .9f), Red = new(1, .22f, .18f, .9f), Green = new(.30f, .95f, .35f, .9f), Yellow = new(1, .88f, .25f, .9f);
    static FieldInfo? s_sessionField;
    static PropertyInfo? s_hoverProperty;
    static FieldInfo? s_playerField;

    public void Initialize(ModContext context)
    {
        context.AddSystem("relations", build => build.Find<AggroSystem>() is { } aggro ? new RelationSystem(aggro.Relations) : null!, SystemAnchor.End);

        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(type.FullName, "WorldSession");
        var pointer = AccessTools.TypeByName("Meitou.Game.Ui.PointerView") ?? throw new TypeLoadException("Meitou.Game.Ui.PointerView");
        s_playerField = pointer.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(f => f.FieldType == type)
            ?? throw new MissingFieldException(pointer.FullName, "PlayerInterface");
        s_hoverProperty = AccessTools.Property(pointer, "HoverTarget");
        var targets = AccessTools.Method(pointer, "HighlightTargets") ?? throw new MissingMethodException(pointer.FullName, "HighlightTargets");
        context.Harmony.Patch(targets, postfix: new HarmonyMethod(typeof(CharacterHighlightMod), nameof(Recolour)));
        context.Log("Character Highlight initialized");
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Recolour(object __instance, List<(CharacterId Id, Vector4 Colour)> __result)
    #pragma warning restore IDE1006
    {
        if (s_sessionField?.GetValue(s_playerField?.GetValue(__instance)) is not WorldSession session) return;
        var kinds = session.World.Systems.OfType<RelationSystem>().FirstOrDefault()?.Published ?? FrozenDictionary<int, Relation>.Empty;
        var hovered = (s_hoverProperty?.GetValue(__instance) as (CharacterId Id, Vector4 Colour)?)?.Id;
        var byId = session.CurrentSnapshot.Characters.ToDictionary(c => c.Id);

        for (int i = 0; i < __result.Count; i++)
        {
            var (id, colour) = __result[i];
            if (id == hovered || !byId.TryGetValue(id, out var c)) continue;
            __result[i] = (id, Colour(c, kinds) ?? colour);
        }
    }

    static Vector4? Colour(CharacterSnapshot c, FrozenDictionary<int, Relation> kinds)
    {
        if (c.IsPlayer) return Blue;
        if (c.HostileToPlayer) return Red;
        return kinds.GetValueOrDefault(c.Faction) switch
        {
            Relation.Ally => Green,
            Relation.Hostile => Red,
            _ => Yellow,
        };
    }
}

public enum Relation : byte { Neutral, Ally, Hostile }

/// <summary>Publishes each faction's standing with the player's faction about once a second (read in the tick, shown by the interface).</summary>
sealed class RelationSystem(FactionRelations relations) : ITickSystem
{
    volatile FrozenDictionary<int, Relation> _published = FrozenDictionary<int, Relation>.Empty;
    public FrozenDictionary<int, Relation> Published => _published;

    public void SlowWorld(World world)
    {
        if (world.Tick % 30 != 0 || !world.Player.Exists) return;

        int mine = world.Player.Faction;
        var map = new Dictionary<int, Relation>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Faction: >= 0 } cold || map.ContainsKey(cold.Faction)) continue;
            map[cold.Faction] = relations.IsAlly(mine, cold.Faction) ? Relation.Ally : relations.IsHostile(mine, cold.Faction) ? Relation.Hostile : Relation.Neutral;
        }
        _published = map.ToFrozenDictionary();
    }
}
