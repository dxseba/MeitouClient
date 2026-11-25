using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;

namespace Meitou.DirectorPanel;

/// <summary>The Body and Effects tabs' world side: strikes, states, hunger, limbs, blood on the ground, animal age and the squad heal.</summary>
internal sealed partial class DirectorSystem
{
    /// <summary>"Coma until healed": a knock-out this long (game seconds), ended by a heal (port choice: the original's call is not known).</summary>
    const float ComaSeconds = 1_000_000;

    /// <summary>"Near death": every part left at this share of its maximum and the character knocked out (port choice).</summary>
    const float NearDeathShare = 0.02f, NearDeathSeconds = 120;

    void ApplyBody(World world, DirectorCommand command)
    {
        switch (command)
        {
            case Strike strike:
                StrikePart(world, strike);
                break;
            case SetBodyState state:
                SetState(world, state);
                break;
            case SetHunger hunger:
                Feed(world, hunger);
                break;
            case SetLimb limb:
                Limb(world, limb);
                break;
            case Splatter splatter:
                Splash(world, splatter);
                break;
            case HealSquads:
                Say("Characters healed: %d", PlayerCharacters(world).Count(id => Heal(world, id)));
                break;
            case SetAnimalAge age:
                if (Cold(world, age.Character, out _) is { Animal: not null } animal) BodySystem.SetAnimalAge(animal, age.Age, refreshLook: true);
                break;
        }
    }

    void StrikePart(World world, Strike strike)
    {
        if (Cold(world, strike.Character, out int slot) is not { Medical: { } medical, Race: { } race } cold
            || IndexOf(medical, strike.Part) is not (var part and >= 0))
        {
            Say("Body part not found");
            return;
        }

        var events = new List<MedicalEvent>();
        medical.ApplyHit(part, new HitDamage(strike.Cut, strike.Blunt, 0), Context(world, slot, cold, race), events);
        Settle(world, slot, cold, medical, events);
        Say("Strike on '%s': %s %.0f", medical.Parts[part].Template.Name, strike.Blunt > 0 ? "blunt" : "cutting", strike.Cut + strike.Blunt);
    }

    /// <summary>What a hit's events need beyond the medical state: one that goes down stands still; a cut-off limb drops and the kit follows.</summary>
    void Settle(World world, int slot, CharacterCold cold, MedicalState medical, List<MedicalEvent> events)
    {
        var table = world.Characters;
        foreach (var e in events)
        {
            if (e.Kind is MedicalEventKind.Died or MedicalEventKind.KnockedOut)
            {
                TaskStart.Stand(table, slot);
                table.Next[slot].Velocity = Vector3.Zero;
            }
            else if (e.Kind == MedicalEventKind.PartSevered)
            {
                table.LimbsChanged.Enqueue((slot, medical.Parts[e.Part].Template.LimbSlot, true));
                CharacterRefresh.Refresh(data, cold, CharacterRefreshKind.Equipment);
            }
        }
    }

    void SetState(World world, SetBodyState set)
    {
        if (Cold(world, set.Character, out int slot) is not { Medical: { } medical, Race: { } race } cold) return;

        var events = new List<MedicalEvent>();
        switch (set.State)
        {
            case BodyState.KnockOut:
                medical.KnockOut(set.Seconds, events);
                Say("Knocked out for %.0f seconds", set.Seconds);
                break;
            case BodyState.Coma:
                medical.KnockOut(ComaSeconds, events);
                Say("Coma until healed");
                break;
            case BodyState.NearDeath:
                foreach (var p in medical.Parts) p.Flesh = Math.Min(p.Flesh, p.MaxHp * NearDeathShare);
                medical.KnockOut(NearDeathSeconds, events);
                Say("Near death");
                break;
            case BodyState.Heal:
                if (Heal(world, set.Character)) Say("Fully healed");
                return;
        }

        Settle(world, slot, cold, medical, events);
    }

    /// <summary>"Heal everything": limbs back, every part whole, blood full, awake (the game's own full heal).</summary>
    bool Heal(World world, CharacterId id)
    {
        if (Cold(world, id, out int slot) is not { Medical: { Dead: false } medical, Race: { } race } cold) return false;

        if (medical.RestoreLimbs()) CharacterRefresh.Refresh(data, cold, CharacterRefreshKind.Equipment);
        medical.Heal(Context(world, slot, cold, race));
        return true;
    }

