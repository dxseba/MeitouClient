using System.Numerics;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Items;

namespace Meitou.DirectorPanel;

/// <summary>
/// The features that lean on the game's internals: another's inventory, theft-free moves, weapon stance, idle poses, worn colours, Bloodbath and
/// splitting a character into its own squad. Serial steps only.
/// </summary>
internal sealed partial class DirectorSystem : ICombatHitObserver
{
    /// <summary>The game's drawn melee weapon of a fighter (<c>CombatSystem.MeleeDrawn</c>, internal), bound once.</summary>
    static readonly Func<Fighter, WeaponKinds>? MeleeDrawn = Bind<Func<Fighter, WeaponKinds>>(typeof(CombatSystem), "MeleeDrawn");

    /// <summary>The dialogue's DA_SEPARATE_TO_MY_OWN_SQUAD (<c>DialogueSystem.Separate</c>, internal), bound to the world's system on use.</summary>
    static readonly System.Reflection.MethodInfo? SeparateMethod = AccessTools.Method(typeof(DialogueSystem), "Separate");

    /// <summary>Extra splashes a cutting hit leaves in Bloodbath mode (port choice: the original's count is not known).</summary>
    const int BloodbathSplashes = 3;

    readonly List<CharacterId> _keepReady = [];
    bool _bloodbath;

    static T? Bind<T>(Type type, string name) where T : Delegate =>
        AccessTools.Method(type, name) is { } method ? (T)method.CreateDelegate(typeof(T)) : null;

    bool ApplyExtras(World world, DirectorCommand command)
    {
        switch (command)
        {
            case OpenInventory open:
                Open(world, open);
                return true;
            case SetNoTheft theft:
                Patches.NoTheft = theft.On;
                return true;
            case SetStance stance:
                Stand(world, stance.Character, stance.Stance);
                return true;
            case KeepReady keep:
                _keepReady.Remove(keep.Character);
                if (keep.On) _keepReady.Add(keep.Character);
                return true;
            case SetPose pose:
                Pose(world, pose);
                return true;
            case PaintWorn paint:
                Paint(world, paint);
                return true;
            case SetBloodbath bath:
                _bloodbath = bath.On;
                Say(bath.On ? "ON" : "OFF");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Lets the target's inventory open (<see cref="Patches.OpenTargets"/>) and gives the game's Open Inventory order to the looter: the loot window
    /// opens when it stands beside the target (at once, paused too, when it does already). Closing closes the window and forgets the target.
    /// </summary>
    void Open(World world, OpenInventory open)
    {
        var targets = new HashSet<CharacterId>(Patches.OpenTargets);
        if (open.Close)
        {
            targets.Remove(open.Target);
            Patches.OpenTargets = targets;
            world.Commands.Enqueue(new CloseContainer { Tick = world.Tick });
            return;
        }

        if (Cold(world, open.Target, out _)?.Inventory is null)
        {
            Say("This character has no inventory");
            return;
        }

        targets.Add(open.Target);
        Patches.OpenTargets = targets;
        world.Commands.Enqueue(new OpenLootOrder([open.Looter], open.Target) { Tick = world.Tick });
        Say("Inventory opened");
    }

    void Stand(World world, CharacterId id, Stance stance)
    {
        if (Cold(world, id, out int slot) is not { } cold) return;

        if (stance == Stance.Draw)
        {
            if (cold.Fighter is not { Unarmed: false } fighter || MeleeDrawn is null)
            {
                Say("The character has no weapon equipped");
                return;
            }

            cold.DrawnWeapon = MeleeDrawn(fighter);
            Say("Weapon drawn");
            return;
        }

        cold.DrawnWeapon = WeaponKinds.None;
        _keepReady.Remove(id);
        if (stance == Stance.LeaveCombat) TaskStart.Stand(world.Characters, slot);
        Say(stance == Stance.LeaveCombat ? "Combat mode cleared" : "Weapon sheathed");
    }

    /// <summary>Kept characters draw again (the game sheathes a player's idle character each tick).</summary>
    void KeepWeaponsReady(World world)
    {
        if (MeleeDrawn is null) return;

        foreach (var id in _keepReady)
        {
            if (Cold(world, id, out _) is { Fighter: { Unarmed: false } fighter, DrawnWeapon: WeaponKinds.None } cold) cold.DrawnWeapon = MeleeDrawn(fighter);
        }
    }

    /// <summary>The idle pose: the look's <c>idle stance</c> (only a look that names one; its key is not added, the renderer reads the look).</summary>
    void Pose(World world, SetPose pose)
    {
        if (Cold(world, pose.Character, out _)?.Appearance?.Body?.Record.Strings is not { } strings || !strings.ContainsKey("idle stance"))
        {
            Say("Poses unavailable");
            return;
        }

        strings["idle stance"] = pose.Pose;
        Say("Pose: " + pose.Pose);
    }

    void Paint(World world, PaintWorn paint)
    {
        if (Cold(world, paint.Character, out _) is not { Inventory: { } inventory } cold) return;

        int painted = 0;
        foreach (var item in inventory.Items)
        {
            if (item.Type != Data.Fcs.FcsRecordType.Armour || !InventoryCommands.IsEquipmentSection(item.Section)) continue;

            item.ColourId = paint.Scheme;
            painted++;
        }

        CharacterRefresh.Refresh(data, cold, CharacterRefreshKind.Equipment);
        Say("Items repainted: %d", painted);
    }

    /// <summary>"Separate into own squad": the dialogue's own split (a new squad of the character's faction made where it stands).</summary>
    void Separate(World world, CharacterId id)
    {
        if (SeparateMethod is null || world.Systems.OfType<DialogueSystem>().FirstOrDefault() is not { } dialogue) return;

        try
        {
            SeparateMethod.Invoke(dialogue, [world, id]);
        }
        catch (System.Reflection.TargetInvocationException e)
        {
            Log?.Invoke($"separate failed, the squad keeps the character: {e.InnerException?.Message}");
        }
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!_bloodbath || hit.Damage.Cut <= 0 || world.BloodMarks is not { } marks) return;

        var race = Cold(world, hit.Victim, out _)?.Race;
        uint colour = race?.BloodColour ?? 0x800000;
        var away = hit.Travel.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(hit.Travel.X, hit.Travel.Z)) : Vector2.UnitX;
        for (int i = 0; i < BloodbathSplashes; i++)
        {
            var turn = Vector2.Transform(away, Matrix3x2.CreateRotation((i - 1) * 0.35f));
            marks.Enqueue(new BloodMark(BloodMarkKind.Splash, hit.Victim, hit.VictimPosition, turn, Math.Clamp(hit.Damage.Cut / 50, 0.2f, 1.5f), colour));
        }
    }
}
