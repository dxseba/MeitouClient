using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.BestMinimap;

/// <summary>
/// Places (the discovered towns, the known camps and nests, the player's outposts, as the big map knows them), the camera's reach, the other mods'
/// marks, the move flag, the alert rings and the camera arrow.
/// </summary>
sealed partial class MinimapUi
{
    /// <summary>A place on the minimap, for its tooltip.</summary>
    readonly record struct Place(Vector2 Screen, float Size, string Name, string? Faction);

    /// <summary>Most dots of the camera's reach in all (the original caps its circles at 20000 samples).</summary>
    const int MaxReachDots = 600;

    readonly List<Place> _places = [];

    void AddPlaces(UiNode body, Projection v, Vector2 origin, UiFrameContext frame)
    {
        _places.Clear();
        if (Minimap.Snapshot is not { } snap) return;

        if (Minimap.Population is { } data)
        {
            for (int i = 0; i < data.Sites.Count && i < snap.Towns.Count; i++)
            {
                var town = data.Sites[i].Town;
                if ((snap.Towns[i] & TownKnowledge.Discovered) == 0 || town.Type is TownType.NestMarker or TownType.Null) continue;

                bool explored = (snap.Towns[i] & TownKnowledge.Explored) != 0;
                string name = explored ? town.ShownName() : town.ShownName(unexplored: true);
                AddPlace(body, v, origin, frame, data.Sites[i].Position, town.Type, name, town.Faction, explored, town.Radius);
            }
        }

        foreach (var camp in snap.Camps)
        {
            if ((camp.Known & TownKnowledge.Discovered) != 0 && camp.Position is { } at)
                AddPlace(body, v, origin, frame, at, TownType.Nest, camp.ShownName(), camp.FactionId, true, 0, camp.Wildlife);
        }

        foreach (var outpost in snap.Outposts)
            AddPlace(body, v, origin, frame, outpost.Position, TownType.Outpost, outpost.Name, null, true, outpost.Radius, player: true);
    }

    void AddPlace(UiNode body, Projection v, Vector2 origin, UiFrameContext frame, Vector3 at, TownType type, string name, string? faction,
        bool explored, float radius, bool wild = false, bool player = false)
    {
        var s = Minimap.Settings;
        var screen = v.ToScreen(new(at.X, at.Z));
        float iconSize = s.TownIconSize;
        if (s.TownRadius && radius > 0) AddTownRadius(body, v, origin, screen, radius);
        if (!v.Inside(screen, -iconSize / 2)) return;

        var colour = TownColour(faction, explored, player);
        var icon = Sprite(Sprites.Diamond, screen, new(iconSize * .6f), colour, origin);
        if (s.TownIcons == MinimapSettings.IconStyles[0] && ImagePatch.Active)
            icon = ImagePatch.MapIcon(Sprite(Sprites.Diamond, screen, new(iconSize), colour, origin), Group(type, wild));
        body.Children.Add(icon);
        _places.Add(new(screen, iconSize, name, faction));

        if (!s.TownNames || s.Range > s.TownNamesRange || name.Length == 0) return;

        float scale = s.TownNameSize > 0 ? s.TownNameSize / 22f : LabelScale(frame);
        body.Children.Add(Label(name, screen + new Vector2(0, iconSize / 2 + 1), colour, scale, origin));
    }

    void AddTownRadius(UiNode body, Projection v, Vector2 origin, Vector2 screen, float radius)
    {
        float d = radius * 2 * v.Scale;
        if (d < 6 || d > 4 * v.Rect.Width + 4 * v.Rect.Height) return;

        body.Children.Add(Sprite(Sprites.ThinRing, screen, new(d), new(1, 1, 1, .35f), origin));
    }

    /// <summary>The big map's marker group of a place type (as Meitou's world map picks it).</summary>
    static string Group(TownType type, bool wild) => type switch
    {
        TownType.Nest => wild ? "Nest" : "Camp",
        TownType.Outpost or TownType.Military or TownType.Prison => "Outpost",
        TownType.Town => "Town",
        TownType.Village => "Village",
        TownType.Ruins => "Ruin",
        TownType.SlaveCamp => "Workcamp",
        _ => "SmallPlace",
    };

    /// <summary>The town colour: the setting, else by the place faction's standing with the player's (unexplored: neutral).</summary>
    static Vector4 TownColour(string? faction, bool explored, bool player)
    {
        var s = Minimap.Settings;
        if (Palette.Parse(s.TownColour, -Vector4.One) is { X: >= 0 } fixedColour) return fixedColour;
        if (player) return Palette.Player;
        if (!explored || faction is null || Minimap.Population is not { } data) return Palette.Neutral;

        return Standing(data.FactionIndex(faction));
    }

    /// <summary>A faction's colour by its standing with the player's (the big map's rule: own, ally, below -30 hostile, else neutral).</summary>
    static Vector4 Standing(int faction)
    {
        int mine = Minimap.PlayerFaction;
        if (faction < 0 || mine < 0 || Minimap.Population is not { } data) return Palette.Neutral;
        if (faction == mine) return Palette.Player;
        if (data.Relations.IsAlly(mine, faction)) return Palette.Ally;

        return data.Relations.Get(mine, faction) < -30 ? Palette.Enemy : Palette.Neutral;
    }

