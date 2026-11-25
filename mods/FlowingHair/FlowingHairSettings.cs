using Meitou.Modding;

namespace Meitou.FlowingHair;

/// <summary>
/// The original's <c>FlowingHair.ini</c> keys, under Mod settings, with the values its Workshop release ships (sections <c>[FlowingHair]</c> and
/// <c>[FlowingWind]</c>); the first five are the rows it adds to the game's graphics options, with their labels and hints. Read live by the
/// renderer patches: a change applies from the next frame.
/// </summary>
public sealed class FlowingHairSettings
{
    [Setting("Enabled", Hint = "Hair and beard physics on or off.")]
    public bool Enabled { get; set; } = true;

    [Setting("Non PC Characters Have Hair Physics",
        Hint = "Enable hair and beard physics for loaded, visible NPCs within Flowing Hair Distance. Disable to reduce CPU use.")]
    public bool SimulateNpcs { get; set; } = true;

    [Setting("Flowing Hair Distance", Hint = "Maximum camera distance in world units for player and NPC hair/beard physics. Default 500, maximum 2000. " +
        "0 disables simulation. Distant hair renders normally. Activation starts 10% inside the limit to prevent repeated switching.",
        Min = 0, Max = 2000, Step = 10)]
    public float Distance { get; set; } = 500;

    [Setting("Flowing Hair Character Limit", Hint = "Maximum characters running physics at once. Nearest visible eligible characters take priority " +
        "regardless of player/NPC status. Scalp and beard count together as one.", Min = 1, Max = 100, Step = 1)]
    public int CharacterLimit { get; set; } = 4;

    [Setting("Hair Gravity", Hint = "Downward pull on hair and beards; 0 removes gravity. Rain adds wet weight. Wet hair gradually dries after exposure.",
        Min = 0, Max = 30, Step = 0.1)]
    public float Gravity { get; set; } = 15.06f;

    [Setting("Hair Rigidity", Hint = "Hair and beard resistance to bending: 0 softer, 100 stiffer. 50 is the original feel. Roots remain attached.",
        Min = 0, Max = 100, Step = 1)]
    public float Rigidity { get; set; } = 35;

    [Setting("Inertia", Hint = "How much the character's own acceleration throws the hair (Inertia).", Min = 0, Max = 0.5, Step = 0.005,
        SubGroup = "Physics")]
    public float Inertia { get; set; } = 0.035f;

    [Setting("Movement Drag", Hint = "Airflow drag from the character's own movement (MovementDrag).", Min = 0, Max = 50, Step = 0.5,
        SubGroup = "Physics")]
    public float MovementDrag { get; set; } = 12;

    [Setting("Maximum Movement Acceleration", Hint = "Cap on the movement airflow (MaximumMovementAcceleration).", Min = 0, Max = 300, Step = 5,
        SubGroup = "Physics")]
    public float MaximumMovementAcceleration { get; set; } = 120;

    [Setting("Maximum Acceleration", Hint = "Cap on gravity, wind and inertia together (MaximumAcceleration).", Min = 1, Max = 200, Step = 1,
        SubGroup = "Physics")]
    public float MaximumAcceleration { get; set; } = 40;

    [Setting("Damping", Hint = "Velocity lost each step (Damping).", Min = 0, Max = 0.5, Step = 0.005, SubGroup = "Physics")]
    public float Damping { get; set; } = 0.035f;

    [Setting("Bending Stiffness", Hint = "Resistance of the strands to folding (BendingStiffness).", Min = 0, Max = 1, Step = 0.01, SubGroup = "Physics")]
    public float Bending { get; set; } = 0.45f;

    [Setting("Shape Stiffness", Hint = "Pull back to the authored hair shape each step (ShapeStiffness).", Min = 0, Max = 0.25, Step = 0.001,
        SubGroup = "Physics")]
    public float ShapeStiffness { get; set; } = 0.018f;

    [Setting("Beard Shape Stiffness", Hint = "The same pull for beards (BeardShapeStiffness).", Min = 0.001, Max = 0.25, Step = 0.0005,
        SubGroup = "Physics")]
    public float BeardShapeStiffness { get; set; } = 0.0045f;

