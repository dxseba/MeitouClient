using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ogre;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

/// <summary>Detaches the original inventory item when a cut removes its hand or intersects its mount.</summary>
internal sealed class StockCutEquipment(CharacterRigs? rigs, Action<CharacterCold> refresh, BodyOptions options) : ITickSystem
{
    static readonly WeaponKinds bow = HandHold.OfCategory((int)WeaponCategory.Bow).Weapon;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly Dictionary<CharacterId, ItemInstance> held = new();
    readonly Dictionary<CharacterId, (int Missing, TorsoBisectionState? Torso)> lost = new();
    readonly ConditionalWeakTable<CharacterCold, RestoredEquipment> checkedLoaded = new();
    long lastSpawn = -1;

    sealed class RestoredEquipment
    {
        public bool Stumps;
        public bool Torso;
    }
    readonly Dictionary<string, RagdollSkinSurface?> skins = new(StringComparer.OrdinalIgnoreCase);

    internal static void Install(ModContext context)
    {
        try
        {
            var assemblyType = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterAssembly", throwOnError: true)!;
            var assembly = Activator.CreateInstance(assemblyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [context.Population], null)!;
            var refresh = AccessTools.Method(assemblyType, "RefreshEquipment", [typeof(CharacterCold)])
                .CreateDelegate<Action<CharacterCold>>(assembly);
            var rigField = AccessTools.Field(typeof(RagdollSystem), "_rigs");
            context.AddSystem("cut-equipment", build => new StockCutEquipment(
                build.Find<RagdollSystem>() is { } ragdolls ? rigField?.GetValue(ragdolls) as CharacterRigs : null,
                refresh, build.Data.BodyOptions), SystemAnchor.AfterPart(StandardParts.Animation));
            context.Log("Brutality stock cut equipment initialized");
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock cut equipment unavailable: {error.GetBaseException().Message}");
        }
        StockCutEquipmentDraw.Install(context);
    }

    public void Schedule(World world)
    {
        held.Clear();
        if (!options.AdvancedRagdolls) return;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
            if (table.Next[slot].Alive && table.Cold(slot) is { DrawnWeapon: not WeaponKinds.None } cold
                && Drawn(cold) is { } item) held[table.IdOf(slot)] = item;
    }

    public void SlowWorld(World world)
    {
        if (!options.AdvancedRagdolls) return;
        lost.Clear();
        var table = world.Characters;
        // Native PopulationSystem consumes these in the next input phase. Leave the native queue intact.
        foreach (var (slot, limb, _) in table.LimbsChanged)
        {
            if (limb is < 0 or > 3 || !table.Next[slot].Alive
                || table.Cold(slot)?.Medical?.LimbPart(limb)?.Limb != LimbState.Stump) continue;
            var owner = table.IdOf(slot);
            var prior = lost.GetValueOrDefault(owner);
            lost[owner] = (prior.Missing | 1 << limb, prior.Torso);
        }
        if (lastSpawn != table.Spawns)
        {
            lastSpawn = table.Spawns;
            for (int slot = 0; slot < table.HighWater; slot++)
            {
                if (!table.Next[slot].Alive || table.Cold(slot) is not
                    { Save: not null, Fighter.IsAnimal: false, Inventory: not null, Medical: { } medical } cold) continue;
                var check = checkedLoaded.GetOrCreateValue(cold);
                if (check.Stumps) continue;
                check.Stumps = true;
                int missing = 0;
                for (int limb = 0; limb < 4; limb++)
                    if (medical.LimbPart(limb)?.Limb == LimbState.Stump) missing |= 1 << limb;
                if (missing == 0) continue;
                var owner = table.IdOf(slot);
                var prior = lost.GetValueOrDefault(owner);
                lost[owner] = (prior.Missing | missing, prior.Torso);
            }
        }
        foreach (var owner in StockTorsoRules.CutOwners(world))
        {
            if (!StockTorsoRules.TryGetSaved(world, owner, out var cut)) continue;
            if (cut.Restoring)
            {
                if (!table.TryResolveNext(owner, out int slot) || table.Cold(slot) is not
                    { Appearance: not null, Fighter.IsAnimal: false, Inventory: not null } cold) continue;
                var check = checkedLoaded.GetOrCreateValue(cold);
                if (check.Torso) continue;
                check.Torso = true;
            }
            else if (cut.Tick != world.Tick) continue;
            lost[owner] = (lost.GetValueOrDefault(owner).Missing, cut.Shape);
        }
        foreach (var (owner, cut) in lost)
            if (table.TryResolveNext(owner, out int slot) && table.Cold(slot) is { Fighter.IsAnimal: false, Inventory: not null } cold)
                DropUnsupported(world, owner, cold, cut.Missing, cut.Torso);
    }