    /// <summary>Hunger in HUD units (the stat times 100), kept above zero: zero is death by starvation.</summary>
    void Feed(World world, SetHunger set)
    {
        if (Cold(world, set.Character, out _) is not { Medical: { Dead: false } medical }) return;

        medical.Hunger = Math.Clamp(set.Hud, 1, MedicalState.MaxHunger * 100) / 100;
        medical.Fed = 0;
        Say("Hunger set");
    }

    void Limb(World world, SetLimb set)
    {
        if (Cold(world, set.Character, out int slot) is not { Medical: { Dead: false } medical, Race: { } race } cold
            || IndexOf(medical, set.Part) is not (var index and >= 0))
            return;

        var part = medical.Parts[index];
        if (set.Sever)
        {
            var events = new List<MedicalEvent>();
            medical.Sever(index, Context(world, slot, cold, race), events);
            Settle(world, slot, cold, medical, events);
            Say("%s: severed", part.Template.Name);
            return;
        }

        part.Limb = LimbState.Original;
        part.Flesh = part.MaxHp;
        world.Characters.LimbsChanged.Enqueue((slot, part.Template.LimbSlot, false));
        CharacterRefresh.Refresh(data, cold, CharacterRefreshKind.Equipment);
        Say("%s: restored", part.Template.Name);
    }

    /// <summary>Splashes of blood on the ground round the character, flung away from the hit's direction; health untouched.</summary>
    void Splash(World world, Splatter splatter)
    {
        if (world.BloodMarks is not { } marks || Cold(world, splatter.Character, out int slot) is not { } cold) return;

        var hot = world.Characters.Next[slot];
        uint colour = cold.Race is { } race ? race.BloodColour : 0x800000;
        var look = SplashLook(splatter);
        for (int i = 0; i < Math.Clamp(splatter.Times, 1, 50); i++)
        {
            float roll = Rng.Float(Rng.Hash(world.Seed, (ulong)world.Tick, Purpose, (ulong)i)) - 0.5f;
            float spread = hot.Yaw + look.Turn + roll * look.Spread + (look.BothSides && i % 2 == 1 ? MathF.PI : 0);
            var away = new Vector2(MathF.Sin(spread), MathF.Cos(spread));
            var kind = look.Pool ? BloodMarkKind.Pool : BloodMarkKind.Splash;
            marks.Enqueue(new BloodMark(kind, splatter.Character, hot.Position, away, Math.Max(0.1f, splatter.Power / 100 * look.Size), colour));
        }

        Say("Splatter: %s, %s, power %.0f, times %d", SplashDirections[Math.Clamp(splatter.Direction, 0, 5)],
            SplashCuts[Math.Clamp(splatter.Cut, 0, 6)], splatter.Power, splatter.Times);
    }

    /// <summary>The original's six directions and seven cut types, in its order (the texts of its status line).</summary>
    internal static readonly string[] SplashDirections = ["From front", "From behind", "From left", "From right", "From above", "From below"];

    internal static readonly string[] SplashCuts = ["Default", "Pierced", "Upward", "Thrust", "Rightward", "Leftward", "Downward"];

    /// <summary>
    /// How a splash flies (port choice: a ground mark has only a direction and a size, the original passes these to the game's own splatter): a hit
    /// from the front flings blood backwards; from above all round, from below a pool at the feet; pierced on both sides, a thrust narrow, a sideways
    /// cut turned a quarter, an upward cut smaller and a downward one larger.
    /// </summary>
    static (float Turn, float Spread, float Size, bool BothSides, bool Pool) SplashLook(Splatter s)
    {
        float turn = s.Direction switch { 0 => MathF.PI, 1 => 0, 2 => MathF.PI / 2, 3 => -MathF.PI / 2, _ => 0 };
        float spread = s.Direction == 4 ? 2 * MathF.PI : 1;
        float size = 1;
        switch (s.Cut)
        {
            case 3:
                spread *= 0.3f;
                break;
            case 4:
                turn -= MathF.PI / 2;
                break;
            case 5:
                turn += MathF.PI / 2;
                break;
            case 2:
                size = 0.6f;
                break;
            case 6:
                size = 1.4f;
                break;
        }

        return (turn, spread, size, s.Cut == 1, s.Direction == 5);
    }

    /// <summary>The mod's own random purpose (a splash's spread; display only).</summary>
    internal static RngPurpose Purpose { get; set; }

    static int IndexOf(MedicalState medical, string part)
    {
        for (int i = 0; i < medical.Parts.Count; i++)
        {
            if (medical.Parts[i].Template.StringId == part) return i;
        }

        return -1;
    }
}
