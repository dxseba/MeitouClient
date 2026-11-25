using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.DirectControl;

/// <summary>
/// The Action RPG scheme's stance and weapon slot. Sheathed, the controlled character neither swings nor blocks (the fork: attacks and blocks need
/// a drawn weapon); the game drawing the weapon by itself (an enemy close) draws it here too. The slot switch hides the back weapon from the
/// fighter (the weapon in hand is the back one when both arms work, else the hip one), so the hip weapon is in hand; switching again gives the
/// back one back. A fighter made again (new gear) starts on the primary slot.
/// </summary>
sealed partial class ControlSystem
{
    static readonly Action<Fighter, WeaponInstance?>? SetBackWeapon =
        AccessTools.PropertySetter(typeof(Fighter), nameof(Fighter.BackWeapon)) is { } setter
            ? AccessTools.MethodDelegate<Action<Fighter, WeaponInstance?>>(setter)
            : null;

    static readonly Func<Fighter, WeaponKinds>? MeleeDrawn =
        AccessTools.Method(typeof(CombatSystem), "MeleeDrawn") is { } drawn ? AccessTools.MethodDelegate<Func<Fighter, WeaponKinds>>(drawn) : null;

    /// <summary>
    /// The back weapons put aside by the slot switch, by fighter.
    /// </summary>
    readonly ConditionalWeakTable<Fighter, WeaponInstance> _aside = new();

    /// <summary>
    /// The controlled character's weapon is sheathed (Action RPG).
    /// </summary>
    bool _sheathed;

    /// <summary>
    /// For the host: the controlled character wants to step but sits, lies or works at something (get up and go).
    /// </summary>
    public volatile bool Stuck;

    void Stance(World world, StanceCommand stance)
    {
        if (!world.Characters.TryResolveNext(Controlled, out int slot) || world.Characters.Cold(slot) is not { Fighter: { } fighter } cold) return;

        _sheathed = stance.Sheathed;
        if (_sheathed) cold.DrawnWeapon = WeaponKinds.None;
        else if (MeleeDrawn is not null) cold.DrawnWeapon = MeleeDrawn(fighter);
    }

    /// <summary>
    /// The game drew the weapon by itself: the stance follows.
    /// </summary>
    void FollowStance(World world)
    {
        if (!_sheathed || !world.Characters.TryResolveNext(Controlled, out int slot)) return;
        if (world.Characters.Cold(slot) is { DrawnWeapon: not WeaponKinds.None }) _sheathed = false;
    }

    void Swap(World world)
    {
        if (SetBackWeapon is null || !world.Characters.TryResolveNext(Controlled, out int slot)) return;
        if (world.Characters.Cold(slot) is not { Fighter: { HipWeapon: not null } fighter } cold) return;

        if (fighter.BackWeapon is { } back)
        {
            _aside.AddOrUpdate(fighter, back);
            SetBackWeapon(fighter, null);
        }
        else if (_aside.TryGetValue(fighter, out var stored))
        {
            SetBackWeapon(fighter, stored);
            _aside.Remove(fighter);
        }

        if (cold.DrawnWeapon != WeaponKinds.None && MeleeDrawn is not null) cold.DrawnWeapon = MeleeDrawn(fighter);
    }
}