    void DropUnsupported(World world, CharacterId owner, CharacterCold cold, int missing, TorsoBisectionState? torso)
    {
        var inventory = cold.Inventory!;
        var weapons = inventory.Items.Where(item => item.Type is FcsRecordType.Weapon or FcsRecordType.Crossbow
            && item.Section is "hip" or "back").ToArray();
        if (weapons.Length == 0) return;
        var drawn = held.GetValueOrDefault(owner) ?? Drawn(cold);
        var look = cold.Appearance;
        var rig = look is { Animal: null } ? rigs?.For(look)?.Rig : null;
        var skin = torso is not null && look is not null && rig is not null ? Skin(look) : null;
        var cuts = StockLimbRules.For(world, owner);
        bool changed = false;
        foreach (var item in weapons)
        {
            bool inHand = ReferenceEquals(item, drawn);
            if (rig is null || look is null)
            {
                if (inHand && (missing & 2) != 0) Drop(item, "Bip01 R Hand");
                continue;
            }

            string pointName = inHand ? "hands" : item.Section == "back" && item.Y > 0 ? "back2" : item.Section;
            if (!look.AttachmentPoints.TryGetValue(pointName, out var point) || rig.Handle(point.Bone) is not int bone)
            {
                if (inHand && (missing & 2) != 0) Drop(item, "Bip01 R Hand");
                continue;
            }
            var extra = look.Parts.FirstOrDefault(part => part.Record.StringId == item.Record && part.Point == pointName
                && part.Field != "sheath")?.PointOffset ?? default;
            var position = rig.BindPosition[bone] + Vector3.Transform((point.Position + extra) * rig.BindScale[bone].Y,
                Quaternion.Inverse(rig.BindRotationInverse[bone]));
            string support = point.Bone;
            if (inHand && point.Bone == "Bip01 Prop2" && rig.Handle("Bip01 R Hand") is int hand)
            {
                bone = hand;
                support = "Bip01 R Hand";
                position = rig.BindPosition[bone];
            }
            int group = CutWeaponDrops.Group(support);
            int limb = group - 1;
            bool limbLost = limb >= 0 && (missing & (1 << limb)) != 0;
            if (!limbLost && torso is null) continue;

            // A held item uses its actual hand. A sheathed torso weapon uses the closest patch of skin behind the strap.
            var triangle = inHand ? null : skin?.EquipmentSupport(rig, position, group);
            var mount = new CutWeaponMount(group, rig.BindPosition[bone], position,
                triangle?.A ?? position, triangle?.B ?? position, triangle?.C ?? position);
            Vector4? plane = null;
            if (limbLost && cuts?.FirstOrDefault(cut => cut.Shape.LimbSlot == limb).Shape is { IsValid: true } shape
                && LimbCutGeometry.TryPlane(rig, shape, out var value)) plane = value;
            if (CutWeaponDrops.Unsupported(mount, torso, limbLost, plane)) Drop(item, support);
        }
        if (changed)
        {
            refresh(cold);
            if (cold.DrawnWeapon == bow && (cold.Fighter?.Crossbow is null || (missing & 2) != 0))
                cold.DrawnWeapon = WeaponKinds.None;
        }

        void Drop(ItemInstance item, string bone)
        {
            if (!CutWeaponDrops.Drop(world, owner, cold, item, bone)) return;
            changed = true;
            if (trace) Console.WriteLine($"Brutality stock cut weapon owner={owner} weapon={item.Record} bone={bone} tick={world.Tick}");
        }
    }

    RagdollSkinSurface? Skin(CharacterAppearance look)
    {
        if (rigs?.FindMesh(look.BodyMesh) is not { } path) return null;
        if (skins.TryGetValue(path, out var surface)) return surface;
        if (skins.Count >= 24) skins.Remove(skins.Keys.First());
        try { surface = RagdollSkinSurface.Read(OgreMeshReader.ReadFile(path)); }
        catch (Exception error) when (error is OgreFormatException or IOException or InvalidDataException) { }
        return skins[path] = surface;
    }

    static ItemInstance? Drawn(CharacterCold cold)
    {
        if (cold.DrawnWeapon == WeaponKinds.None || cold.Fighter is not { IsAnimal: false } fighter) return null;
        if (cold.DrawnWeapon != bow) return fighter.Weapon is not null ? StockWeaponItems.InSlot(cold, fighter.WeaponIsBack) : null;
        if (fighter.Crossbow is not { } crossbow || cold.Inventory is not { } inventory) return null;
        string section = crossbow.AtBack ? "back" : "hip";
        return inventory.Items.Find(item => item.Type == FcsRecordType.Crossbow && item.Section == section && item.Record == crossbow.Data.StringId);
    }
}