    /// <summary>
    /// The camera's reach: a circle of <see cref="Minimap.CameraReach"/> round each player character, without the parts inside another's
    /// (the outline of where the camera may go), dotted or solid.
    /// </summary>
    void AddCameraReach(UiNode body, Projection v, Vector2 origin)
    {
        var s = Minimap.Settings;
        var anchors = Minimap.PlayerCharacters().Select(c => new Vector2(c.Position.X, c.Position.Z)).Distinct().Take(64).ToList();
        if (anchors.Count == 0) return;

        float reach = Minimap.CameraReach, width = s.CameraLineWidth;
        float gap = s.CameraRangeStyle == MinimapSettings.LineStyles[0] ? width * 3 : MathF.Max(1, width * .7f);
        int perCircle = (int)Math.Clamp(2 * MathF.PI * reach * v.Scale / gap, 8, MaxReachDots);
        var colour = Palette.Parse(s.CameraRangeColour, Palette.White);
        int dots = 0;
        foreach (var a in anchors)
        {
            for (int k = 0; k < perCircle && dots < MaxReachDots; k++)
            {
                float t = k * 2 * MathF.PI / perCircle;
                var p = a + new Vector2(MathF.Cos(t), MathF.Sin(t)) * reach;
                if (anchors.Any(b => b != a && Vector2.DistanceSquared(p, b) < reach * reach * .999f)) continue;

                var screen = v.ToScreen(p);
                if (!v.Inside(screen, width / 2)) continue;

                body.Children.Add(Sprite(Sprites.Dot, screen, new(width), colour, origin));
                dots++;
            }
        }
    }

    /// <summary>Another mod's marks: a dot in its colour with its glyph, pinned to the edge pointing the way when out of range.</summary>
    void AddMarks(UiNode body, Projection v, Vector2 origin, List<OtherMark> marks, UiFrameContext frame)
    {
        var s = Minimap.Settings;
        float size = MathF.Max(8, s.DotSize * 2);
        foreach (var m in marks)
        {
            var screen = v.ToScreen(m.At);
            if (!v.Inside(screen, size / 2))
            {
                if (!s.PinToEdge) continue;

                var pinned = v.Pin(screen, size / 2 + 1);
                float angle = MathF.Atan2(screen.X - pinned.X, pinned.Y - screen.Y);
                body.Children.Add(Sprite(Sprites.Arrow, pinned, new(size), m.Colour, origin, angle));
                continue;
            }

            body.Children.Add(Sprite(Sprites.Diamond, screen, new(size), m.Colour, origin));
            string text = m.Glyph.Length > 0 && m.Label.Length > 0 ? $"{m.Glyph} {m.Label}" : m.Glyph + m.Label;
            if (text.Length > 0 && s.Range <= s.TownNamesRange)
                body.Children.Add(Label(text, screen + new Vector2(0, size / 2), m.Colour, LabelScale(frame), origin));
        }
    }

    /// <summary>The green flag where a right click sent the selected, for 3 seconds (the big map's movement marker).</summary>
    void AddMoveFlag(UiNode body, Projection v, Vector2 origin)
    {
        if (Minimap.MoveFlag is not { } at || Minimap.Now - Minimap.MoveFlagAt > 3) return;

        var screen = v.ToScreen(at);
        if (!v.Inside(screen)) return;

        var flag = Sprite(Sprites.Diamond, screen, new(14), Palette.Movement, origin);
        body.Children.Add(ImagePatch.Active ? ImagePatch.MapIcon(flag, "Movement") : flag);
    }

    /// <summary>A pulsing ring where each alert is (at the edge, pointing the way, when out of range).</summary>
    void AddAlerts(UiNode body, Projection v, Vector2 origin)
    {
        var colour = Palette.Parse(Minimap.Settings.AlertColour, Palette.Enemy);
        foreach (var (at, age) in Alerts.Current)
        {
            float pulse = (float)(age % 1);
            float size = 12 + 18 * pulse;
            var screen = v.ToScreen(at);
            var tint = Palette.Faded(colour, 1 - pulse * .7f);
            if (v.Inside(screen))
            {
                body.Children.Add(Sprite(Sprites.Ring, screen, new(size), tint, origin));
                continue;
            }

            var pinned = v.Pin(screen, 8);
            float angle = MathF.Atan2(screen.X - pinned.X, pinned.Y - screen.Y);
            body.Children.Add(Sprite(Sprites.Arrow, pinned, new(14), colour, origin, angle));
            body.Children.Add(Sprite(Sprites.Ring, pinned, new(size * .7f), tint, origin));
        }
    }

    /// <summary>The camera arrow at the camera's target: the way the camera looks (up while the map turns with it).</summary>
    void AddArrow(UiNode body, Projection v, Vector2 origin)
    {
        var s = Minimap.Settings;
        var at = v.ToScreen(new(Minimap.CameraTarget.X, Minimap.CameraTarget.Z));
        if (!v.Inside(at)) return;

        float angle = v.ScreenAngle(Minimap.Heading);
        float size = Math.Clamp(s.CameraArrowSize, 4, 64);
        body.Children.Add(Sprite(Sprites.Arrow, at, new(size), Palette.Parse(s.ViewColour, Palette.White), origin, angle));
    }
}