    [Setting("Constraint Iterations", Hint = "Solver passes per step (ConstraintIterations).", Min = 1, Max = 32, Step = 1, SubGroup = "Physics")]
    public int Iterations { get; set; } = 8;

    [Setting("Face Collision", Hint = "Keep hair and beards out of the head (FaceCollision).", SubGroup = "Physics")]
    public bool FaceCollision { get; set; } = true;

    [Setting("Wet Hair", Hint = "Rain soaks hair, making it heavier and less wind-blown (WetHair).", SubGroup = "Physics")]
    public bool WetHair { get; set; } = true;

    [Setting("Khajiit Tails", Hint = "Tail physics for the Khajiit race mod's bodies (KhajjitTails).", SubGroup = "Physics")]
    public bool KhajjitTails { get; set; } = true;

    [Setting("Cat Tails", Hint = "Tail physics for The Cat Mod's cats (CatTails).", SubGroup = "Physics")]
    public bool CatTails { get; set; } = true;

    [Setting("Stress Wind Key", Hint = "The debug key (P by default, under Controls) toggles a maximum steady wind (EnableStressWindHotkey).",
        SubGroup = "Physics")]
    public bool StressWindKey { get; set; } = true;

    [Setting("Stress Wind Acceleration", Hint = "The debug key's steady wind (StressWindAcceleration).", Min = 40, Max = 1000, Step = 10,
        SubGroup = "Physics")]
    public float StressAcceleration { get; set; } = 240;

    [Setting("Wind", Hint = "Wind blows hair and beards ([FlowingWind] Enabled).", SubGroup = "Wind")]
    public bool Wind { get; set; } = true;

    [Setting("Follow Weather", Hint = "Use the weather's wind; off uses the procedural wind below (FollowWeather).", SubGroup = "Wind")]
    public bool FollowWeather { get; set; } = true;

    [Setting("Weather Wind Multiplier", Hint = "Weather wind speed to hair acceleration (WeatherWindMultiplier).", Min = 0, Max = 10, Step = 0.1,
        SubGroup = "Wind")]
    public float WeatherMultiplier { get; set; } = 2;

    [Setting("Weather Gust Fraction", Hint = "How much the weather wind gusts (WeatherGustFraction).", Min = 0, Max = 1, Step = 0.01, SubGroup = "Wind")]
    public float WeatherGust { get; set; } = 0.25f;

    [Setting("Maximum Weather Wind", Hint = "Cap on the weather wind (MaximumWeatherWind).", Min = 0, Max = 200, Step = 1, SubGroup = "Wind")]
    public float WeatherMaximum { get; set; } = 60;

    [Setting("Base Wind", Hint = "Procedural wind's steady part (BaseWind).", Min = 0, Max = 100, Step = 1, SubGroup = "Wind")]
    public float BaseWind { get; set; } = 15;

    [Setting("Gust Wind", Hint = "Procedural wind's gusts (GustWind).", Min = 0, Max = 150, Step = 1, SubGroup = "Wind")]
    public float GustWind { get; set; } = 45;

    [Setting("Gust Frequency", Hint = "Gusts per second (GustFrequency).", Min = 0.005, Max = 1, Step = 0.005, SubGroup = "Wind")]
    public float GustFrequency { get; set; } = 0.04f;

    [Setting("Direction Degrees", Hint = "Procedural wind direction (DirectionDegrees).", Min = -360, Max = 360, Step = 1, SubGroup = "Wind")]
    public float Direction { get; set; } = 35;

    [Setting("Direction Variation", Hint = "How far the procedural wind's direction wanders (DirectionVariation).", Min = 0, Max = 180, Step = 1,
        SubGroup = "Wind")]
    public float DirectionVariation { get; set; } = 75;

    [Setting("Rain And Dust", Hint = "The wind also pushes rain, dust and sandstorm particles (RainAndDust).", SubGroup = "Wind")]
    public bool RainAndDust { get; set; } = true;

    [Setting("Particle Wind Scale", Hint = "How hard the wind pushes those particles (ParticleWindScale).", Min = 0, Max = 10, Step = 0.1,
        SubGroup = "Wind")]
    public float ParticleWindScale { get; set; } = 1;
}
