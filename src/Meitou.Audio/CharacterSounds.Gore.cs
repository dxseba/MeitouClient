using System.Numerics;

namespace Meitou.Audio;

public sealed partial class CharacterSounds
{
    int debrisSoundsThisFrame;
    long lastDebrisSound = long.MinValue;
    public int DebrisSounds { get; private set; }
    public int MaterialImpacts { get; private set; }

    /// <summary>Meitou: a robot casing or armour contact takes its material sound; flesh retains the game's impact and pain.</summary>
    public void GoreHit(ulong key, bool heavy, bool attackerUnarmed, bool attackerRobot, bool armoured)
    {
        if (!entries.TryGetValue(key, out var entry)) return;
        if (entry.Profile.Robot)
        {
            Impact(entry, "Metal");
            MaterialImpacts++;
            PainVoice(entry);
        }
        else if (armoured && entry.Profile.ArmourMaterial >= 1)
        {
            player.SetSwitch("Armor_Worn", ArmourName(entry.Profile.ArmourMaterial), entry.Object);
            Impact(entry, "Light_Hit", "Deflection");
            MaterialImpacts++;
            PainVoice(entry);
        }
        else Hit(key, heavy, attackerUnarmed, attackerRobot);
    }

    /// <summary>At most four nearby first/strong bounce contacts per frame, with a global 35ms gap; no sound on settling or pause.</summary>
    public void DebrisLanding(Vector3 at, string material, float speed, int bounce)
    {
        if (!float.IsFinite(speed) || speed < 5 || bounce > 1 || debrisSoundsThisFrame >= 4
            || player.HeardDistanceSquared(at, camera) >= FootstepLimit) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (lastDebrisSound != long.MinValue && System.Diagnostics.Stopwatch.GetElapsedTime(lastDebrisSound, now).TotalSeconds < 0.035) return;
        lastDebrisSound = now;
        debrisSoundsThisFrame++;
        ulong id = pool[poolAt++ % pool.Length];
        player.SetPosition(id, at);
        player.SetSwitch("Inventory_Items", material, id);
        player.Post("Item_Drop", id);
        DebrisSounds++;
    }
}
