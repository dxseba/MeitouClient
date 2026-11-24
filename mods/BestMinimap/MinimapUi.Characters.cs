using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Meitou.BestMinimap;

/// <summary>
/// The characters as dots by who they are to the player (squad, allies, neutrals, enemies, animals; the dead dimmed), the selected bigger with a
/// white outline, a nose where each looks, bounties as a gold ring and the sum, names and the player's squad names over their leaders.
/// </summary>
sealed partial class MinimapUi
{
    enum Kind { Squad, Ally, Neutral, Enemy, Animal }

    readonly record struct Dot(Vector2 Screen, float Size, CharacterSnapshot Character);

    readonly List<Dot> _dots = [];

    void AddCharacters(UiNode body, Projection v, Vector2 origin, UiFrameContext frame)
    {
        _dots.Clear();
        if (Minimap.Snapshot is not { } snap) return;

        foreach (var c in snap.Characters)
        {
            if (c.Body?.Dead == true && !Minimap.Settings.ShowDead) continue;

            var kind = KindOf(c);
            if (!Shows(kind)) continue;

            var screen = v.ToScreen(new(c.Position.X, c.Position.Z));
            if (!v.Inside(screen, 1)) continue;

            AddCharacter(body, v, origin, c, kind, screen, frame);
        }

        AddSquadNames(body, v, origin, snap, frame);
    }

    void AddCharacter(UiNode body, Projection v, Vector2 origin, CharacterSnapshot c, Kind kind, Vector2 screen, UiFrameContext frame)
    {
        var s = Minimap.Settings;
        bool dead = c.Body?.Dead == true;
        float size = s.DotSize * (c.Selected ? 1.6f : 1);
        var colour = ColourOf(kind, c.Selected);
        if (dead) colour = Palette.Faded(colour * new Vector4(.6f, .6f, .6f, 1), .6f);

        if (c.Selected) body.Children.Add(Sprite(Sprites.Dot, screen, new(size + 2), Palette.White, origin));
        body.Children.Add(Sprite(Sprites.Dot, screen, new(size), colour, origin));
        if (s.ShowFacing && !dead) AddNose(body, v, origin, c, screen, size, colour);
        if (s.ShowBounty && Bounty(c) is > 0 and var bounty) AddBounty(body, origin, screen, size, bounty, frame);
        if (s.CharacterNames && c.Name.Length > 0)
            body.Children.Add(Label(c.Name, screen + new Vector2(0, size / 2 + 1), colour, LabelScale(frame) * .9f, origin));
        _dots.Add(new(screen, size, c));
    }

    void AddNose(UiNode body, Projection v, Vector2 origin, CharacterSnapshot c, Vector2 screen, float size, Vector4 colour)
    {
        var forward = new Vector2(MathF.Sin(c.Yaw), MathF.Cos(c.Yaw)) * (Minimap.Settings.FacingFlip ? -1 : 1);
        float angle = v.ScreenAngle(forward);
        var tip = screen + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * (size / 2 + 1);
        body.Children.Add(Sprite(Sprites.Arrow, tip, new(MathF.Max(4, size * .8f)), colour, origin, angle));
    }

    void AddBounty(UiNode body, Vector2 origin, Vector2 screen, float size, int bounty, UiFrameContext frame)
    {
        var colour = Palette.Parse(Minimap.Settings.BountyColour, Palette.Rgb(0xf2c230));
        body.Children.Add(Sprite(Sprites.Ring, screen, new(size + 6), colour, origin));
        body.Children.Add(Label($"{bounty} c", screen - new Vector2(0, size / 2 + 16), colour, LabelScale(frame) * .9f, origin));
    }

    /// <summary>The player's squads' names over their leaders (only "All squads": the snapshot names the player's squads alone).</summary>
    void AddSquadNames(UiNode body, Projection v, Vector2 origin, WorldSnapshot snap, UiFrameContext frame)
    {
        if (Minimap.Settings.SquadLabels != MinimapSettings.SquadLabelModes[2]) return;

        var colour = Palette.Parse(Minimap.Settings.SquadLabelColour, Palette.Rgb(0xe6c84a));
        foreach (var squad in snap.PlayerSquads)
        {
            if (squad.Members.Count == 0 || squad.Name.Length == 0) continue;
            if (snap.Characters.FirstOrDefault(c => c.Id == squad.Members[0]) is not { } leader) continue;

            var screen = v.ToScreen(new(leader.Position.X, leader.Position.Z));
            var top = screen - new Vector2(0, Minimap.Settings.DotSize + 18);
            if (v.Inside(screen)) body.Children.Add(Label(squad.Name, top, colour, LabelScale(frame), origin));
        }
    }

    static int Bounty(CharacterSnapshot c)
    {
        int sum = 0;
        foreach (var line in c.Bounties) sum += line.Amount;
        return sum;
    }

    static Kind KindOf(CharacterSnapshot c)
    {
        if (c.IsPlayer) return Kind.Squad;
        if (c.Animal) return Kind.Animal;
        if (c.HostileToPlayer) return Kind.Enemy;

        int mine = Minimap.PlayerFaction;
        return mine >= 0 && c.Faction >= 0 && Minimap.Population is { } data && data.Relations.IsAlly(mine, c.Faction) ? Kind.Ally : Kind.Neutral;
    }

    static bool Shows(Kind kind)
    {
        var s = Minimap.Settings;
        return kind switch
        {
            Kind.Squad => s.ShowSquad,
            Kind.Ally => s.ShowAllies,
            Kind.Neutral => s.ShowNeutrals,
            Kind.Enemy => s.ShowEnemies,
            _ => s.ShowAnimals,
        };
    }

    static Vector4 ColourOf(Kind kind, bool selected)
    {
        var s = Minimap.Settings;
        return kind switch
        {
            Kind.Squad => Palette.Parse(s.ColourSquad, selected ? Palette.Selected : Palette.Player),
            Kind.Ally => Palette.Parse(s.ColourAllies, Palette.Ally),
            Kind.Neutral => Palette.Parse(s.ColourNeutrals, Palette.Neutral),
            Kind.Enemy => Palette.Parse(s.ColourEnemies, Palette.Enemy),
            _ => Palette.Parse(s.ColourAnimals, Palette.Rgb(0xc8b48c)),
        };
    }
}
