using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality;

/// <summary>Organic cut colours. Race fluid tints exposed tissue while bone and individual organs stay readable.</summary>
internal readonly record struct StockTissuePalette(uint Blood, bool Hiver, bool Shek, Vector4 Bowel)
{
    internal static StockTissuePalette For(CharacterAppearance look)
    {
        string race = look.Race.Name;
        uint blood = unchecked((uint)look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        return new(blood, race.Contains("Hive", StringComparison.OrdinalIgnoreCase),
            race.Contains("Shek", StringComparison.OrdinalIgnoreCase), new(BowelColour(race, blood), 1));
    }

    // FCS defines the fluid colour, not tissue. Keep bowel albedo muted and distinct even for species sharing red blood.
    static Vector3 BowelColour(string race, uint blood) => race switch
    {
        "Greenlander" => new(.34f, .24f, .23f),
        "Scorchlander" => new(.31f, .21f, .20f),
        "Cannibal" or "Cannibal Skav" => new(.32f, .23f, .20f),
        "Shek" => new(.31f, .26f, .285f),
        "Hive Queen" or "Hive Prince" or "Hive Worker Drone" or "Hive Soldier Drone" => new(.29f, .33f, .22f),
        "Hive Prince South Hive" or "Hive Worker Drone South Hive" or "Hive Soldier Drone South Hive" => new(.33f, .29f, .22f),
        "Deadhive Worker" or "Deadhive Soldier" => new(.31f, .33f, .29f),
        "Deadhive Prince" => new(.40f, .34f, .16f),
        "Fishman" or "Alpha Fishman" or "King Fishman" => new(.24f, .30f, .29f),
        _ => CustomBowelColour(blood),
    };

    static Vector3 CustomBowelColour(uint blood)
    {
        var tissue = new Vector3(.32f, .24f, .23f);
        if (blood == 0x800000) return tissue;
        var pigment = new Vector3(blood >> 16 & 255, blood >> 8 & 255, blood & 255) / 255f;
        float peak = MathF.Max(.18f, MathF.Max(pigment.X, MathF.Max(pigment.Y, pigment.Z)));
        return Vector3.Lerp(tissue, pigment * (.34f / peak), .32f);
    }

    internal Vector4 Bone => new(.66f, .55f, .39f, 1);
    internal Vector4 Marrow => Tint(new(.39f, .13f, .10f, 1), .72f);
    internal Vector4 Flesh => Tint(new(.43f, .072f, .066f, 1), .72f);
    internal Vector4 Muscle => Tint(new(.48f, .12f, .094f, 1), .64f);
    internal Vector4 Fat => Tint(new(.65f, .39f, .24f, 1), .26f);
    internal Vector4 Brain => Tint(new(.67f, .39f, .44f, 1), .40f);
    internal Vector4 Wet => Tint(new(.50f, .043f, .048f, 1), .90f);

    internal Vector4 Organ(int organ) => organ == 5 ? Bowel : Tint(organ switch
    {
        1 => new(.48f, .10f, .12f, 1), 2 => new(.38f, .15f, .17f, 1), 3 => new(.34f, .075f, .077f, 1),
        4 => new(.46f, .19f, .15f, 1), 6 => new(.28f, .086f, .078f, 1),
        _ => new(.43f, .14f, .13f, 1),
    }, .55f);

    internal Vector4 Tint(Vector4 colour, float strength)
    {
        Vector3 tissue = new(colour.X, colour.Y, colour.Z);
        // Hiver tissue is waxier even when its FCS blood colour matches a human's.
        if (Hiver) tissue *= new Vector3(.89f, 1.18f, .93f);
        else if (Shek) tissue *= new Vector3(.94f, .94f, 1.12f);
        if (Blood != 0x800000)
        {
            var pigment = new Vector3(Blood >> 16 & 255, Blood >> 8 & 255, Blood & 255) / 255f;
            float brightness = MathF.Max(tissue.X, MathF.Max(tissue.Y, tissue.Z));
            float peak = MathF.Max(.18f, MathF.Max(pigment.X, MathF.Max(pigment.Y, pigment.Z)));
            var tinted = pigment * (brightness / peak);
            tissue = Vector3.Lerp(tissue, tinted, Math.Clamp(strength, 0, 1));
        }
        return new(Vector3.Clamp(tissue, Vector3.Zero, Vector3.One), colour.W);
    }
}
